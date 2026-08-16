// =============================================================================
// XDCKIT.Extensions.cs - String / byte helpers used by the library internals
// =============================================================================
using System;
using System.Globalization;
using System.Text;

    /// <summary>
    /// Internal-use string / byte / endian helpers shared by XDCKIT.  Public so
    /// power users can call them directly when building custom xbdm payloads.
    /// </summary>
    public static class XboxExtensions
    {
        #region Hex helpers

        /// <summary>Encode an ASCII string as a hex string ("AB" -> "4142").</summary>
        public static string ToHexString(this string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length * 2);
            foreach (char c in text) sb.Append(((byte)c).ToString("X2"));
            return sb.ToString();
        }

        /// <summary>Hex-encode a byte buffer ("AB" bytes -> "4142").</summary>
        public static string ToHexString(this byte[] data)
            => (data == null || data.Length == 0) ? string.Empty : BitConverter.ToString(data).Replace("-", string.Empty);

        /// <summary>Hex-encode a string with explicit encoding (used by consolefeatures RPC).</summary>
        public static string ConvertStringToHex(string input, Encoding encoding)
        {
            var bytes = encoding.GetBytes(input ?? string.Empty);
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        /// <summary>Decode a hex string into bytes ("4142" -> [0x41,0x42]). Tolerant of "-", "0x", whitespace.</summary>
        public static byte[] HexToBytes(string input)
        {
            if (string.IsNullOrEmpty(input)) return new byte[0];
            input = input.Replace(" ", string.Empty)
                         .Replace("-", string.Empty)
                         .Replace("0x", string.Empty)
                         .Replace("0X", string.Empty);
            if ((input.Length & 1) != 0) input = "0" + input;

            var output = new byte[input.Length / 2];
            for (int i = 0; i < output.Length; i++)
                output[i] = Convert.ToByte(input.Substring(i * 2, 2), 16);
            return output;
        }

        /// <summary>Strips NUL bytes that xbdm sometimes appends to text packets.</summary>
        public static string TrimXbdmPadding(this string text)
            => text == null ? null : text.Replace("\0", string.Empty);

        #endregion

        #region xbdm command-line quoting

        /// <summary>
        /// Escape a user-supplied string for inclusion inside an xbdm
        /// <c>key="value"</c> token.  xbdm parses <c>\\</c> as a literal
        /// backslash and <c>\"</c> as a literal quote — every other
        /// character passes through.  Returns the value WITHOUT the
        /// surrounding double quotes; the caller wraps it.
        /// </summary>
        public static string EscapeXbdmString(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            // Fast path: nothing to escape.
            if (value.IndexOfAny(new[] { '\\', '"', '\r', '\n' }) < 0) return value;

            var sb = new StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append('\\').Append('\\'); break;
                    case '"':  sb.Append('\\').Append('"');  break;
                    // xbdm gets confused by raw CR/LF inside a quoted token; drop them.
                    case '\r': case '\n': break;
                    default:   sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Convenience: returns the value already wrapped in <c>"..."</c> with escapes.</summary>
        public static string QuoteXbdm(string value)
            => "\"" + EscapeXbdmString(value) + "\"";

        #endregion

        #region Wide-char (UTF-16 LE) encoding

        /// <summary>Wide-char encoder (1-byte ASCII -> 2-byte WCHAR, NUL terminated).</summary>
        public static byte[] WCHAR(string text)
        {
            if (text == null) text = string.Empty;
            var bytes = new byte[(text.Length * 2) + 2];
            int idx = 1;
            for (int i = 0; i < text.Length; i++) { bytes[idx] = (byte)text[i]; idx += 2; }
            return bytes;
        }

        #endregion

        #region Byte-buffer / endian helpers

        /// <summary>Reverse <paramref name="buffer"/> in groups of <paramref name="groupSize"/> bytes.</summary>
        public static void ReverseBytes(byte[] buffer, int groupSize)
        {
            if (buffer == null || groupSize <= 0) return;
            if (buffer.Length % groupSize != 0)
                throw new ArgumentException("Buffer length must be a multiple of groupSize", nameof(groupSize));

            for (int i = 0; i < buffer.Length; i += groupSize)
                Array.Reverse(buffer, i, groupSize);
        }

        /// <summary>
        /// Read a 32-bit big-endian unsigned integer from a 4-byte buffer.
        /// Used by xbdm bulk-transfer length prefixes (PowerPC byte order).
        /// </summary>
        public static uint ReadUInt32BE(byte[] buffer, int offset = 0)
        {
            if (buffer == null || offset + 4 > buffer.Length)
                throw new ArgumentException("Buffer must contain at least 4 bytes from offset.", nameof(buffer));
            return ((uint)buffer[offset] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                    buffer[offset + 3];
        }

        /// <summary>Convert an int[] to a byte[] (used by the consolefeatures RPC marshaller).</summary>
        public static byte[] IntArrayToByte(int[] iArray)
        {
            if (iArray == null) return new byte[0];
            var bytes = new byte[iArray.Length * 4];
            for (int i = 0; i < iArray.Length; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(iArray[i]), 0, bytes, i * 4, 4);
            return bytes;
        }

        /// <summary>Reinterpret the bits of a uint as a signed int (RPC marshalling).</summary>
        public static int UIntToInt(uint value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        /// <summary>Reinterpret an int's bits as a uint.</summary>
        public static uint IntToUInt(int value) => BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);

        /// <summary>
        /// Converts arbitrary boxed values to a 64-bit representation suitable
        /// for serialising into the consolefeatures RPC args stream.
        /// </summary>
        internal static ulong ConvertToUInt64(object o)
        {
            if (o == null) return 0;
            switch (o)
            {
                case bool b: return b ? 1UL : 0UL;
                case byte by: return by;
                case sbyte sb: return (ulong)sb;
                case short sh: return (ulong)sh;
                case ushort us: return us;
                case int i: return (ulong)i;
                case uint ui: return ui;
                case long l: return (ulong)l;
                case ulong ul: return ul;
                case float f: return (ulong)BitConverter.DoubleToInt64Bits(f);
                case double d: return (ulong)BitConverter.DoubleToInt64Bits(d);
                default: return 0;
            }
        }

        #endregion

        #region Numeric parsing (xbdm response payloads)

        /// <summary>Parse a hex uint tolerant of "0x" prefix and surrounding whitespace.</summary>
        public static uint ParseHexUInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        /// <summary>Parse a hex ulong tolerant of "0x" prefix and surrounding whitespace.</summary>
        public static ulong ParseHexULong(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        #endregion

        #region xbdm key=value parsing (index-based, allocation-light)

        /// <summary>
        /// Locate <c>key=</c> in <paramref name="line"/> and return the
        /// substring of the raw value (without surrounding quotes).  xbdm
        /// escapes (<c>\"</c>, <c>\\</c>) are NOT yet unescaped here —
        /// callers that need the raw text should use
        /// <see cref="ParseKvUnescape"/>.  Out parameters
        /// <paramref name="start"/>/<paramref name="length"/> describe the
        /// slice inside <paramref name="line"/>.  Returns true on hit.
        /// </summary>
        internal static bool TryFindKvSlice(string line, string key, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(key)) return false;

            int searchStart = 0;
            while (searchStart < line.Length)
            {
                int abs = IndexOfIgnoreCase(line, key, searchStart);
                if (abs < 0) return false;
                int end = abs + key.Length;
                if (end >= line.Length || line[end] != '=')
                {
                    searchStart = abs + 1;
                    continue;
                }
                // Match must start at the beginning of a token, not inside another (e.g. "size" inside "sizehi").
                if (abs > 0)
                {
                    char prev = line[abs - 1];
                    if (prev != ' ' && prev != '\t' && prev != '\r' && prev != '\n')
                    {
                        searchStart = abs + 1;
                        continue;
                    }
                }

                int valueStart = end + 1;
                if (valueStart >= line.Length) { start = valueStart; length = 0; return true; }

                if (line[valueStart] == '"')
                {
                    int p = valueStart + 1;
                    while (p < line.Length)
                    {
                        if (line[p] == '\\' && p + 1 < line.Length) { p += 2; continue; }
                        if (line[p] == '"') break;
                        p++;
                    }
                    start = valueStart + 1;
                    length = p - (valueStart + 1);
                    return true;
                }

                int q = valueStart;
                while (q < line.Length)
                {
                    char c = line[q];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') break;
                    q++;
                }
                start = valueStart;
                length = q - valueStart;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Resolve <see cref="TryFindKvSlice"/> and unescape <c>\"</c>/<c>\\</c>
        /// sequences.  Returns null when the key is missing (xbdm convention),
        /// or an empty string when the key is present with no value.
        /// </summary>
        internal static string ParseKvUnescape(string line, string key)
        {
            if (!TryFindKvSlice(line, key, out int start, out int length)) return null;
            if (length == 0) return string.Empty;

            int end = start + length;
            int firstEscape = -1;
            for (int i = start; i < end; i++) if (line[i] == '\\') { firstEscape = i; break; }
            if (firstEscape < 0) return line.Substring(start, length);

            var sb = new StringBuilder(length);
            sb.Append(line, start, firstEscape - start);
            for (int i = firstEscape; i < end; i++)
            {
                char c = line[i];
                if (c == '\\' && i + 1 < end)
                {
                    char next = line[i + 1];
                    if (next == '"' || next == '\\') { sb.Append(next); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Parse <c>key=N</c> as a hex uint using the index scanner.</summary>
        internal static uint ParseKvUIntHex(string line, string key)
        {
            if (!TryFindKvSlice(line, key, out int start, out int length)) return 0;
            if (length == 0) return 0;
            if (length >= 2 && line[start] == '0' && (line[start + 1] == 'x' || line[start + 1] == 'X'))
            {
                start += 2;
                length -= 2;
                if (length == 0) return 0;
            }
            if (uint.TryParse(line.Substring(start, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return v;
            return 0;
        }

        /// <summary>Case-insensitive IndexOf operating on a substring of <paramref name="haystack"/>.</summary>
        private static int IndexOfIgnoreCase(string haystack, string needle, int startIndex)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return -1;
            if (startIndex < 0 || startIndex >= haystack.Length) return -1;
            return haystack.IndexOf(needle, startIndex, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
