using System;
using Xrqr.Json;

namespace Xrqr
{
    /// <summary>The placement hint for the surface a model stands on (section 6.1).</summary>
    public enum Surface
    {
        Floor,
        Wall,
        Any,
    }

    /// <summary>A verified metadata document of format version 1 (section 6.1).</summary>
    public sealed class Metadata
    {
        /// <summary>The metadata format version this client reads.</summary>
        public const int FormatVersion = 1;
        /// <summary>The largest thumbnail, in octets after decoding.</summary>
        public const int ThumbnailMax = 16384;
        /// <summary>The longest title and publisher name, in Unicode scalar values.</summary>
        public const int TextMax = 200;

        const double IntegerMax = 9007199254740992.0;

        public XrqrId Id { get; private set; }
        public uint Version { get; private set; }
        /// <summary>The served model's size in octets.</summary>
        public ulong Bytes { get; private set; }
        /// <summary>The served model's SHA-256.</summary>
        public byte[] Sha256 { get; private set; }
        /// <summary>The resolver's size cap.</summary>
        public ulong MaxBytes { get; private set; }
        public uint PublisherNumber { get; private set; }
        public string PublisherName { get; private set; }
        public string Title { get; private set; }
        /// <summary>The <c>https</c> product or source page, or <c>null</c>.</summary>
        public string Link { get; private set; }
        /// <summary>The WebP thumbnail.</summary>
        public byte[] Thumbnail { get; private set; }
        public Surface Surface { get; private set; }
        /// <summary>The bounding box of the default scene in metres, along glTF axes.</summary>
        public double DimensionX { get; private set; }
        public double DimensionY { get; private set; }
        public double DimensionZ { get; private set; }

        Metadata() { }

        /// <summary>
        /// Verifies a metadata response body for <paramref name="id"/> against the keys of
        /// <paramref name="entry"/>, the resolver it was requested from, with
        /// <paramref name="pinned"/> set for a <c>/v/{n}/meta</c> request (section 7.2). The
        /// signature is checked over the document as received, unknown members included; only
        /// then are the members read. Error kinds, in the order checked: <c>json</c>, <c>sig</c>,
        /// <c>key</c>, <c>signature</c>, <c>format-version</c>, <c>schema</c>, <c>publisher</c>,
        /// <c>id</c>, <c>version</c>.
        /// </summary>
        public static Metadata Verify(ResolverEntry entry, XrqrId id, uint? pinned, byte[] body)
        {
            JsonValue document;
            try
            {
                document = StrictJson.Parse(body);
            }
            catch (JsonFormatException e)
            {
                throw Error("json", e.Message);
            }
            if (!(document is JsonObject members))
                throw Error("json", "not an I-JSON object");
            var sig = members.Get("sig");
            if (sig == null)
                throw Error("sig", "missing sig");
            var kid = Encodings.UnBase64UrlExact(sig["kid"]?.AsString, 8);
            var signature = Encodings.UnBase64UrlExact(sig["value"]?.AsString, 64);
            if (kid == null || signature == null)
                throw Error("sig", "malformed sig");
            var key = entry.FindKey(kid);
            if (key == null)
                throw Error("key", "unknown signing key");
            if (!Ed25519Strict.Verify(key.PublicKey, Jcs.SerializeWithout(members, "sig"), signature))
                throw Error("signature", "bad signature");

            var meta = Read(members);
            if (meta.Id != id)
                throw Error("id", "id differs from the id requested");
            if (pinned.HasValue && pinned.Value != meta.Version)
                throw Error("version", "version differs from the pinned version requested");
            return meta;
        }

        /// <summary>Reads a signed document's members, ignoring <c>sig</c> and unknown members.</summary>
        static Metadata Read(JsonObject doc)
        {
            var v = Integer(doc.Get("v"));
            if (!v.HasValue)
                throw Schema("v");
            if (v.Value != FormatVersion)
                throw Error("format-version", "unsupported metadata format version");

            var meta = new Metadata();
            meta.Id = CanonicalId(doc.Get("id")) ?? throw Schema("id");
            var version = Integer(doc.Get("version"));
            if (!version.HasValue || version.Value < 1 || version.Value > uint.MaxValue)
                throw Schema("version");
            meta.Version = (uint)version.Value;
            meta.Bytes = Positive(doc.Get("bytes")) ?? throw Schema("bytes");
            meta.Sha256 = Encodings.Sha256Hex(doc.Get("sha256")?.AsString) ?? throw Schema("sha256");
            meta.MaxBytes = Positive(doc.Get("max_bytes")) ?? throw Schema("max_bytes");

            var publisher = doc.Get("publisher") ?? throw Schema("publisher");
            var number = Integer(publisher["number"]);
            if (!number.HasValue || number.Value > XrqrId.PublisherMax)
                throw Schema("publisher.number");
            meta.PublisherNumber = (uint)number.Value;
            meta.PublisherName = BoundedText(publisher["name"]) ?? throw Schema("publisher.name");

            meta.Title = BoundedText(doc.Get("title")) ?? throw Schema("title");
            var link = doc.Get("link");
            if (link != null)
                meta.Link = IsLink(link.AsString) ? link.AsString : throw Schema("link");

            var thumbnail = doc.Get("thumbnail") ?? throw Schema("thumbnail");
            if (thumbnail["type"]?.AsString != "image/webp")
                throw Schema("thumbnail.type");
            var data = thumbnail["data"]?.AsString;
            var image = string.IsNullOrEmpty(data) ? null : Encodings.UnBase64Url(data);
            if (image == null || image.Length > ThumbnailMax)
                throw Schema("thumbnail.data");
            meta.Thumbnail = image;

            switch (doc.Get("surface")?.AsString)
            {
                case "floor": meta.Surface = Surface.Floor; break;
                case "wall": meta.Surface = Surface.Wall; break;
                case "any": meta.Surface = Surface.Any; break;
                default: throw Schema("surface");
            }

            var dimensions = doc.Get("dimensions") ?? throw Schema("dimensions");
            meta.DimensionX = Axis(dimensions, "x");
            meta.DimensionY = Axis(dimensions, "y");
            meta.DimensionZ = Axis(dimensions, "z");

            if (meta.PublisherNumber != meta.Id.Publisher)
                throw Error("publisher", "publisher number differs from the id's");
            return meta;
        }

        /// <summary>
        /// Tells whether <paramref name="link"/> is an <c>https</c> URL whose host is not empty,
        /// as the Appendix A pattern <c>^https://[^/?#:@]</c> states.
        /// </summary>
        public static bool IsLink(string link)
        {
            const string scheme = "https://";
            if (link == null || link.Length <= scheme.Length || !link.StartsWith(scheme, StringComparison.Ordinal))
                return false;
            var c = link[scheme.Length];
            return c != '/' && c != '?' && c != '#' && c != ':' && c != '@';
        }

        static double Axis(JsonValue dimensions, string name)
        {
            if (dimensions[name] is JsonNumber n && n.Value >= 0)
                return n.Value;
            throw Schema("dimensions");
        }

        /// <summary>Reads the canonical text of an id: upper case, with no <c>I</c>, <c>L</c>, or <c>O</c>.</summary>
        static XrqrId? CanonicalId(JsonValue value)
        {
            var text = value?.AsString;
            if (text == null)
                return null;
            try
            {
                var id = XrqrId.Decode(text);
                return id.Text == text ? id : (XrqrId?)null;
            }
            catch (XrqrException)
            {
                return null;
            }
        }

        /// <summary>
        /// Reads a JSON integer up to 2^53, accepting a number with a zero fraction as JSON Schema
        /// does.
        /// </summary>
        static ulong? Integer(JsonValue value)
        {
            if (value is JsonNumber n && n.Value >= 0 && n.Value <= IntegerMax && Math.Floor(n.Value) == n.Value)
                return (ulong)n.Value;
            return null;
        }

        static ulong? Positive(JsonValue value)
        {
            var n = Integer(value);
            return n.HasValue && n.Value >= 1 ? n : null;
        }

        static string BoundedText(JsonValue value)
        {
            var text = value?.AsString;
            return text != null && TextRules.Within(text, TextMax) ? text : null;
        }

        static XrqrException Schema(string member) => new XrqrException(Stage.Meta, "schema", "metadata member out of schema: " + member);
        static XrqrException Error(string kind, string message) => new XrqrException(Stage.Meta, kind, message);
    }

    /// <summary>
    /// Model verification (section 7.3): a served model carries no signature of its own and is
    /// bound to its signed metadata by size and hash.
    /// </summary>
    public static class ModelCheck
    {
        /// <summary>
        /// Verifies a model body against the verified metadata of the same request, with
        /// <paramref name="pinned"/> set for a <c>/v/{n}</c> request. Returns the verified
        /// version. Error kinds, in the order checked: <c>version</c>, <c>size</c>, <c>hash</c>.
        /// </summary>
        public static uint Verify(Metadata metadata, uint? pinned, byte[] body)
        {
            if (pinned.HasValue && pinned.Value != metadata.Version)
                throw new XrqrException(Stage.Model, "version", "version differs from the pinned version requested");
            if ((ulong)body.LongLength != metadata.Bytes)
                throw new XrqrException(Stage.Model, "size", "body length differs from the metadata's bytes");
            if (!Encodings.BytesEqual(Encodings.Sha256(body), metadata.Sha256))
                throw new XrqrException(Stage.Model, "hash", "body hash differs from the metadata's sha256");
            return metadata.Version;
        }
    }
}
