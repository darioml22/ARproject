using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Xrqr.Unity
{
    /// <summary>A verified metadata answer from one resolver.</summary>
    public sealed class Candidate
    {
        public ResolverEntry Entry;
        public Metadata Meta;
    }

    /// <summary>Why one resolver gave no verified answer.</summary>
    public sealed class LookupFailure
    {
        public ResolverEntry Entry;
        public string Reason;
    }

    /// <summary>A model that failed to download or verify.</summary>
    public sealed class ModelFetchException : System.Exception
    {
        public ModelFetchException(string message) : base(message) { }
    }

    /// <summary>
    /// The lookup of a scanned id (section 4.2 and Appendix C): every trusted resolver is asked
    /// for the latest metadata in parallel, each answer is verified with the keys of the entry it
    /// was asked from, and the model of a chosen answer is fetched by its pinned path and verified
    /// against that metadata before anything loads it.
    /// </summary>
    public static class XrqrLookup
    {
        /// <summary>The largest metadata body read.</summary>
        public const long MetaLimit = 1 << 20;

        /// <summary>The largest model this client downloads, whatever a resolver allows.</summary>
        public const long ClientModelLimit = 64L << 20;

        public static async Task<(List<Candidate> candidates, List<LookupFailure> failures)> ResolveAsync(
            XrqrId id, IReadOnlyList<ResolverEntry> entries, CancellationToken cancellation)
        {
            var queries = new List<Task<HttpResult>>();
            foreach (var entry in entries)
                queries.Add(XrqrHttp.GetAsync(entry.UrlOf(ResourcePath.Meta(id)), MetaLimit, cancellation));
            await Task.WhenAll(queries);

            var candidates = new List<Candidate>();
            var failures = new List<LookupFailure>();
            for (var i = 0; i < entries.Count; i++)
            {
                var response = queries[i].Result;
                if (!response.Ok)
                {
                    failures.Add(new LookupFailure { Entry = entries[i], Reason = response.Error });
                    continue;
                }
                try
                {
                    candidates.Add(new Candidate { Entry = entries[i], Meta = Metadata.Verify(entries[i], id, null, response.Body) });
                }
                catch (XrqrException e)
                {
                    failures.Add(new LookupFailure { Entry = entries[i], Reason = $"metadata rejected ({e.Kind}): {e.Message}" });
                }
            }
            return (candidates, failures);
        }

        /// <summary>
        /// Fetches the model a candidate's metadata describes from <c>{base}/{id}/v/{version}</c> and
        /// verifies it (section 7.3). Returns the verified GLB octets.
        /// </summary>
        public static async Task<byte[]> FetchModelAsync(Candidate candidate, CancellationToken cancellation)
        {
            var meta = candidate.Meta;
            if (meta.Bytes > meta.MaxBytes)
                throw new ModelFetchException($"the model's {meta.Bytes} octets exceed the resolver's cap of {meta.MaxBytes}");
            if (meta.Bytes > ClientModelLimit)
                throw new ModelFetchException($"the model's {meta.Bytes} octets exceed this client's limit of {ClientModelLimit}");

            var path = ResourcePath.Model(meta.Id, meta.Version);
            var response = await XrqrHttp.GetAsync(candidate.Entry.UrlOf(path), (long)meta.Bytes, cancellation);
            if (!response.Ok)
                throw new ModelFetchException($"model not served: {response.Error}");
            var body = response.Body;
            try
            {
                await Task.Run(() => ModelCheck.Verify(meta, path.Version, body), cancellation);
            }
            catch (XrqrException e)
            {
                throw new ModelFetchException($"model rejected ({e.Kind}): {e.Message}");
            }
            return body;
        }
    }
}
