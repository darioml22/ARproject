using System.Security.Cryptography;
using System.Text;

namespace Xrqr
{
    /// <summary>
    /// The encodings the specification names (section 1.2): canonical <c>base64url</c> without
    /// padding, lowercase hexadecimal, and SHA-256.
    /// </summary>
    public static class Encodings
    {
        const string Base64UrlAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        const string HexDigits = "0123456789abcdef";

        /// <summary>Writes <paramref name="bytes"/> as <c>base64url</c> without padding.</summary>
        public static string Base64Url(byte[] bytes)
        {
            var text = new StringBuilder((bytes.Length * 4 + 2) / 3);
            int acc = 0, bits = 0;
            foreach (var b in bytes)
            {
                acc = (acc << 8) | b;
                bits += 8;
                while (bits >= 6)
                {
                    bits -= 6;
                    text.Append(Base64UrlAlphabet[(acc >> bits) & 63]);
                }
                acc &= (1 << bits) - 1;
            }
            if (bits > 0)
                text.Append(Base64UrlAlphabet[(acc << (6 - bits)) & 63]);
            return text.ToString();
        }

        /// <summary>
        /// Reads canonical <c>base64url</c>: no padding, no other alphabet, and zero unused
        /// trailing bits; or returns <c>null</c>.
        /// </summary>
        public static byte[] UnBase64Url(string text)
        {
            if (text == null || text.Length % 4 == 1)
                return null;
            var output = new byte[text.Length * 3 / 4];
            int acc = 0, bits = 0, o = 0;
            foreach (var c in text)
            {
                var value = Base64UrlValue(c);
                if (value < 0)
                    return null;
                acc = (acc << 6) | value;
                bits += 6;
                if (bits >= 8)
                {
                    bits -= 8;
                    output[o++] = (byte)(acc >> bits);
                    acc &= (1 << bits) - 1;
                }
            }
            return acc == 0 ? output : null;
        }

        /// <summary>Reads canonical <c>base64url</c> of exactly <paramref name="length"/> octets, or returns <c>null</c>.</summary>
        public static byte[] UnBase64UrlExact(string text, int length)
        {
            var bytes = UnBase64Url(text);
            return bytes != null && bytes.Length == length ? bytes : null;
        }

        static int Base64UrlValue(char c)
        {
            if (c >= 'A' && c <= 'Z') return c - 'A';
            if (c >= 'a' && c <= 'z') return c - 'a' + 26;
            if (c >= '0' && c <= '9') return c - '0' + 52;
            if (c == '-') return 62;
            if (c == '_') return 63;
            return -1;
        }

        /// <summary>Writes <paramref name="bytes"/> as lowercase hexadecimal.</summary>
        public static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                text.Append(HexDigits[b >> 4]);
                text.Append(HexDigits[b & 0xF]);
            }
            return text.ToString();
        }

        /// <summary>Reads 64 lowercase hexadecimal digits, or returns <c>null</c>.</summary>
        public static byte[] Sha256Hex(string text)
        {
            if (text == null || text.Length != 64)
                return null;
            var output = new byte[32];
            for (var i = 0; i < 32; i++)
            {
                int high = LowerNibble(text[2 * i]), low = LowerNibble(text[2 * i + 1]);
                if (high < 0 || low < 0)
                    return null;
                output[i] = (byte)((high << 4) | low);
            }
            return output;
        }

        static int LowerNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }

        /// <summary>Returns the SHA-256 of <paramref name="bytes"/>.</summary>
        public static byte[] Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(bytes);
        }

        /// <summary>Compares two octet arrays in constant time for equal lengths.</summary>
        public static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    /// <summary>Text checks the modules share.</summary>
    static class TextRules
    {
        /// <summary>Counts Unicode scalar values, reading a surrogate pair as one.</summary>
        public static int ScalarCount(string text)
        {
            var count = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    i++;
                count++;
            }
            return count;
        }

        /// <summary>Tells whether <paramref name="text"/> holds 1 to <paramref name="max"/> Unicode scalar values.</summary>
        public static bool Within(string text, int max)
        {
            var count = ScalarCount(text);
            return count >= 1 && count <= max;
        }

        /// <summary>Tells whether <paramref name="text"/> starts with the ASCII <paramref name="prefix"/> in either ASCII case.</summary>
        public static bool StartsWithAsciiIgnoreCase(string text, string prefix)
        {
            if (text.Length < prefix.Length)
                return false;
            for (var i = 0; i < prefix.Length; i++)
            {
                if (AsciiLower(text[i]) != AsciiLower(prefix[i]))
                    return false;
            }
            return true;
        }

        static char AsciiLower(char c) => c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
    }
}
