using System;
using System.IO;
using System.Text;

namespace X360GameHack.Core.Formats
{
    internal static class BE
    {
        public static uint U32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        public static ushort U16(byte[] b, int o) => (ushort)(b[o] << 8 | b[o + 1]);
        public static ulong U64(byte[] b, int o) => (ulong)U32(b, o) << 32 | U32(b, o + 4);

        public static void W32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        public static void W16(byte[] b, int o, ushort v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }

        public static void W64(byte[] b, int o, ulong v) { W32(b, o, (uint)(v >> 32)); W32(b, o + 4, (uint)v); }

        /// <summary>Writes a UTF-16 big-endian string into a fixed field, null-padded and (when it fits) null-terminated.</summary>
        public static void WriteUtf16BE(byte[] b, int o, int fieldBytes, string s)
        {
            Array.Clear(b, o, fieldBytes);
            var enc = Encoding.BigEndianUnicode.GetBytes(s ?? "");
            int room = fieldBytes >= 2 ? fieldBytes - 2 : fieldBytes;   // leave room for the terminator when possible
            Buffer.BlockCopy(enc, 0, b, o, Math.Min(enc.Length, room));
        }

        public static uint LE32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);

        public static void WLE32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }

        /// <summary>Null-terminated UTF-16 big-endian string.</summary>
        public static string Utf16BE(byte[] b, int o, int maxBytes)
        {
            int len = 0;
            while (len + 1 < maxBytes && o + len + 1 < b.Length && (b[o + len] != 0 || b[o + len + 1] != 0)) len += 2;
            return Encoding.BigEndianUnicode.GetString(b, o, len).Trim();
        }

        public static string Utf16LE(byte[] b, int o, int maxBytes)
        {
            int len = 0;
            while (len + 1 < maxBytes && o + len + 1 < b.Length && (b[o + len] != 0 || b[o + len + 1] != 0)) len += 2;
            return Encoding.Unicode.GetString(b, o, len).Trim();
        }

        public static string Hex(byte[] b, int o, int count)
        {
            var sb = new StringBuilder(count * 2);
            for (int i = 0; i < count && o + i < b.Length; i++) sb.Append(b[o + i].ToString("X2"));
            return sb.ToString();
        }

        /// <summary>The whole file, read even while the hex editor (or another program) has it open for writing.</summary>
        public static byte[] ReadAllShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length > int.MaxValue - 64) throw new IOException("\"" + Path.GetFileName(path) + "\" is too big to load whole.");
                var buf = new byte[fs.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int r = fs.Read(buf, read, buf.Length - read);
                    if (r <= 0) throw new EndOfStreamException("\"" + Path.GetFileName(path) + "\" got shorter while it was read.");
                    read += r;
                }
                return buf;
            }
        }

        public static byte[] ReadHead(string path, int count)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int n = (int)Math.Min(count, fs.Length);
                var buf = new byte[n];
                int read = 0;
                while (read < n)
                {
                    int r = fs.Read(buf, read, n - read);
                    if (r <= 0) break;
                    read += r;
                }
                return buf;
            }
        }

        public static uint ParseHex32(string text)
        {
            string s = (text ?? "").Trim().Replace(" ", "");
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (s.Length == 0 || s.Length > 8) throw new FormatException("Enter a hex value of up to 8 digits.");
            return Convert.ToUInt32(s, 16);
        }

        public static byte[] ParseHexBytes(string hex)
        {
            var sb = new StringBuilder();
            foreach (char c in hex ?? "")
                if (Uri.IsHexDigit(c)) sb.Append(c);
            string s = sb.ToString();
            if (s.Length % 2 != 0) throw new FormatException("Hex must have an even number of digits.");
            var r = new byte[s.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return r;
        }
    }
}
