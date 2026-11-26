using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Be.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class XexHackerPage : PageBase
    {
        public override string Title => "XEX Disassembler";
        public override string Subtitle => "Hex editor with raw-file and memory-address (base file) views.";
        public override string PageGlyph => Glyph.Edit;

        private readonly HexBox _hex;
        private readonly Segmented _mode = new Segmented("Raw file (editable)", "Memory addresses");
        private readonly XTextBox _jump = new XTextBox("Address (hex)", false, true) { Width = Theme.S(150) };
        private readonly XTextBox _find = new XTextBox("Hex bytes to find, e.g. 38 60 00 01", false, true);
        private readonly XTextBox _replace = new XTextBox("Replace with (same length)", false, true);
        private readonly ListBox _results = new ListBox();
        private readonly TextBlock _pos = new TextBlock("", Theme.Mono, Theme.TextDim);
        private readonly XButton _save;
        private readonly List<long> _hits = new List<long>();

        private OverwriteOnlyProvider _provider;
        private string _openPath;
        private string _openTarget;
        private long _loadAddress;
        private long _lastAddress;

        public XexHackerPage() : base(false)
        {
            var bar = new HStack { Height = Theme.S(36) };
            bar.Add(_mode);
            var reload = Btn("Reload", Glyph.Refresh, () => OpenCurrent());
            _save = Btn("Save", Glyph.Save, Save, BtnKind.Primary, "Write the changes to the XEX (raw mode only).");
            var spacer = new Panel { Width = 10 };
            bar.Add(spacer); bar.FillChild = spacer;
            bar.Add(Btn("Save copy...", Glyph.Download, SaveCopy, tip: "Saves what is shown (in memory-address mode: the decrypted, decompressed base file)."));
            _ghidra = Btn("To Ghidra...", Glyph.Code, ExportGhidra, tip: "Writes a folder Ghidra opens directly: the decoded program plus its sections, entry point, function starts and named imports.");
            bar.Add(_ghidra);
            _jump.Width = Theme.S(100);
            bar.Add(_jump);
            bar.Add(Btn("Jump", Glyph.ChevronRight, Jump));
            bar.Add(reload);
            bar.Add(_save);
            Body.Add(bar);

            var search = new HStack { Height = Theme.S(36) };
            search.Add(_find); search.FillChild = _find;
            search.Add(Btn("Find next", Glyph.Search, FindNext));
            search.Add(Btn("Find all", Glyph.List, FindAll));
            search.Add(_replace); _replace.Width = Theme.S(160);
            search.Add(Btn("Replace all", Glyph.Edit, ReplaceAll, BtnKind.Danger));
            search.Add(Btn("Disassemble", Glyph.Code, Disassemble, tip: "PowerPC instructions of the bytes you selected - or, with nothing selected, of the page on screen. Double-click a line to go there."));
            _decompile = Btn("Decompile", Glyph.Code, Decompile, BtnKind.Primary, "C-style code of the bytes you selected - or, with nothing selected, of every function on the page on screen. Double-click a line to go to its instruction.");
            search.Add(_decompile);
            Body.Add(search);

            var split = _split = new SplitContainer
            {
                Orientation = Orientation.Vertical,
                BackColor = Theme.Border,
                FixedPanel = FixedPanel.Panel2,
                SplitterWidth = Theme.S(6)
            };
            split.Panel1.BackColor = Theme.Input;
            split.Panel2.BackColor = Theme.Surface;
            _hex = new HexBox
            {
                Dock = DockStyle.Fill,
                Font = Theme.Mono,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                InfoForeColor = Theme.Accent,
                SelectionBackColor = Theme.Accent,
                SelectionForeColor = Theme.OnAccent,
                ShadowSelectionColor = System.Drawing.Color.FromArgb(120, Theme.AccentHover),
                BorderStyle = BorderStyle.None,
                StringViewVisible = true,
                LineInfoVisible = true,
                ColumnInfoVisible = true,
                VScrollBarVisible = true,
                UseFixedBytesPerLine = true,
                BytesPerLine = 16,
                GroupSize = 4,
                GroupSeparatorVisible = true
            };
            _hex.SelectionStartChanged += (s, e) => UpdatePos();
            _hex.SelectionLengthChanged += (s, e) => UpdatePos();
            Theme.DarkScrollbars(_hex);
            split.Panel1.Controls.Add(_hex);

            _results.Dock = DockStyle.Fill;
            _results.BackColor = Theme.Surface;
            _results.ForeColor = Theme.Text;
            _results.Font = Theme.Mono;
            _results.BorderStyle = BorderStyle.None;
            _results.IntegralHeight = false;
            Theme.DarkScrollbars(_results);
            _results.DoubleClick += (s, e) => GoToHit();
            _results.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) GoToHit(); };
            var resHead = _resHead = new TextBlock("Results (double-click to go there)", Theme.SmallBold, Theme.TextDim)
            { Dock = DockStyle.Top, Height = Theme.S(26), Flags = TextFormatFlags.VerticalCenter, Padding = new Padding(Theme.S(8), 0, 0, 0) };
            split.Panel2.Controls.Add(_results);
            split.Panel2.Controls.Add(resHead);
            bool placed = false;
            split.SizeChanged += (s, e) =>
            {
                if (placed || split.Width < Theme.S(500)) return;
                placed = true;
                try
                {
                    split.SplitterDistance = split.Width - Theme.S(240);
                    split.Panel2MinSize = Theme.S(160);
                }
                catch { /* size changed mid-layout; the default split still works */ }
            };
            Body.Add(split);
            Body.FillChild = split;

            _pos.Height = Theme.S(22);
            _pos.Flags = TextFormatFlags.VerticalCenter;
            Body.Add(_pos);

            _mode.SelectedIndexChanged += (s, e) => OpenCurrent();
            _jump.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Jump(); } };
            _find.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };
        }

        private bool BaseMode => _mode.SelectedIndex == 1;

        public override void OnActivated()
        {
            if (WorkingFile.Path != null && (_provider == null || WorkingFile.Path != _openPath)) OpenCurrent();
            else if (WorkingFile.Path == null) _pos.Text = "Open a file to start. Any file works in raw mode; base-file mode needs an XEX.";
        }

        public override void OnWorkingFileChanged()
        {
            if (Visible) OpenCurrent();
        }

        /// <summary>Let go of the file when leaving the page so xextool and other tools can modify it.</summary>
        public override void OnDeactivated()
        {
            if (_provider == null) return;
            if (_provider.HasChanges())
            {
                Log.Warn("The hex editor keeps " + Path.GetFileName(_openTarget) + " open because it has unsaved edits.");
                return;
            }
            Release();
            _openPath = null;
        }

        private void Release()
        {
            _hex.ByteProvider = null;
            try { _provider?.Dispose(); } catch { }
            _provider = null;
        }

        private void OpenCurrent()
        {
            string path = WorkingFile.Path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Release(); _openPath = null; return; }
            if (_provider != null && _provider.HasChanges() && _openPath != null &&
                !Dlg.Confirm("Unsaved changes", "Discard the unsaved hex edits?", "Discard", true))
                return;

            Run(null, "Opening in hex editor", async () =>
            {
                string target = path;
                _loadAddress = 0;
                if (BaseMode)
                {
                    if (WorkingFile.Kind != WorkingFileKind.Xex)
                    {
                        Dlg.Warn("Base file mode", "Memory-address mode needs an XEX file.");
                        _mode.SelectedIndex = 0;
                        return;
                    }
                    var x = await Task.Run(() => XexFile.Read(path));
                    await x.LoadToolInfoAsync();
                    target = XexFile.BaseFilePath(path);
                    if (!File.Exists(target)) throw new InvalidOperationException("The base file could not be decoded (" + x.DecodeNote + ").");
                    _loadAddress = x.LoadAddress;
                    _decodeNote = x.DecodeNote;
                }
                Release();
                Exception last = null;
                for (int i = 0; i < 5 && _provider == null; i++)
                {
                    try { _provider = new OverwriteOnlyProvider(new DynamicFileByteProvider(target)); }
                    catch (IOException ex) { last = ex; await Task.Delay(100); }
                }
                if (_provider == null) throw last ?? new IOException("File is locked.");
                _hex.ByteProvider = _provider;
                _hex.LineInfoOffset = _loadAddress;
                _openPath = path;
                _openTarget = target;
                _save.Enabled = !BaseMode;
                _listIsCode = false;
                _results.Items.Clear();
                _hits.Clear();
                long restore = _lastAddress - _loadAddress;
                if (restore > 0 && restore < _provider.Length) { _hex.Select(restore, 1); _hex.ScrollByteIntoView(restore); }
                UpdatePos();
                Log.Info("Hex editor: " + Path.GetFileName(target) + (BaseMode ? " at 0x" + _loadAddress.ToString("X8") : ""));
            });
        }

        private void UpdatePos()
        {
            if (_provider == null) { _pos.Text = ""; return; }
            long off = _hex.SelectionStart;
            _lastAddress = off + _loadAddress;
            string val = "";
            if (off >= 0 && off + 4 <= _provider.Length)
            {
                uint v = (uint)(_provider.ReadByte(off) << 24 | _provider.ReadByte(off + 1) << 16 | _provider.ReadByte(off + 2) << 8 | _provider.ReadByte(off + 3));
                val = $"   u32 BE: 0x{v:X8} ({v})   float: {BitConverter.ToSingle(BitConverter.GetBytes(v), 0):G6}";
            }
            _pos.Text = (BaseMode ? $"Address 0x{off + _loadAddress:X8}   file offset 0x{off:X}" : $"Offset 0x{off:X8}") +
                        $"   selection {_hex.SelectionLength} byte(s)   size {FileSniffer.FormatSize(_provider.Length)}" + val +
                        (_provider.HasChanges() ? "   ● unsaved" : "");
        }

        private string _decodeNote = "";
        private bool _listIsCode;       // the right-hand list shows a disassembly rather than search hits
        private SplitContainer _split;

        private const int MaxCodeBytes = 0x10000;       // 16,384 instructions in one listing

        /// <summary>The bytes the page shows right now: [start, end).</summary>
        private void VisiblePage(out long start, out long end)
        {
            start = -1; end = -1;
            try
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var first = typeof(HexBox).GetField("_startByte", flags); var last = typeof(HexBox).GetField("_endByte", flags);
                if (first != null && last != null) { start = (long)first.GetValue(_hex); end = (long)last.GetValue(_hex) + 1; }
            }
            catch (Exception) { }
            if (start < 0 || end <= start || end > _provider.Length)
            {
                // the control did not say: the lines from the cursor's line on
                int perLine = Math.Max(1, _hex.BytesPerLine);
                start = Math.Max(0, _hex.SelectionStart) / perLine * perLine;
                end = Math.Min(_provider.Length, start + (long)perLine * Math.Max(1, _hex.VerticalByteCount));
            }
        }

        /// <summary>
        /// What Disassemble and Decompile work on: the selected bytes when at least one instruction is selected,
        /// otherwise the page on screen. Offsets into the open file / image, 4-byte aligned.
        /// </summary>
        private bool CodeRange(out long start, out long end, out bool selection)
        {
            selection = _hex.SelectionLength >= 4;
            if (selection) { start = _hex.SelectionStart & ~3L; end = (_hex.SelectionStart + _hex.SelectionLength + 3) & ~3L; }
            else { VisiblePage(out start, out end); start &= ~3L; end = (end + 3) & ~3L; }
            end = Math.Min(end, _provider.Length & ~3L);
            if (end - start > MaxCodeBytes) end = start + MaxCodeBytes;
            return end > start;
        }

        private void Heading(string text) => _resHead.Text = text;

        /// <summary>Lists the PowerPC instructions of the selection - or, with nothing selected, of the page on screen - in the results panel.</summary>
        private void Disassemble()
        {
            if (!Ready()) return;
            if (!CodeRange(out long start, out long end, out bool selection)) { Dlg.Info("Disassemble", "There is nothing here to disassemble."); return; }
            int count = (int)(end - start);
            var data = new byte[count];
            for (int i = 0; i < count; i++) data[i] = _provider.ReadByte(start + i);
            uint address = (uint)(start + _loadAddress);
            _results.BeginUpdate();
            _results.Items.Clear();
            _hits.Clear();
            foreach (var ins in PowerPc.Disassemble(data, 0, count, address))
            {
                _hits.Add(ins.Address - _loadAddress);
                _results.Items.Add(ins.Address.ToString("X8") + "  " + (ins.Operands.Length == 0 ? ins.Mnemonic : ins.Mnemonic.PadRight(8) + " " + ins.Operands));
            }
            _results.EndUpdate();
            // code lines are wider than search hits: give the list room
            try
            {
                int want = Theme.S(440);
                if (_split.Width - _split.SplitterDistance < want && _split.Width > want + Theme.S(420)) _split.SplitterDistance = _split.Width - want;
            }
            catch { /* the default split still works */ }
            _listIsCode = true;
            string what = (selection ? "the selection" : "the page on screen") + ": 0x" + address.ToString("X8") + " - 0x" + ((uint)(end + _loadAddress)).ToString("X8") + ", " + count / 4 + " instructions";
            Heading("Disassembly of " + what + "  (double-click a line to go there)");
            Log.Info("Disassembled " + what + (BaseMode ? "" : " (raw mode: addresses are file offsets)") + ". Select bytes first to disassemble just those.");
        }
        private XButton _ghidra, _decompile;
        private TextBlock _resHead;
        private string _programPath;
        private XexImage _image;
        private XexProgram _program;
        private Dictionary<uint, string> _names;

        /// <summary>The decoded program behind the open XEX, with the names of its imports, exports and helper routines (cached per file).</summary>
        private void LoadProgram(string xex)
        {
            if (_program != null && _programPath == xex) return;
            byte[] file = BE.ReadAllShared(xex);
            var image = XexImage.Load(file);
            var program = XexProgram.Read(file, image);
            var names = new Dictionary<uint, string>();
            foreach (var e in program.Exports) names[e.Address] = e.Label;
            foreach (var i in program.Imports) if (i.IsFunction) names[i.Address] = i.Label;
            foreach (var kv in program.HelperNames(image.BaseFile)) names[kv.Key] = kv.Value;
            _image = image; _program = program; _names = names; _programPath = xex;
        }

        /// <summary>
        /// Decompile: with bytes selected, exactly the selection as C; with nothing selected, every function that
        /// has code on the page on screen, each one whole (half a function would be cut mid-statement).
        /// </summary>
        private void Decompile()
        {
            if (!Ready() || _openPath == null) return;
            if (!BaseMode) { Dlg.Info("Decompile", "Switch to the memory-address view first - that is the decoded program as the console runs it."); return; }
            if (!CodeRange(out long start, out long end, out bool selection)) { Dlg.Info("Decompile", "There is nothing here to decompile."); return; }
            uint from = (uint)(start + _loadAddress), to = (uint)(end + _loadAddress);
            if (selection) DecompileRange(from, to); else DecompilePage(from, to);
        }

        /// <summary>Shows the function that contains <paramref name="address"/> as C-style code in the results panel.</summary>
        public void DecompileAt(uint address) =>
            ShowDecompiled(() =>
            {
                var range = _program.FunctionAt(address, _image.BaseFile);
                if (range == null) throw new InvalidOperationException("There is no code at 0x" + address.ToString("X8") + ".");
                return new List<Tuple<uint, uint>> { range };
            }, address, n => "C code of the function at 0x" + address.ToString("X8"));

        /// <summary>Shows exactly the bytes [from, to) as C-style code (a selection).</summary>
        public void DecompileRange(uint from, uint to) =>
            ShowDecompiled(() => new List<Tuple<uint, uint>> { Tuple.Create(from, to) }, from,
                           n => "C code of the selection: 0x" + from.ToString("X8") + " - 0x" + to.ToString("X8") + ", " + (to - from) / 4 + " instructions");

        /// <summary>Shows every function that has code in [from, to), each one whole.</summary>
        public void DecompilePage(uint from, uint to) =>
            ShowDecompiled(() =>
            {
                var list = new List<Tuple<uint, uint>>();
                uint at = from;
                while (at < to && list.Count < 40)
                {
                    var range = _program.FunctionAt(at, _image.BaseFile);
                    if (range == null || range.Item2 <= at) { at += 4; continue; }
                    if (list.Count == 0 || list[list.Count - 1].Item1 != range.Item1) list.Add(range);
                    at = range.Item2;
                }
                if (list.Count == 0) throw new InvalidOperationException("There is no code on this page.");
                return list;
            }, from, n => "C code of the " + (n == 1 ? "function" : n + " functions") + " on this page (0x" + from.ToString("X8") + " - 0x" + to.ToString("X8") + ")");

        private async void ShowDecompiled(Func<List<Tuple<uint, uint>>> ranges, uint first, Func<int, string> heading)
        {
            string xex = _openPath;
            await RunAsync(_decompile, "Decompiling", async () =>
            {
                try
                {
                    int functions = 0;
                    var parts = await Task.Run(() =>
                    {
                        LoadProgram(xex);
                        byte[] b = _image.BaseFile; uint imageBase = _program.ImageBase;
                        Func<uint, uint?> read = a => a >= imageBase && a - imageBase + 4 <= (uint)b.Length ? (uint?)BE.U32(b, (int)(a - imageBase)) : null;
                        var list = ranges();
                        functions = list.Count;
                        return list.Select(r => PpcFunction.Build(read, r.Item1, r.Item2).Print(a => _names.TryGetValue(a, out string n) ? n : null)).ToList();
                    });
                    if (xex != _openPath) return;
                    _results.BeginUpdate();
                    _results.Items.Clear();
                    _hits.Clear();
                    long lastOffset = first - _loadAddress;
                    int total = 0;
                    foreach (var listing in parts)
                    {
                        if (total > 0) { _hits.Add(lastOffset); _results.Items.Add(""); total++; }
                        string[] lines = listing.Text.TrimEnd('\n').Split('\n');
                        var addressOf = new Dictionary<int, uint>();
                        foreach (var kv in listing.Lines) addressOf[kv.Key] = kv.Value;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (addressOf.TryGetValue(i, out uint at)) lastOffset = at - _loadAddress;
                            _hits.Add(lastOffset);
                            _results.Items.Add(lines[i]);
                            total++;
                        }
                    }
                    _results.EndUpdate();
                    try
                    {
                        // C lines are long: the listing gets most of the width (drag the divider to bring the hex view back)
                        int want = Math.Min(Theme.S(640), _split.Width - Theme.S(300));
                        if (want > 0 && _split.Width - _split.SplitterDistance < want) _split.SplitterDistance = _split.Width - want;
                    }
                    catch { /* the default split still works */ }
                    _listIsCode = true;
                    Heading(heading(functions) + "  (double-click a line to go there)");
                    Log.Info(heading(functions) + " - " + total + " lines. Select bytes first to decompile just those.");
                }
                catch (Exception ex) { Dlg.Warn("Decompile", ex.Message); }
            });
        }
        /// <summary>The C-style text currently listed (for tests and copying).</summary>
        public string ListedText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var item in _results.Items) sb.Append(item).Append('\n');
            return sb.ToString();
        }

        private void ExportGhidra()
        {
            if (!Ready() || _openPath == null) return;
            string parent = PickFolder("Choose where to put the Ghidra folder");
            if (parent != null) ExportGhidraTo(parent);
        }

        /// <summary>Exports the open XEX for Ghidra into a new folder under <paramref name="parent"/>.</summary>
        public async void ExportGhidraTo(string parent)
        {
            string xex = _openPath;
            await RunAsync(_ghidra, "Exporting for Ghidra", async () =>
            {
                try
                {
                    var r = await System.Threading.Tasks.Task.Run(() => XexGhidra.Export(xex, parent));
                    Log.Success("Ghidra export written to " + r.Folder + ": " + r.Program.Sections.Count + " sections, " + r.Program.Functions.Count + " function starts, " +
                                r.Program.Imports.Count + " imports (" + r.Program.Imports.FindAll(i => i.Name != null).Count + " named), " + r.Program.Exports.Count + " exports.");
                    Tools.OpenFolder(r.ReadmeFile);
                }
                catch (Exception ex) { Dlg.Warn("Ghidra export", ex.Message); }
            });
        }

        private void SaveCopy()
        {
            if (!Ready() || _openTarget == null) return;
            if (_provider.HasChanges()) { Dlg.Info("Save copy", "Save or discard your edits first."); return; }
            string name = Path.GetFileNameWithoutExtension(_openPath ?? "file") + (BaseMode ? ".basefile.bin" : Path.GetExtension(_openPath ?? ""));
            string to = PickSave(BaseMode ? "Save the decoded base file" : "Save a copy", name);
            if (to == null) return;
            try
            {
                if (string.Equals(Path.GetFullPath(to), Path.GetFullPath(_openTarget), StringComparison.OrdinalIgnoreCase)) { Dlg.Warn("Save copy", "Choose a different file name."); return; }
                File.Copy(_openTarget, to, true);
                Log.Success("Saved " + (BaseMode ? "the decoded base file (" + _decodeNote + ")" : "a copy") + " to " + to);
            }
            catch (Exception ex) { Dlg.Error("Save copy", ex.Message); }
        }

        private bool Ready()
        {
            if (_provider != null) return true;
            Dlg.Info("Hex editor", "Open a file first.");
            return false;
        }

        private void Jump()
        {
            if (!Ready()) return;
            long value;
            try { value = Convert.ToInt64(_jump.Text.Trim().Replace("0x", "").Replace("0X", ""), 16); }
            catch { Dlg.Warn("Jump", "Enter a hexadecimal address."); return; }
            long off = BaseMode ? value - _loadAddress : value;
            if (off < 0 || off >= _provider.Length)
            {
                Dlg.Warn("Jump", "That address is outside the file." + (BaseMode ? $"\nValid range: 0x{_loadAddress:X8} - 0x{_loadAddress + _provider.Length - 1:X8}" : ""));
                return;
            }
            _hex.Select(off, 1);
            _hex.ScrollByteIntoView(off);
            _hex.Focus();
        }

        private byte[] Pattern(XTextBox box)
        {
            try
            {
                var p = BE.ParseHexBytes(box.Text);
                if (p.Length == 0) throw new FormatException("Enter some hex bytes.");
                return p;
            }
            catch (Exception ex) { Dlg.Warn("Invalid hex", ex.Message); return null; }
        }

        private void FindNext()
        {
            if (!Ready()) return;
            var p = Pattern(_find);
            if (p == null) return;
            long found = _hex.Find(new FindOptions { Type = FindType.Hex, Hex = p });
            if (found == -1) Log.Info("Pattern not found after the cursor.");
            else _hex.Focus();
        }

        /// <summary>Searches the unmodified file on disk in the background, or the edited buffer on the UI thread.</summary>
        private async Task<List<long>> ScanAsync(byte[] p)
        {
            if (!_provider.HasChanges() && File.Exists(_openTarget))
            {
                string file = _openTarget;
                return await Task.Run(() => ScanFile(file, p));
            }
            UseWaitCursor = true;
            try { return Scan(p); }
            finally { UseWaitCursor = false; }
        }

        private static List<long> ScanFile(string path, byte[] p)
        {
            var hits = new List<long>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
            {
                const int chunk = 1 << 20;
                var buf = new byte[chunk + p.Length];
                long start = 0;
                int carry = 0;
                while (true)
                {
                    int n = carry;
                    int r;
                    while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
                    if (n < p.Length) break;
                    for (int i = 0; i + p.Length <= n; i++)
                    {
                        int j = 0;
                        while (j < p.Length && buf[i + j] == p[j]) j++;
                        if (j == p.Length) { hits.Add(start + i); if (hits.Count >= 100000) return hits; }
                    }
                    if (n < buf.Length) break;          // end of file
                    carry = p.Length - 1;               // keep the tail so a match across chunks is found once
                    Buffer.BlockCopy(buf, n - carry, buf, 0, carry);
                    start += n - carry;
                }
            }
            return hits;
        }

        private List<long> Scan(byte[] p)
        {
            var hits = new List<long>();
            long len = _provider.Length;
            const int chunk = 1 << 20;
            var buf = new byte[chunk + p.Length];
            for (long start = 0; start < len; start += chunk)
            {
                int n = (int)Math.Min(buf.Length, len - start);
                for (int i = 0; i < n; i++) buf[i] = _provider.ReadByte(start + i);
                for (int i = 0; i + p.Length <= n && start + i < Math.Min(len, start + chunk); i++)
                {
                    int j = 0;
                    while (j < p.Length && buf[i + j] == p[j]) j++;
                    if (j == p.Length) { hits.Add(start + i); if (hits.Count >= 100000) return hits; }
                }
            }
            return hits;
        }

        private void FindAll()
        {
            if (!Ready()) return;
            var p = Pattern(_find);
            if (p == null) return;
            Run(null, "Searching", async () =>
            {
                var hits = await ScanAsync(p);
                _hits.Clear(); _hits.AddRange(hits);
                _listIsCode = false;
                _results.BeginUpdate();
                _results.Items.Clear();
                foreach (var h in hits) _results.Items.Add(BaseMode ? $"0x{h + _loadAddress:X8}" : $"0x{h:X8}");
                _results.EndUpdate();
                Log.Info(hits.Count + " match(es) for " + _find.Text.Trim());
            });
        }

        private void GoToHit()
        {
            int i = _results.SelectedIndex;
            if (i < 0 || i >= _hits.Count || _provider == null) return;
            // a disassembly line selects its 4-byte instruction; a search hit selects the pattern that was searched for
            int length = _listIsCode ? 4 : Pattern(_find)?.Length ?? 1;
            _hex.Select(_hits[i], length);
            _hex.ScrollByteIntoView(_hits[i]);
            _hex.Focus();
        }

        private void ReplaceAll()
        {
            if (!Ready()) return;
            var f = Pattern(_find);
            var r = f == null ? null : Pattern(_replace);
            if (f == null || r == null) return;
            if (f.Length != r.Length)
            {
                Dlg.Warn("Replace", $"Find is {f.Length} byte(s) and replace is {r.Length}. They must be the same length so the file layout does not shift.");
                return;
            }
            Run(null, "Searching", async () =>
            {
                var hits = await ScanAsync(f);
                if (hits.Count == 0) { Log.Info("No matches to replace."); return; }
                if (!Dlg.Confirm("Replace all", $"Replace {hits.Count} occurrence(s)?", "Replace", true)) return;
                foreach (var h in hits)
                    for (int i = 0; i < r.Length; i++) _provider.WriteByte(h + i, r[i]);
                _hex.Invalidate();
                UpdatePos();
                Log.Success(hits.Count + " occurrence(s) replaced" + (BaseMode ? " in the base-file view (not saved to the XEX)." : ". Click Save to write them."));
            });
        }

        private void Save()
        {
            if (!Ready()) return;
            if (BaseMode) { Dlg.Info("Save", "Base-file view is read-only for the XEX. Switch to raw mode to save patches."); return; }
            if (!_provider.HasChanges()) { Log.Info("No changes to save."); return; }
            var op = WorkingFile.BeginOperation("Saving hex edits");
            if (op == null) return;
            using (op)
            {
                try
                {
                    string path = WorkingFile.Path;
                    BackupOnce(path);
                    _provider.ApplyChanges();
                    _lastAddress = _hex.SelectionStart + _loadAddress;
                    Release();
                    _openPath = null;
                    if (WorkingFile.Kind == WorkingFileKind.Xex)
                    {
                        try
                        {
                            if (!XexFile.HeaderDigestValid(path))
                            {
                                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                                    XexFile.FixHeaderDigest(fs);
                                Log.Info("The XEX header changed - its header digest was recomputed.");
                            }
                        }
                        catch (Exception ex) { Log.Warn("Could not verify the XEX header digest: " + ex.Message); }
                    }
                    Log.Success("Saved hex changes to " + WorkingFile.Name);
                }
                catch (Exception ex) { Dlg.Error("Save failed", ex.Message); }
            }
            WorkingFile.Refresh();   // re-opens the file here and refreshes the other pages
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Release();
            base.Dispose(disposing);
        }
    }
}
