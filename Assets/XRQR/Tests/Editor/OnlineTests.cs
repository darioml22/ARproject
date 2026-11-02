using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Xrqr.Unity;

namespace Xrqr.Tests
{
    /// <summary>
    /// End-to-end lookups against the live proof of concept in the shipped main list. They need
    /// the network, so they run only when selected, for example with
    /// <c>-testFilter Xrqr.Tests.OnlineTests</c>.
    /// </summary>
    [Explicit, Category("Online")]
    public sealed class OnlineTests
    {
        static readonly string[] s_Codes =
        {
            "XRQR:00001JC6TDGVNV34XRA2CW6FBW",
            "XRQR:000017MX4GH2PSSGXKYEJ06QS3",
        };

        /// <summary>The foreign key of the conformance vectors, under the proof of concept's base.</summary>
        const string ForeignEntry =
            "{\"name\":\"Foreign\",\"base\":\"https://xrqr-d3574c.gitlab.io\",\"keys\":[{\"kid\":\"OfcT0KZEJT8\",\"ed25519\":\"PUAXw-hDiVqStwqnTRt-vJyYLM8uxJaMwM1V8Sr0Zgw\"}]}";

        static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted)
                throw task.Exception.GetBaseException();
        }

        [UnityTest]
        public IEnumerator ProofOfConceptCodesResolveVerifyAndLoad()
        {
            yield return Await(ResolveVerifyAndLoad());
        }

        static async Task ResolveVerifyAndLoad()
        {
            var main = ClientState.Load().Main;
            Assert.IsNotEmpty(main, "the shipped main list");
            var holder = new GameObject("XRQR test holder");
            holder.SetActive(false);
            try
            {
                foreach (var code in s_Codes)
                {
                    var (candidates, failures) = await XrqrLookup.ResolveAsync(XrqrCode.Parse(code), main, CancellationToken.None);
                    Assert.AreEqual(1, candidates.Count, code + ": " + string.Join("; ", failures.ConvertAll(f => f.Reason)));
                    var candidate = candidates[0];
                    Assert.AreEqual(candidate.Meta.Id.Publisher, candidate.Meta.PublisherNumber, code);

                    var thumbnail = Thumbnail.Decode(candidate.Meta.Thumbnail);
                    Assert.IsNotNull(thumbnail, code + ": thumbnail");
                    Object.DestroyImmediate(thumbnail);

                    var glb = await XrqrLookup.FetchModelAsync(candidate, CancellationToken.None);
                    Assert.AreEqual(candidate.Meta.Bytes, (ulong)glb.Length, code);
                    using (var model = await ModelLoader.LoadAsync(glb, code, holder.transform, CancellationToken.None))
                        Assert.Greater(model.Template.GetComponentsInChildren<Renderer>(true).Length, 0, code + ": renderers");
                }
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [UnityTest]
        public IEnumerator AnswerSignedByAnotherKeyIsRejected()
        {
            yield return Await(ForeignKeyFails());
        }

        static async Task ForeignKeyFails()
        {
            var entry = ResolverEntry.Parse(ForeignEntry);
            var (candidates, failures) = await XrqrLookup.ResolveAsync(XrqrCode.Parse(s_Codes[0]), new[] { entry }, CancellationToken.None);
            Assert.IsEmpty(candidates);
            StringAssert.Contains("(key)", failures[0].Reason);
        }
    }
}
