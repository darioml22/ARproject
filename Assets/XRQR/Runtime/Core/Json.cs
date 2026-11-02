using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Xrqr.Json
{
    /// <summary>A JSON value read by <see cref="StrictJson"/>.</summary>
    public abstract class JsonValue
    {
        /// <summary>Returns the member <paramref name="name"/> when this is an object holding it, else <c>null</c>.</summary>
        public JsonValue this[string name] => (this as JsonObject)?.Get(name);

        /// <summary>The string, or <c>null</c> for any other value.</summary>
        public string AsString => (this as JsonString)?.Value;
    }

    /// <summary>An object; members keep the order in which they were read.</summary>
    public sealed class JsonObject : JsonValue
    {
        public readonly List<KeyValuePair<string, JsonValue>> Members = new List<KeyValuePair<string, JsonValue>>();

        public JsonValue Get(string name)
        {
            foreach (var member in Members)
            {
                if (string.Equals(member.Key, name, StringComparison.Ordinal))
                    return member.Value;
            }
            return null;
        }

        public JsonObject Add(string name, JsonValue value)
        {
            Members.Add(new KeyValuePair<string, JsonValue>(name, value));
            return this;
        }
    }

    public sealed class JsonArray : JsonValue
    {
        public readonly List<JsonValue> Items = new List<JsonValue>();
    }

    public sealed class JsonString : JsonValue
    {
        public readonly string Value;
        public JsonString(string value) => Value = value;
    }

    /// <summary>A number, held as the nearest double.</summary>
    public sealed class JsonNumber : JsonValue
    {
        public readonly double Value;
        public JsonNumber(double value) => Value = value;
    }

    public sealed class JsonBool : JsonValue
    {
        public static readonly JsonBool True = new JsonBool(true);
        public static readonly JsonBool False = new JsonBool(false);
        public readonly bool Value;
        JsonBool(bool value) => Value = value;
    }

    public sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();
        JsonNull() { }
    }

    /// <summary>Why a text is not I-JSON.</summary>
    public sealed class JsonFormatException : Exception
    {
        public JsonFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// Strict I-JSON reading (RFC 7493): JSON (RFC 8259) in UTF-8 without duplicate member names,
    /// lone surrogates, numbers beyond double precision, or integers above 2^53 in magnitude. An
    /// integer is a number written without fraction or exponent, as the text spells it.
    /// </summary>
    public sealed class StrictJson
    {
        /// <summary>The deepest nesting accepted, as the reference reader allows.</summary>
        const int MaxDepth = 128;

        /// <summary>The decimal digits of 2^53, the largest integer magnitude accepted.</summary>
        const string IntegerMaxDigits = "9007199254740992";

        static readonly UTF8Encoding s_Utf8 = new UTF8Encoding(false, true);

        readonly string m_Text;
        int m_Pos;

        StrictJson(string text) => m_Text = text;

        /// <summary>Parses UTF-8 I-JSON text.</summary>
        public static JsonValue Parse(byte[] utf8)
        {
            string text;
            try
            {
                text = s_Utf8.GetString(utf8);
            }
            catch (ArgumentException)
            {
                throw new JsonFormatException("invalid UTF-8");
            }
            return Parse(text);
        }

        /// <summary>Parses I-JSON text already decoded.</summary>
        public static JsonValue Parse(string text)
        {
            var reader = new StrictJson(text);
            reader.SkipWhitespace();
            var value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (reader.m_Pos != text.Length)
                throw new JsonFormatException("trailing characters");
            return value;
        }

        JsonValue ReadValue(int depth)
        {
            if (depth > MaxDepth)
                throw new JsonFormatException("nesting too deep");
            if (m_Pos >= m_Text.Length)
                throw new JsonFormatException("unexpected end");
            var c = m_Text[m_Pos];
            switch (c)
            {
                case '{': return ReadObject(depth);
                case '[': return ReadArray(depth);
                case '"': return new JsonString(ReadString());
                case 't': Expect("true"); return JsonBool.True;
                case 'f': Expect("false"); return JsonBool.False;
                case 'n': Expect("null"); return JsonNull.Instance;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ReadNumber();
                    throw new JsonFormatException($"unexpected character at {m_Pos}");
            }
        }

        JsonObject ReadObject(int depth)
        {
            m_Pos++;
            var obj = new JsonObject();
            var names = new HashSet<string>(StringComparer.Ordinal);
            SkipWhitespace();
            if (Peek() == '}')
            {
                m_Pos++;
                return obj;
            }
            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                    throw new JsonFormatException($"expected a member name at {m_Pos}");
                var name = ReadString();
                if (!names.Add(name))
                    throw new JsonFormatException($"duplicate member name {name}");
                SkipWhitespace();
                if (Peek() != ':')
                    throw new JsonFormatException($"expected ':' at {m_Pos}");
                m_Pos++;
                SkipWhitespace();
                obj.Add(name, ReadValue(depth + 1));
                SkipWhitespace();
                var next = Peek();
                m_Pos++;
                if (next == '}')
                    return obj;
                if (next != ',')
                    throw new JsonFormatException($"expected ',' or '}}' at {m_Pos - 1}");
            }
        }

        JsonArray ReadArray(int depth)
        {
            m_Pos++;
            var array = new JsonArray();
            SkipWhitespace();
            if (Peek() == ']')
            {
                m_Pos++;
                return array;
            }
            while (true)
            {
                SkipWhitespace();
                array.Items.Add(ReadValue(depth + 1));
                SkipWhitespace();
                var next = Peek();
                m_Pos++;
                if (next == ']')
                    return array;
                if (next != ',')
                    throw new JsonFormatException($"expected ',' or ']' at {m_Pos - 1}");
            }
        }

        string ReadString()
        {
            m_Pos++;
            var text = new StringBuilder();
            while (true)
            {
                if (m_Pos >= m_Text.Length)
                    throw new JsonFormatException("unterminated string");
                var c = m_Text[m_Pos++];
                if (c == '"')
                    return text.ToString();
                if (c < 0x20)
                    throw new JsonFormatException("control character in string");
                if (c != '\\')
                {
                    // Raw surrogates come only in pairs: strict UTF-8 decoding refuses encoded halves.
                    text.Append(c);
                    continue;
                }
                if (m_Pos >= m_Text.Length)
                    throw new JsonFormatException("unterminated escape");
                var e = m_Text[m_Pos++];
                switch (e)
                {
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '/': text.Append('/'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'u':
                        var unit = ReadHex4();
                        if (char.IsLowSurrogate(unit))
                            throw new JsonFormatException("lone surrogate");
                        if (char.IsHighSurrogate(unit))
                        {
                            if (m_Pos + 1 >= m_Text.Length || m_Text[m_Pos] != '\\' || m_Text[m_Pos + 1] != 'u')
                                throw new JsonFormatException("lone surrogate");
                            m_Pos += 2;
                            var low = ReadHex4();
                            if (!char.IsLowSurrogate(low))
                                throw new JsonFormatException("lone surrogate");
                            text.Append(unit).Append(low);
                        }
                        else
                        {
                            text.Append(unit);
                        }
                        break;
                    default:
                        throw new JsonFormatException("invalid escape");
                }
            }
        }

        char ReadHex4()
        {
            if (m_Pos + 4 > m_Text.Length)
                throw new JsonFormatException("truncated \\u escape");
            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                var c = m_Text[m_Pos++];
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                else throw new JsonFormatException("invalid \\u escape");
                value = (value << 4) | digit;
            }
            return (char)value;
        }

        JsonNumber ReadNumber()
        {
            var start = m_Pos;
            if (Peek() == '-')
                m_Pos++;
            var intStart = m_Pos;
            if (Peek() == '0')
            {
                m_Pos++;
            }
            else if (Peek() >= '1' && Peek() <= '9')
            {
                while (IsDigit(Peek()))
                    m_Pos++;
            }
            else
            {
                throw new JsonFormatException($"invalid number at {start}");
            }
            var intEnd = m_Pos;
            var integer = true;
            if (Peek() == '.')
            {
                integer = false;
                m_Pos++;
                if (!IsDigit(Peek()))
                    throw new JsonFormatException($"invalid number at {start}");
                while (IsDigit(Peek()))
                    m_Pos++;
            }
            if (Peek() == 'e' || Peek() == 'E')
            {
                integer = false;
                m_Pos++;
                if (Peek() == '+' || Peek() == '-')
                    m_Pos++;
                if (!IsDigit(Peek()))
                    throw new JsonFormatException($"invalid number at {start}");
                while (IsDigit(Peek()))
                    m_Pos++;
            }
            if (integer && IsLargeInteger(m_Text.Substring(intStart, intEnd - intStart)))
                throw new JsonFormatException("integer beyond 2^53");
            var literal = m_Text.Substring(start, m_Pos - start);
            double value;
            try
            {
                value = double.Parse(literal, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                throw new JsonFormatException("number outside double precision");
            }
            if (double.IsInfinity(value) || double.IsNaN(value))
                throw new JsonFormatException("number outside double precision");
            return new JsonNumber(value);
        }

        static bool IsLargeInteger(string digits)
        {
            var first = 0;
            while (first < digits.Length - 1 && digits[first] == '0')
                first++;
            var significant = digits.Length - first;
            if (significant != IntegerMaxDigits.Length)
                return significant > IntegerMaxDigits.Length;
            return string.CompareOrdinal(digits, first, IntegerMaxDigits, 0, significant) > 0;
        }

        static bool IsDigit(char c) => c >= '0' && c <= '9';

        char Peek() => m_Pos < m_Text.Length ? m_Text[m_Pos] : '\0';

        void Expect(string literal)
        {
            if (m_Pos + literal.Length > m_Text.Length || string.CompareOrdinal(m_Text, m_Pos, literal, 0, literal.Length) != 0)
                throw new JsonFormatException($"invalid literal at {m_Pos}");
            m_Pos += literal.Length;
        }

        void SkipWhitespace()
        {
            while (m_Pos < m_Text.Length)
            {
                var c = m_Text[m_Pos];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    return;
                m_Pos++;
            }
        }
    }

    /// <summary>
    /// The JSON Canonicalization Scheme (RFC 8785): members sorted by UTF-16 code units, strings
    /// escaped as ECMAScript's <c>JSON.stringify</c> escapes them, numbers in ECMAScript form.
    /// </summary>
    public static class Jcs
    {
        static readonly UTF8Encoding s_Utf8 = new UTF8Encoding(false, true);

        /// <summary>Returns the JCS octets of <paramref name="value"/>.</summary>
        public static byte[] Serialize(JsonValue value) => s_Utf8.GetBytes(SerializeToString(value));

        /// <summary>Returns the JCS octets of an object without the member <paramref name="omit"/>.</summary>
        public static byte[] SerializeWithout(JsonObject obj, string omit)
        {
            var trimmed = new JsonObject();
            foreach (var member in obj.Members)
            {
                if (!string.Equals(member.Key, omit, StringComparison.Ordinal))
                    trimmed.Members.Add(member);
            }
            return Serialize(trimmed);
        }

        public static string SerializeToString(JsonValue value)
        {
            var text = new StringBuilder();
            Write(value, text);
            return text.ToString();
        }

        static void Write(JsonValue value, StringBuilder text)
        {
            switch (value)
            {
                case JsonObject obj:
                    var members = new List<KeyValuePair<string, JsonValue>>(obj.Members);
                    members.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                    text.Append('{');
                    for (var i = 0; i < members.Count; i++)
                    {
                        if (i > 0)
                            text.Append(',');
                        WriteString(members[i].Key, text);
                        text.Append(':');
                        Write(members[i].Value, text);
                    }
                    text.Append('}');
                    break;
                case JsonArray array:
                    text.Append('[');
                    for (var i = 0; i < array.Items.Count; i++)
                    {
                        if (i > 0)
                            text.Append(',');
                        Write(array.Items[i], text);
                    }
                    text.Append(']');
                    break;
                case JsonString s:
                    WriteString(s.Value, text);
                    break;
                case JsonNumber n:
                    text.Append(EcmaNumber.Format(n.Value));
                    break;
                case JsonBool b:
                    text.Append(b.Value ? "true" : "false");
                    break;
                case JsonNull _:
                    text.Append("null");
                    break;
                default:
                    throw new ArgumentException("unknown JSON value");
            }
        }

        static void WriteString(string value, StringBuilder text)
        {
            text.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }
    }

    /// <summary>
    /// The ECMAScript Number::toString form of a double (ECMA-262, section 6.1.6.1.20), which
    /// JCS uses: the shortest digits that read back to the same double, laid out in plain or
    /// exponent notation by the magnitude.
    /// </summary>
    public static class EcmaNumber
    {
        public static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("JCS has no form for NaN or infinity");
            if (value == 0)
                return "0";
            if (value < 0)
                return "-" + Format(-value);

            ShortestDigits(value, out var digits, out var exponent);
            var k = digits.Length;
            var n = exponent + 1;
            var text = new StringBuilder();
            if (k <= n && n <= 21)
            {
                text.Append(digits).Append('0', n - k);
            }
            else if (0 < n && n <= 21)
            {
                text.Append(digits, 0, n).Append('.').Append(digits, n, k - n);
            }
            else if (-6 < n && n <= 0)
            {
                text.Append("0.").Append('0', -n).Append(digits);
            }
            else
            {
                var e = n - 1;
                text.Append(digits[0]);
                if (k > 1)
                    text.Append('.').Append(digits, 1, k - 1);
                text.Append('e').Append(e > 0 ? '+' : '-').Append(Math.Abs(e).ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        /// <summary>
        /// Finds the fewest significant digits that read back to <paramref name="value"/>, with the
        /// decimal exponent of the first digit. Each precision is correctly rounded, so the first
        /// that round-trips is also the closest of that length.
        /// </summary>
        static void ShortestDigits(double value, out string digits, out int exponent)
        {
            for (var precision = 1; precision <= 17; precision++)
            {
                var formatted = value.ToString("E" + (precision - 1), CultureInfo.InvariantCulture);
                if (precision < 17 && double.Parse(formatted, NumberStyles.Float, CultureInfo.InvariantCulture) != value)
                    continue;
                var e = formatted.IndexOf('E');
                digits = formatted.Substring(0, e).Replace(".", string.Empty).TrimEnd('0');
                if (digits.Length == 0)
                    digits = "0";
                exponent = int.Parse(formatted.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                return;
            }
            throw new InvalidOperationException("unreachable");
        }
    }
}
