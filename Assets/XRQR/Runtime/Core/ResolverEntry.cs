using System;
using System.Collections.Generic;
using System.Text;
using Xrqr.Json;

namespace Xrqr
{
    /// <summary>One signing key of a resolver, with its key id (section 7.1).</summary>
    public sealed class ResolverKey
    {
        /// <summary>The first 8 octets of the SHA-256 of <see cref="PublicKey"/>.</summary>
        public byte[] Kid { get; }
        /// <summary>The raw 32-octet Ed25519 public key.</summary>
        public byte[] PublicKey { get; }

        public ResolverKey(byte[] publicKey)
        {
            PublicKey = publicKey;
            Kid = KidOf(publicKey);
        }

        public string KidText => Encodings.Base64Url(Kid);

        /// <summary>Returns the key id of a raw public key.</summary>
        public static byte[] KidOf(byte[] publicKey)
        {
            var kid = new byte[8];
            Array.Copy(Encodings.Sha256(publicKey), kid, 8);
            return kid;
        }
    }

    /// <summary>A resolver a client trusts: its name, base URL, and keys (section 4.1).</summary>
    public sealed class ResolverEntry
    {
        /// <summary>The longest resolver name, in Unicode scalar values.</summary>
        public const int NameMax = 100;

        public string Name { get; }
        /// <summary>An absolute <c>https</c> URL without query, fragment, or trailing slash.</summary>
        public string Base { get; }
        public IReadOnlyList<ResolverKey> Keys { get; }

        ResolverEntry(string name, string baseUrl, List<ResolverKey> keys)
        {
            Name = name;
            Base = baseUrl;
            Keys = keys;
        }

        /// <summary>Returns the key with id <paramref name="kid"/>, if the entry lists it.</summary>
        public ResolverKey FindKey(byte[] kid)
        {
            foreach (var key in Keys)
            {
                if (Encodings.BytesEqual(key.Kid, kid))
                    return key;
            }
            return null;
        }

        /// <summary>Returns the absolute URL of <paramref name="resource"/> on this resolver.</summary>
        public string UrlOf(ResourcePath resource) => Base + resource;

        /// <summary>Returns the entry as the JSON object of section 4.1.</summary>
        public JsonObject ToJson()
        {
            var keys = new JsonArray();
            foreach (var key in Keys)
            {
                keys.Items.Add(new JsonObject()
                    .Add("kid", new JsonString(key.KidText))
                    .Add("ed25519", new JsonString(Encodings.Base64Url(key.PublicKey))));
            }
            return new JsonObject()
                .Add("name", new JsonString(Name))
                .Add("base", new JsonString(Base))
                .Add("keys", keys);
        }

        /// <summary>Reads a resolver entry from its JSON text.</summary>
        public static ResolverEntry Parse(string json) => Parse(Encoding.UTF8.GetBytes(json));

        /// <summary>
        /// Reads a resolver entry from its UTF-8 JSON text; unknown members are ignored. Checks,
        /// in order: JSON, name, base, keys, and then per key the key before its <c>kid</c>.
        /// Error kinds: <c>json</c>, <c>name</c>, <c>base</c>, <c>keys</c>, <c>key</c>, <c>kid</c>.
        /// </summary>
        public static ResolverEntry Parse(byte[] json)
        {
            JsonValue value;
            try
            {
                value = StrictJson.Parse(json);
            }
            catch (JsonFormatException e)
            {
                throw Error("json", e.Message);
            }
            var name = value["name"]?.AsString;
            var baseUrl = value["base"]?.AsString;
            if (name == null || baseUrl == null || !(value["keys"] is JsonArray keyArray))
                throw Error("json", "not a resolver entry object");
            var listed = new List<(string kid, string key)>();
            foreach (var item in keyArray.Items)
            {
                string kid = item["kid"]?.AsString, key = item["ed25519"]?.AsString;
                if (kid == null || key == null)
                    throw Error("json", "a key is an object with kid and ed25519 strings");
                listed.Add((kid, key));
            }

            if (!TextRules.Within(name, NameMax))
                throw Error("name", "a resolver name is 1 to 100 characters");
            if (!IsBase(baseUrl))
                throw Error("base", "a resolver base is an https URL without query, fragment, or trailing slash");
            if (listed.Count == 0)
                throw Error("keys", "a resolver entry lists at least one key");
            var keys = new List<ResolverKey>();
            foreach (var (kid, key) in listed)
                keys.Add(ReadKey(kid, key));
            return new ResolverEntry(name, baseUrl, keys);
        }

        static ResolverKey ReadKey(string kidText, string keyText)
        {
            var raw = Encodings.UnBase64UrlExact(keyText, 32);
            if (raw == null || !Ed25519Strict.IsValidPublicKey(raw))
                throw Error("key", "a key is 32 octets of base64url holding an Ed25519 public key not of small order");
            var key = new ResolverKey(raw);
            var kid = Encodings.UnBase64UrlExact(kidText, 8);
            if (kid == null || !Encodings.BytesEqual(kid, key.Kid))
                throw Error("kid", "a kid is the key id of its key");
            return key;
        }

        /// <summary>
        /// Tells whether <paramref name="url"/> is an absolute <c>https</c> URL with a host and
        /// without user information, query, fragment, trailing slash, white space, or control
        /// characters. A port, when present, is decimal.
        /// </summary>
        public static bool IsBase(string url)
        {
            if (!TextRules.StartsWithAsciiIgnoreCase(url, "https://"))
                return false;
            var after = url.Substring("https://".Length);
            var end = after.IndexOfAny(new[] { '/', '?', '#' });
            if (end < 0)
                end = after.Length;
            var authority = after.Substring(0, end);
            var rest = after.Substring(end);
            var at = authority.LastIndexOf('@');
            var hostPort = at < 0 ? authority : authority.Substring(at + 1);
            string host, port;
            if (hostPort.StartsWith("[", StringComparison.Ordinal))
            {
                var close = hostPort.IndexOf(']');
                if (close < 0)
                    return false;
                host = hostPort.Substring(0, close + 1);
                port = hostPort.Substring(close + 1);
            }
            else
            {
                var colon = hostPort.IndexOf(':');
                host = colon < 0 ? hostPort : hostPort.Substring(0, colon);
                port = colon < 0 ? string.Empty : hostPort.Substring(colon);
            }
            if (host.Length == 0 || !IsPort(port))
                return false;
            if (authority.IndexOf('@') >= 0 || url.EndsWith("/", StringComparison.Ordinal) || rest.IndexOfAny(new[] { '?', '#' }) >= 0)
                return false;
            foreach (var c in url)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                    return false;
            }
            return true;
        }

        static bool IsPort(string port)
        {
            if (port.Length == 0)
                return true;
            if (port[0] != ':')
                return false;
            for (var i = 1; i < port.Length; i++)
            {
                if (port[i] < '0' || port[i] > '9')
                    return false;
            }
            return true;
        }

        static XrqrException Error(string kind, string message) => new XrqrException(Stage.Entry, kind, message);
    }
}
