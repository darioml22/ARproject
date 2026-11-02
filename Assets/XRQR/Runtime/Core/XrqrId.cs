using System;

namespace Xrqr
{
    /// <summary>
    /// A 128-bit XRQR id: a UUID version 8 value with a 23-bit publisher number, written as 26
    /// Crockford base32 symbols (specification section 3). Bits are numbered as RFC 9562 numbers
    /// them, from the most significant: the publisher number is bits 0 to 22, the version field
    /// bits 48 to 51, and the variant field bits 64 and 65.
    /// </summary>
    public readonly struct XrqrId : IEquatable<XrqrId>
    {
        /// <summary>The highest publisher number, 2^23 - 1.</summary>
        public const uint PublisherMax = (1u << 23) - 1;

        /// <summary>The length of an id's text form in symbols.</summary>
        public const int TextLength = 26;

        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        // The value, as its high and low 64 bits.
        readonly ulong m_High;
        readonly ulong m_Low;

        XrqrId(ulong high, ulong low)
        {
            m_High = high;
            m_Low = low;
        }

        /// <summary>The publisher number in bits 0 to 22.</summary>
        public uint Publisher => (uint)(m_High >> 41);

        /// <summary>
        /// Builds an id from a publisher number and 16 random octets: the publisher number
        /// overwrites bits 0 to 22, and the version and variant overwrite bits 48 to 51 and 64 to 65.
        /// </summary>
        public static XrqrId FromParts(uint publisher, byte[] random)
        {
            if (publisher > PublisherMax)
                throw new ArgumentOutOfRangeException(nameof(publisher));
            ReadValue(random, out var high, out var low);
            high = (high & ((1UL << 41) - 1)) | ((ulong)publisher << 41);
            high = (high & ~(0xFUL << 12)) | (0x8UL << 12);
            low = (low & ~(0x3UL << 62)) | (0x2UL << 62);
            return new XrqrId(high, low);
        }

        /// <summary>Accepts a 128-bit value, most significant octet first, as an id.</summary>
        public static XrqrId FromBytes(byte[] bytes)
        {
            ReadValue(bytes, out var high, out var low);
            return Checked(high, low, Stage.Id);
        }

        /// <summary>
        /// Decodes the text form of an id, accepting lower case and reading <c>I</c> and <c>L</c>
        /// as 1 and <c>O</c> as 0. Checks, in order: length, symbols, first symbol, version, variant.
        /// </summary>
        public static XrqrId Decode(string text) => Decode(text, Stage.Id);

        internal static XrqrId Decode(string text, Stage stage)
        {
            if (TextRules.ScalarCount(text) != TextLength)
                throw new XrqrException(stage, "length", "an id is 26 symbols long");
            var symbols = new int[TextLength];
            for (var i = 0; i < TextLength; i++)
            {
                // A surrogate pair makes the length differ from 26 characters, so each char here is
                // a scalar value of its own when the count above matched.
                var value = text.Length == TextLength ? SymbolValue(text[i]) : -1;
                if (value < 0)
                    throw new XrqrException(stage, "symbol", "an id holds only Crockford base32 symbols");
                symbols[i] = value;
            }
            if (symbols[0] > 7)
                throw new XrqrException(stage, "first-symbol", "the first symbol of an id is 0 to 7");
            ulong high = 0, low = 0;
            foreach (var s in symbols)
            {
                high = (high << 5) | (low >> 59);
                low = (low << 5) | (uint)s;
            }
            return Checked(high, low, stage);
        }

        static XrqrId Checked(ulong high, ulong low, Stage stage)
        {
            if (((high >> 12) & 0xF) != 0x8)
                throw new XrqrException(stage, "version", "an id is a UUID version 8");
            if (((low >> 62) & 0x3) != 0x2)
                throw new XrqrException(stage, "variant", "an id has the RFC 9562 variant");
            return new XrqrId(high, low);
        }

        static void ReadValue(byte[] bytes, out ulong high, out ulong low)
        {
            if (bytes == null || bytes.Length != 16)
                throw new ArgumentException("an id value is 16 octets", nameof(bytes));
            high = 0;
            low = 0;
            for (var i = 0; i < 8; i++)
            {
                high = (high << 8) | bytes[i];
                low = (low << 8) | bytes[8 + i];
            }
        }

        static int SymbolValue(char c)
        {
            if (c >= 'a' && c <= 'z')
                c = (char)(c - 32);
            switch (c)
            {
                case 'I':
                case 'L':
                    return 1;
                case 'O':
                    return 0;
                default:
                    return c < 128 ? Alphabet.IndexOf(c) : -1;
            }
        }

        /// <summary>Returns the 128-bit value, most significant octet first.</summary>
        public byte[] ToBytes()
        {
            var bytes = new byte[16];
            for (var i = 0; i < 8; i++)
            {
                bytes[7 - i] = (byte)(m_High >> (8 * i));
                bytes[15 - i] = (byte)(m_Low >> (8 * i));
            }
            return bytes;
        }

        /// <summary>Returns the canonical text form: 26 upper-case symbols.</summary>
        public string Text
        {
            get
            {
                var text = new char[TextLength];
                ulong high = m_High, low = m_Low;
                for (var i = TextLength - 1; i >= 0; i--)
                {
                    text[i] = Alphabet[(int)(low & 31)];
                    low = (low >> 5) | (high << 59);
                    high >>= 5;
                }
                return new string(text);
            }
        }

        /// <summary>Returns the value in the hyphenated lower-case UUID form of RFC 9562.</summary>
        public string Uuid
        {
            get
            {
                var hex = Encodings.Hex(ToBytes());
                return $"{hex.Substring(0, 8)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20)}";
            }
        }

        public override string ToString() => Text;
        public bool Equals(XrqrId other) => m_High == other.m_High && m_Low == other.m_Low;
        public override bool Equals(object obj) => obj is XrqrId other && Equals(other);
        public override int GetHashCode() => (m_High ^ (m_Low * 31)).GetHashCode();
        public static bool operator ==(XrqrId a, XrqrId b) => a.Equals(b);
        public static bool operator !=(XrqrId a, XrqrId b) => !a.Equals(b);
    }

    /// <summary>Codes: the text of a QR symbol, <c>XRQR:</c> followed by an id (section 2).</summary>
    public static class XrqrCode
    {
        /// <summary>The scheme, as generators emit it.</summary>
        public const string Scheme = "XRQR";

        /// <summary>
        /// Reads a code, accepting the scheme in any case and the id in any decodable form. No
        /// white space is trimmed.
        /// </summary>
        public static XrqrId Parse(string code)
        {
            if (code == null || !TextRules.StartsWithAsciiIgnoreCase(code, Scheme) || code.Length <= Scheme.Length || code[Scheme.Length] != ':')
                throw new XrqrException(Stage.Code, "scheme", "a code starts with XRQR:");
            return XrqrId.Decode(code.Substring(Scheme.Length + 1), Stage.Code);
        }

        /// <summary>Reads a code, or returns <c>false</c> for any text that is not one.</summary>
        public static bool TryParse(string code, out XrqrId id)
        {
            try
            {
                id = Parse(code);
                return true;
            }
            catch (XrqrException)
            {
                id = default;
                return false;
            }
        }

        /// <summary>Writes the code of <paramref name="id"/> in upper case.</summary>
        public static string Format(XrqrId id) => Scheme + ":" + id.Text;
    }
}
