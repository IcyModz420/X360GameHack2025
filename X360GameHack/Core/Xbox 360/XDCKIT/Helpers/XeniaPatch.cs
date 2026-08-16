// =============================================================================
// XDCKIT.XeniaPatch.cs - Xenia Canary game-patches (.patch.toml) loader / applier
// =============================================================================
// Lets XDCKIT load the same `.patch.toml` files that Xenia Canary's
// game-patches repository ships and apply them to a running title on the
// console.  The patches are address+value tables and the underlying writes
// (big-endian be8/be16/be32/be64, raw arrays, IEEE floats, ASCII / UTF-16
// strings) map cleanly to XDCKIT's typed memory R/W partial.
//
// Source / format documentation:
//   https://github.com/xenia-canary/game-patches
//
// Wire types accepted (matches Xenia's reader):
//   [[patch.be8]]  / [[patch.be16]] / [[patch.be32]] / [[patch.be64]]
//   [[patch.array]]
//   [[patch.f32]]  / [[patch.f64]]
//   [[patch.string]] / [[patch.u16string]]
//
// Usage:
//     var pf = XeniaPatchFile.Load(@"C:\game-patches\454108D8 - Halo 3.patch.toml");
//     int applied = pf.ApplyEnabled(console);     // every is_enabled = true row
//     pf.Apply(console, "60 FPS");                 // by name
//
// Notes:
//  * Xenia patches target the same XEX-loaded virtual addresses xbdm sees,
//    so they apply directly to a running title.  Validate the running
//    title's hash via xbdm `getconsoleid` / `xbeinfo` before applying.
//  * Per Xenia convention is_enabled = false is the default; XDCKIT only
//    applies rows that explicitly opted in.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

    /// <summary>One operation inside a Xenia patch (a single typed write).</summary>
    public enum XeniaPatchOpType
    {
        Unknown,
        Be8,
        Be16,
        Be32,
        Be64,
        Array,
        F32,
        F64,
        String,
        U16String
    }

    /// <summary>Single typed write at a virtual address.  Built from a <c>[[patch.&lt;type&gt;]]</c> sub-table.</summary>
    public sealed class XeniaPatchOperation
    {
        /// <summary>Wire type (Be8 / Be16 / Be32 / Be64 / Array / F32 / F64 / String / U16String).</summary>
        public XeniaPatchOpType Type;

        /// <summary>Virtual address to write to.</summary>
        public uint Address;

        /// <summary>Pre-encoded bytes ready for <see cref="XboxConsole.SetMemory(uint, byte[])"/>.</summary>
        public byte[] Bytes;

        /// <summary>For diagnostics: original TOML <c>value =</c> token (raw text, escapes left intact).</summary>
        public string RawValue;

        /// <summary>Re-encode <see cref="RawValue"/> into <see cref="Bytes"/> using <see cref="Type"/>.  Throws on bad input.</summary>
        public void EncodeFromRawValue()
        {
            Bytes = EncodeValue(Type, RawValue);
        }

        /// <summary>
        /// Apply this single operation to <paramref name="console"/>.  Uses
        /// the right typed writer for the op kind (<c>WriteUInt32</c> for
        /// be32, raw <c>SetMemory</c> for array/string, etc.).
        /// </summary>
        public void Apply(XboxConsole console)
        {
            if (console == null) throw new ArgumentNullException(nameof(console));
            if (Bytes == null) EncodeFromRawValue();
            if (Bytes == null || Bytes.Length == 0) return;
            console.SetMemory(Address, Bytes);
        }

        /// <summary>Encode <paramref name="rawValue"/> as the byte sequence the console will receive.</summary>
        public static byte[] EncodeValue(XeniaPatchOpType type, string rawValue)
        {
            if (rawValue == null) throw new ArgumentNullException(nameof(rawValue));
            switch (type)
            {
                case XeniaPatchOpType.Be8:
                    return new[] { (byte)(MiniToml.ParseScalarLong(rawValue) & 0xFF) };

                case XeniaPatchOpType.Be16:
                {
                    ushort v = (ushort)(MiniToml.ParseScalarLong(rawValue) & 0xFFFF);
                    return new byte[] { (byte)(v >> 8), (byte)v };
                }

                case XeniaPatchOpType.Be32:
                {
                    uint v = (uint)(MiniToml.ParseScalarLong(rawValue) & 0xFFFFFFFFL);
                    return new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
                }

                case XeniaPatchOpType.Be64:
                {
                    ulong v = (ulong)MiniToml.ParseScalarLong(rawValue);
                    var b = new byte[8];
                    for (int i = 7; i >= 0; i--) { b[i] = (byte)(v & 0xFF); v >>= 8; }
                    return b;
                }

                case XeniaPatchOpType.F32:
                {
                    float f = (float)MiniToml.ParseScalarDouble(rawValue);
                    var le = BitConverter.GetBytes(f);
                    Array.Reverse(le);
                    return le;
                }

                case XeniaPatchOpType.F64:
                {
                    double d = MiniToml.ParseScalarDouble(rawValue);
                    var le = BitConverter.GetBytes(d);
                    Array.Reverse(le);
                    return le;
                }

                case XeniaPatchOpType.Array:
                    return XboxExtensions.HexToBytes(MiniToml.ParseScalarString(rawValue));

                case XeniaPatchOpType.String:
                {
                    string s = MiniToml.ParseScalarString(rawValue) ?? string.Empty;
                    return Encoding.UTF8.GetBytes(s);
                }

                case XeniaPatchOpType.U16String:
                {
                    string s = MiniToml.ParseScalarString(rawValue) ?? string.Empty;
                    // Xenia encodes u16string as big-endian UTF-16 in the patched bytes
                    // (Xbox 360 / xbox.exe uses BE UCS-2 for kernel strings).
                    var be = new byte[s.Length * 2];
                    for (int i = 0; i < s.Length; i++)
                    {
                        be[i * 2]     = (byte)(s[i] >> 8);
                        be[i * 2 + 1] = (byte)(s[i] & 0xFF);
                    }
                    return be;
                }

                default:
                    throw new NotSupportedException($"Xenia patch type '{type}' is not supported.");
            }
        }

        public override string ToString()
            => $"{Type} 0x{Address:X8} = {RawValue}";
    }

    /// <summary>One <c>[[patch]]</c> entry — a logical, optionally-enabled patch with metadata and a list of writes.</summary>
    public sealed class XeniaPatch
    {
        public string Name;
        public string Author;
        public string Description;
        public bool IsEnabled;
        public List<XeniaPatchOperation> Operations = new List<XeniaPatchOperation>();

        /// <summary>
        /// Apply every operation in this patch unconditionally.  Returns the
        /// number of writes performed.  Use <see cref="ApplyIfEnabled"/> to
        /// respect <see cref="IsEnabled"/>.
        /// </summary>
        public int Apply(XboxConsole console)
        {
            if (console == null) throw new ArgumentNullException(nameof(console));
            int n = 0;
            foreach (var op in Operations)
            {
                op.Apply(console);
                n++;
            }
            return n;
        }

        /// <summary>Apply only if <see cref="IsEnabled"/> is true.  Returns the number of writes performed.</summary>
        public int ApplyIfEnabled(XboxConsole console)
            => IsEnabled ? Apply(console) : 0;

        public override string ToString()
            => $"{(IsEnabled ? "[X]" : "[ ]")} {Name} ({Operations.Count} writes)";
    }

    /// <summary>
    /// A loaded <c>*.patch.toml</c> Xenia game-patches file.  Holds the
    /// title metadata plus every <c>[[patch]]</c> entry inside.
    /// </summary>
    public sealed class XeniaPatchFile
    {
        public string TitleName;
        public string TitleId;
        public List<string> Hashes = new List<string>();
        public List<string> MediaIds = new List<string>();
        public List<XeniaPatch> Patches = new List<XeniaPatch>();
        public string SourcePath;

        /// <summary>Load and parse a <c>.patch.toml</c> file.</summary>
        public static XeniaPatchFile Load(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Xenia patch file not found.", path);
            var f = Parse(File.ReadAllText(path));
            f.SourcePath = path;
            return f;
        }

        /// <summary>Parse a <c>.patch.toml</c> document already in memory.</summary>
        public static XeniaPatchFile Parse(string text)
        {
            var f = new XeniaPatchFile();
            if (string.IsNullOrEmpty(text)) return f;

            var lines = text.Split('\n');
            XeniaPatch currentPatch = null;
            XeniaPatchOperation currentOp = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = MiniToml.StripComment(lines[i]);
                string line = raw.Trim();
                if (line.Length == 0) continue;

                // Array-of-tables headers ----------------------------------------------
                if (line.StartsWith("[[", StringComparison.Ordinal))
                {
                    int end = line.IndexOf("]]", StringComparison.Ordinal);
                    if (end < 0) continue;
                    string path = line.Substring(2, end - 2).Trim();

                    if (path.Equals("patch", StringComparison.OrdinalIgnoreCase))
                    {
                        currentPatch = new XeniaPatch();
                        currentOp = null;
                        f.Patches.Add(currentPatch);
                        continue;
                    }
                    if (path.StartsWith("patch.", StringComparison.OrdinalIgnoreCase))
                    {
                        string sub = path.Substring("patch.".Length).Trim();
                        currentOp = new XeniaPatchOperation { Type = ParseOpType(sub) };
                        if (currentPatch != null) currentPatch.Operations.Add(currentOp);
                        continue;
                    }
                    // Unknown AOT — leave context alone.
                    continue;
                }

                // Plain table header — Xenia doesn't use these in patches, so reset context defensively.
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    currentOp = null;
                    continue;
                }

                // key = value ----------------------------------------------------------
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                // Multi-line array continuation: hash = [ "A", "B" ]
                if (val.StartsWith("[", StringComparison.Ordinal) &&
                    val.IndexOf(']') < 0)
                {
                    var sb = new StringBuilder(val);
                    while (++i < lines.Length)
                    {
                        string nl = MiniToml.StripComment(lines[i]).Trim();
                        sb.Append(' ').Append(nl);
                        if (nl.IndexOf(']') >= 0) break;
                    }
                    val = sb.ToString();
                }

                if (currentOp != null)
                {
                    if (key.Equals("address", StringComparison.OrdinalIgnoreCase))
                        currentOp.Address = unchecked((uint)MiniToml.ParseScalarLong(val));
                    else if (key.Equals("value", StringComparison.OrdinalIgnoreCase))
                    {
                        currentOp.RawValue = val;
                        try { currentOp.EncodeFromRawValue(); }
                        catch { /* leave Bytes null; caller will see RawValue and Type */ }
                    }
                }
                else if (currentPatch != null)
                {
                    if      (key.Equals("name", StringComparison.OrdinalIgnoreCase))       currentPatch.Name        = MiniToml.ParseScalarString(val);
                    else if (key.Equals("author", StringComparison.OrdinalIgnoreCase))     currentPatch.Author      = MiniToml.ParseScalarString(val);
                    else if (key.Equals("desc", StringComparison.OrdinalIgnoreCase))       currentPatch.Description = MiniToml.ParseScalarString(val);
                    else if (key.Equals("is_enabled", StringComparison.OrdinalIgnoreCase)) currentPatch.IsEnabled   = MiniToml.ParseScalarBool(val);
                }
                else
                {
                    if      (key.Equals("title_name", StringComparison.OrdinalIgnoreCase)) f.TitleName = MiniToml.ParseScalarString(val);
                    else if (key.Equals("title_id", StringComparison.OrdinalIgnoreCase))   f.TitleId   = MiniToml.ParseScalarString(val);
                    else if (key.Equals("hash", StringComparison.OrdinalIgnoreCase))       f.Hashes   .AddRange(MiniToml.ParseStringList(val));
                    else if (key.Equals("media_id", StringComparison.OrdinalIgnoreCase))   f.MediaIds .AddRange(MiniToml.ParseStringList(val));
                }
            }

            return f;
        }

        /// <summary>Find a patch by name (case-insensitive).  Null if not present.</summary>
        public XeniaPatch Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var p in Patches)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        /// <summary>Apply every <c>is_enabled = true</c> patch in order.  Returns the number of writes performed.</summary>
        public int ApplyEnabled(XboxConsole console)
        {
            if (console == null) throw new ArgumentNullException(nameof(console));
            int writes = 0;
            foreach (var p in Patches)
                writes += p.ApplyIfEnabled(console);
            return writes;
        }

        /// <summary>Apply a single patch by name.  Returns true if found and applied.</summary>
        public bool Apply(XboxConsole console, string patchName)
        {
            if (console == null) throw new ArgumentNullException(nameof(console));
            var p = Find(patchName);
            if (p == null) return false;
            p.Apply(console);
            return true;
        }

        private static XeniaPatchOpType ParseOpType(string token)
        {
            switch (token?.Trim().ToLowerInvariant())
            {
                case "be8":       return XeniaPatchOpType.Be8;
                case "be16":      return XeniaPatchOpType.Be16;
                case "be32":      return XeniaPatchOpType.Be32;
                case "be64":      return XeniaPatchOpType.Be64;
                case "array":     return XeniaPatchOpType.Array;
                case "f32":       return XeniaPatchOpType.F32;
                case "f64":       return XeniaPatchOpType.F64;
                case "string":    return XeniaPatchOpType.String;
                case "u16string": return XeniaPatchOpType.U16String;
                default:          return XeniaPatchOpType.Unknown;
            }
        }
    }

    /// <summary>
    /// Per-console facade exposed as <c>console.Patches</c>.  Holds an
    /// in-memory list of <see cref="XeniaPatchFile"/>s the caller has loaded
    /// and offers <see cref="ApplyEnabled"/> across all of them.
    /// </summary>
    public sealed class XboxPatchManager
    {
        private readonly XboxConsole _console;
        private readonly List<XeniaPatchFile> _files = new List<XeniaPatchFile>();

        internal XboxPatchManager(XboxConsole console) { _console = console; }

        /// <summary>All currently-loaded patch files.</summary>
        public IReadOnlyList<XeniaPatchFile> Files => _files;

        /// <summary>Load a single <c>.patch.toml</c> and add it to <see cref="Files"/>.</summary>
        public XeniaPatchFile LoadFile(string path)
        {
            var pf = XeniaPatchFile.Load(path);
            _files.Add(pf);
            return pf;
        }

        /// <summary>Load every <c>*.patch.toml</c> in <paramref name="directory"/> (non-recursive).</summary>
        public IReadOnlyList<XeniaPatchFile> LoadDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return _files;
            foreach (var f in Directory.GetFiles(directory, "*.patch.toml", SearchOption.TopDirectoryOnly))
                LoadFile(f);
            return _files;
        }

        /// <summary>Clear the loaded patch-file list.</summary>
        public void Clear() => _files.Clear();

        /// <summary>Apply every <c>is_enabled = true</c> patch across every loaded file.</summary>
        public int ApplyEnabled()
        {
            int writes = 0;
            foreach (var f in _files) writes += f.ApplyEnabled(_console);
            return writes;
        }

        /// <summary>Apply a single named patch across every loaded file (first match wins).</summary>
        public bool Apply(string patchName)
        {
            foreach (var f in _files)
                if (f.Apply(_console, patchName)) return true;
            return false;
        }
    }
