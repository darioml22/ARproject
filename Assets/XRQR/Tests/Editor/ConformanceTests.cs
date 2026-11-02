using System.IO;
using System.Text;
using NUnit.Framework;
using Xrqr.Json;

namespace Xrqr.Tests
{
    /// <summary>
    /// Runs the XRQR conformance vectors (specification draft 0.7) against the C# core. Each test
    /// covers one vector file and names the failing case in its message. A case's error kind is
    /// compared as well as the rejection itself.
    /// </summary>
    public sealed class ConformanceTests
    {
        const string VectorDirectory = "Assets/XRQR/Tests/Editor/Vectors";

        static JsonValue Load(string file) =>
            StrictJson.Parse(File.ReadAllBytes(Path.Combine(VectorDirectory, file)));

        static JsonArray Cases(JsonValue file, string key) => (JsonArray)file[key];

        static string Text(JsonValue value, string key) => value[key].AsString;

        static string Name(JsonValue c) => Text(c, "name");

        static ResolverEntry Entry() => ResolverEntry.Parse(Jcs.Serialize(Load("keys.json")["entry"]));

        static byte[] Hex(string text)
        {
            var bytes = new byte[text.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = System.Convert.ToByte(text.Substring(2 * i, 2), 16);
            return bytes;
        }

        static void ExpectKind(JsonValue c, System.Action action)
        {
            var e = Assert.Throws<XrqrException>(() => action(), Name(c));
            Assert.AreEqual(Text(c, "error"), e.Kind, Name(c));
        }

        [Test]
        public void Ids()
        {
            var file = Load("ids.json");
            foreach (var c in Cases(file, "valid").Items)
            {
                var publisher = (uint)((JsonNumber)c["publisher"]).Value;
                var built = XrqrId.FromParts(publisher, Hex(Text(c, "random")));
                Assert.AreEqual(Text(c, "text"), built.Text, Name(c));
                Assert.AreEqual(Text(c, "uuid"), built.Uuid, Name(c));
                var read = XrqrId.Decode(Text(c, "text"));
                Assert.AreEqual(built, read, Name(c));
                Assert.AreEqual(publisher, read.Publisher, Name(c));
                Assert.AreEqual(read, XrqrId.FromBytes(read.ToBytes()), Name(c));
            }
            foreach (var c in Cases(file, "alternate").Items)
                Assert.AreEqual(Text(c, "canonical"), XrqrId.Decode(Text(c, "text")).Text, Text(c, "text"));
            foreach (var c in Cases(file, "invalid").Items)
                ExpectKind(c, () => XrqrId.Decode(Text(c, "text")));
        }

        [Test]
        public void Codes()
        {
            var file = Load("codes.json");
            foreach (var c in Cases(file, "valid").Items)
            {
                var id = XrqrCode.Parse(Text(c, "code"));
                Assert.AreEqual(Text(c, "id"), id.Text, Name(c));
                Assert.AreEqual("XRQR:" + Text(c, "id"), XrqrCode.Format(id), Name(c));
            }
            foreach (var c in Cases(file, "invalid").Items)
                ExpectKind(c, () => XrqrCode.Parse(Text(c, "code")));
        }

        [Test]
        public void Paths()
        {
            var file = Load("paths.json");
            foreach (var c in Cases(file, "valid").Items)
            {
                var resource = ResourcePath.Parse(Text(c, "path"));
                Assert.AreEqual(Text(c, "id"), resource.Id.Text, Name(c));
                var version = c["version"] as JsonNumber;
                Assert.AreEqual(version == null ? (uint?)null : (uint)version.Value, resource.Version, Name(c));
                Assert.AreEqual(Text(c, "kind"), resource.Kind == ResourceKind.Meta ? "meta" : "model", Name(c));
                Assert.AreEqual(Text(c, "canonical"), resource.ToString(), Name(c));
            }
            foreach (var c in Cases(file, "invalid").Items)
                ExpectKind(c, () => ResourcePath.Parse(Text(c, "path")));
        }

        [Test]
        public void Entries()
        {
            var file = Load("entries.json");
            foreach (var c in Cases(file, "valid").Items)
            {
                var listed = c["entry"];
                var entry = ResolverEntry.Parse(Jcs.Serialize(listed));
                Assert.AreEqual(Text(listed, "name"), entry.Name, Name(c));
                Assert.AreEqual(Text(listed, "base"), entry.Base, Name(c));
                var keys = (JsonArray)listed["keys"];
                Assert.AreEqual(keys.Items.Count, entry.Keys.Count, Name(c));
                foreach (var key in keys.Items)
                    Assert.IsNotNull(entry.FindKey(Encodings.UnBase64UrlExact(Text(key, "kid"), 8)), Name(c));
            }
            foreach (var c in Cases(file, "invalid").Items)
                ExpectKind(c, () => ResolverEntry.Parse(Jcs.Serialize(c["entry"])));
        }

        [Test]
        public void KeyIds()
        {
            foreach (var c in Cases(Load("keys.json"), "keys").Items)
            {
                var key = Encodings.UnBase64UrlExact(Text(c, "ed25519"), 32);
                Assert.AreEqual(Text(c, "kid"), Encodings.Base64Url(ResolverKey.KidOf(key)), Name(c));
            }
            Assert.IsNull(Encodings.UnBase64UrlExact("If4x36FUom", 8));
            Assert.IsNull(Encodings.UnBase64UrlExact("If4x36FUomE=", 8));
            Assert.IsNull(Encodings.UnBase64UrlExact("If4x36FUom+", 8));
            Assert.IsNull(Encodings.UnBase64UrlExact("If4x36FUomF", 8), "non-zero trailing bits");
        }

        [Test]
        public void MetadataResponses()
        {
            var entry = Entry();
            foreach (var c in Cases(Load("meta.json"), "cases").Items)
            {
                var request = ResourcePath.Parse(Text(c, "request"));
                var body = Encoding.UTF8.GetBytes(Text(c, "document"));
                var expect = c["expect"];
                if (expect == null)
                {
                    ExpectKind(c, () => Metadata.Verify(entry, request.Id, request.Version, body));
                    continue;
                }
                var meta = Metadata.Verify(entry, request.Id, request.Version, body);
                Assert.AreEqual(Text(expect, "id"), meta.Id.Text, Name(c));
                Assert.AreEqual((uint)((JsonNumber)expect["version"]).Value, meta.Version, Name(c));
                Assert.AreEqual((ulong)((JsonNumber)expect["bytes"]).Value, meta.Bytes, Name(c));
                Assert.AreEqual(Text(expect, "sha256"), Encodings.Hex(meta.Sha256), Name(c));
                Assert.AreEqual(Text(expect, "title"), meta.Title, Name(c));
            }
        }

        [Test]
        public void SignedOctetsAreTheJcsForm()
        {
            foreach (var c in Cases(Load("meta.json"), "cases").Items)
            {
                if (c["jcs"] == null)
                    continue;
                var document = (JsonObject)StrictJson.Parse(Encoding.UTF8.GetBytes(Text(c, "document")));
                Assert.AreEqual(Text(c, "jcs"), Encoding.UTF8.GetString(Jcs.SerializeWithout(document, "sig")), Name(c));
            }
        }

        [Test]
        public void ModelResponses()
        {
            var entry = Entry();
            foreach (var c in Cases(Load("model.json"), "cases").Items)
            {
                var request = ResourcePath.Parse(Text(c, "request"));
                var meta = Metadata.Verify(entry, request.Id, null, Encoding.UTF8.GetBytes(Text(c, "metadata")));
                var body = Encodings.UnBase64Url(Text(c, "body"));
                if (c["error"] != null)
                {
                    ExpectKind(c, () => ModelCheck.Verify(meta, request.Version, body));
                    continue;
                }
                Assert.AreEqual((uint)((JsonNumber)c["version"]).Value, ModelCheck.Verify(meta, request.Version, body), Name(c));
            }
        }

        [Test]
        public void Base64UrlRoundTrips()
        {
            for (var length = 0; length < 40; length++)
            {
                var bytes = new byte[length];
                for (var i = 0; i < length; i++)
                    bytes[i] = (byte)(i * 37 + length);
                CollectionAssert.AreEqual(bytes, Encodings.UnBase64Url(Encodings.Base64Url(bytes)), length.ToString());
            }
        }
    }
}
