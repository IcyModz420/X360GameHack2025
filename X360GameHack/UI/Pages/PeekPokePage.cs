using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Be.Windows.Forms;
using Newtonsoft.Json;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public enum PokeType { Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Float, Double, AsciiString, UnicodeString, HexBytes }

    /// <summary>Big-endian (PowerPC) value encoding for Peek Poker.</summary>
    public static class PokeValue
    {
        public static int Size(PokeType t, string value = null)
        {
            switch (t)
            {
                case PokeType.Byte: return 1;
                case PokeType.Int16: case PokeType.UInt16: return 2;
                case PokeType.Int32: case PokeType.UInt32: case PokeType.Float: return 4;
                case PokeType.Int64: case PokeType.UInt64: case PokeType.Double: return 8;
                default: return value == null ? 32 : Encode(t, value).Length;
            }
        }

        private static byte[] Rev(byte[] b) { if (BitConverter.IsLittleEndian) Array.Reverse(b); return b; }

        private static ulong ParseInteger(string s, bool signed, out long sv)
        {
            s = s.Trim();
            sv = 0;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ulong u = Convert.ToUInt64(s.Substring(2), 16);
                sv = (long)u;
                return u;
            }
            if (signed) { sv = long.Parse(s, CultureInfo.InvariantCulture); return (ulong)sv; }
            ulong v = ulong.Parse(s, CultureInfo.InvariantCulture);
            sv = (long)v;
            return v;
        }

        public static byte[] Encode(PokeType t, string s)
        {
            long sv;
            switch (t)
            {
                case PokeType.Byte: ParseInteger(s, false, out sv); return new[] { (byte)sv };
                case PokeType.Int16: ParseInteger(s, true, out sv); return Rev(BitConverter.GetBytes((short)sv));
                case PokeType.UInt16: ParseInteger(s, false, out sv); return Rev(BitConverter.GetBytes((ushort)sv));
                case PokeType.Int32: ParseInteger(s, true, out sv); return Rev(BitConverter.GetBytes((int)sv));
                case PokeType.UInt32: ParseInteger(s, false, out sv); return Rev(BitConverter.GetBytes((uint)sv));
                case PokeType.Int64: ParseInteger(s, true, out sv); return Rev(BitConverter.GetBytes(sv));
                case PokeType.UInt64: return Rev(BitConverter.GetBytes(ParseInteger(s, false, out sv)));
                case PokeType.Float: return Rev(BitConverter.GetBytes(float.Parse(s.Trim(), CultureInfo.InvariantCulture)));
                case PokeType.Double: return Rev(BitConverter.GetBytes(double.Parse(s.Trim(), CultureInfo.InvariantCulture)));
                case PokeType.AsciiString: return Encoding.ASCII.GetBytes(s + "\0");
                case PokeType.UnicodeString: return Encoding.BigEndianUnicode.GetBytes(s + "\0");
                default: return BE.ParseHexBytes(s);
            }
        }

        public static string Decode(PokeType t, byte[] b)
        {
            byte[] c = (byte[])b.Clone();
            switch (t)
            {
                case PokeType.Byte: return c[0].ToString();
                case PokeType.Int16: return BitConverter.ToInt16(Rev(c), 0).ToString();
                case PokeType.UInt16: return BitConverter.ToUInt16(Rev(c), 0).ToString();
                case PokeType.Int32: return BitConverter.ToInt32(Rev(c), 0).ToString();
                case PokeType.UInt32: return BitConverter.ToUInt32(Rev(c), 0).ToString();
                case PokeType.Int64: return BitConverter.ToInt64(Rev(c), 0).ToString();
                case PokeType.UInt64: return BitConverter.ToUInt64(Rev(c), 0).ToString();
                case PokeType.Float: return BitConverter.ToSingle(Rev(c), 0).ToString("R", CultureInfo.InvariantCulture);
                case PokeType.Double: return BitConverter.ToDouble(Rev(c), 0).ToString("R", CultureInfo.InvariantCulture);
                case PokeType.AsciiString: { int z = Array.IndexOf(c, (byte)0); return Encoding.ASCII.GetString(c, 0, z < 0 ? c.Length : z); }
                case PokeType.UnicodeString:
                {
                    int n = 0;
                    while (n + 1 < c.Length && (c[n] != 0 || c[n + 1] != 0)) n += 2;
                    return Encoding.BigEndianUnicode.GetString(c, 0, n);
                }
                default: return BitConverter.ToString(c).Replace("-", " ");
            }
        }
    }

    public sealed class TrainerEntry
    {
        public string Description { get; set; } = "";
        public uint Address { get; set; }
        public PokeType Type { get; set; } = PokeType.UInt32;
        public string Value { get; set; } = "0";
        public bool Frozen { get; set; }
    }

    public sealed class PeekPokePage : PageBase
    {
        public override string Title => "Peek Poker";
        public override string Subtitle => "Live memory viewer and editor - for a console over XBDM, or for a game running in Xenia. Values are big-endian (PowerPC).";
        public override string PageGlyph => Glyph.Memory;

        private readonly XTextBox _addr = new XTextBox("Address, e.g. 82000000", false, true) { Width = Theme.S(170) };
        private readonly XTextBox _len = new XTextBox("Length", false, true) { Width = Theme.S(100) };
        private readonly HexBox _hex;
        private DynamicByteProvider _dyn;
        private OverwriteOnlyProvider _provider;
        private byte[] _original = new byte[0];
        private uint _viewAddress;
        private readonly XCheck _live = new XCheck("Live refresh", false, "Re-read the view twice per second (paused while you have unsent edits).");
        private readonly System.Windows.Forms.Timer _liveTimer = new System.Windows.Forms.Timer { Interval = 500 };

        private readonly XCombo _type = new XCombo { Width = Theme.S(150) };
        private readonly XTextBox _valAddr = new XTextBox("Address", false, true) { Width = Theme.S(140) };
        private readonly XTextBox _value = new XTextBox("Value (decimal, 0x hex, text or bytes)", false, true);

        private readonly ListView _trainer = ListStyle.Create(("Description", 150), ("Address", 90), ("Type", 70), ("Value", 80), ("Frozen", 50));
        private readonly List<TrainerEntry> _entries = new List<TrainerEntry>();
        private readonly System.Windows.Forms.Timer _freezeTimer = new System.Windows.Forms.Timer { Interval = 250 };
        private bool _freezing;

        private readonly XTextBox _scanStart = new XTextBox("Start", false, true) { Width = Theme.S(120) };
        private readonly XTextBox _scanLen = new XTextBox("Length", false, true) { Width = Theme.S(110) };
        private readonly XTextBox _scanValue = new XTextBox("Value to find", false, true);
        private readonly ListView _scanResults = ListStyle.Create(("Address", 120), ("Value", 160));
        private List<uint> _hits = new List<uint>();

        private readonly ListView _regions = ListStyle.Create(("Base", 110), ("Size", 100), ("Protection", 200));
        private readonly Segmented _target = new Segmented("Console (XBDM)", "Xenia (this PC)");
        private readonly TextBlock _targetNote = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(40), Flags = TextFormatFlags.WordBreak };
        private ConnectBar _connect;

        private void ShowTarget()
        {
            bool xenia = MemoryTargets.UseXenia;
            _connect.Visible = !xenia;
            _targetNote.Text = xenia
                ? "Start the game in Xenia (Xenia page > Games), then Peek. Values, tables and text change at once. The game's code and read-only data cannot be written while it runs (Xenia keeps them read-only): code patches go in a Xenia patch file (Xenia Patches page)."
                : "Connect to the console below. xbdm.xex must be running on it (Console Setup > launch.ini).";
            Body.PerformLayout();
        }

        public PeekPokePage()
        {
            // which memory: the console over XBDM, or the game running in Xenia on this PC
            var targetCard = AddCard("Where", "The same tools work on a real console and on a game running in Xenia - addresses are the same in both.", Glyph.Network);
            Field(targetCard, "Memory of", _target).FillChild = null;
            targetCard.Add(_targetNote);
            _target.SelectedIndex = MemoryTargets.UseXenia ? 1 : 0;
            _target.SelectedIndexChanged += (s, e) => { MemoryTargets.UseXenia = _target.SelectedIndex == 1; ShowTarget(); Sync(); };
            // another page can switch the target (Mods uses Xenia when no console is connected): follow it
            Action follow = () => { if (IsDisposed || !IsHandleCreated) return; BeginInvoke((Action)(() => { int want = MemoryTargets.UseXenia ? 1 : 0; if (_target.SelectedIndex != want) _target.SelectedIndex = want; })); };
            MemoryTargets.Changed += follow;
            Disposed += (s, e) => MemoryTargets.Changed -= follow;
            _connect = new ConnectBar();
            Body.Add(_connect);
            ShowTarget();

            // ---------- viewer
            Into = AddSection("Memory view", Glyph.Memory);
            var view = AddCard("Memory view", "Edit bytes in the grid, then Poke to write the changed bytes back.", Glyph.Memory);
            _addr.Text = "82000000";
            _len.Text = "0x200";
            var bar = new WrapPanel();
            bar.Add(_addr); bar.Add(_len);
            bar.Add(Btn("Peek", Glyph.Download, Peek, BtnKind.Primary, "Read memory (Enter)"));
            bar.Add(Btn("Poke changes", Glyph.Upload, Poke, BtnKind.Secondary, "Write the bytes you edited"));
            bar.Add(Btn("", Glyph.ChevronRight, () => Step(+1), tip: "Next block"));
            bar.Add(Btn("Dump to file...", Glyph.Save, Dump));
            bar.Add(_live);
            view.Add(bar);
            _hex = new HexBox
            {
                Font = Theme.Mono, BackColor = Theme.Input, ForeColor = Theme.Text, InfoForeColor = Theme.Accent,
                SelectionBackColor = Theme.Accent, SelectionForeColor = Theme.OnAccent,
                ShadowSelectionColor = System.Drawing.Color.FromArgb(120, Theme.AccentHover),
                BorderStyle = BorderStyle.None, StringViewVisible = true, LineInfoVisible = true, ColumnInfoVisible = true,
                VScrollBarVisible = true, UseFixedBytesPerLine = true, BytesPerLine = 16, GroupSize = 4, GroupSeparatorVisible = true,
                Height = Theme.S(300)
            };
            _hex.SelectionStartChanged += (s, e) => SyncValueAddress();
            Theme.DarkScrollbars(_hex);
            view.Add(_hex);
            _addr.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Peek(); } };
            _len.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Peek(); } };
            _live.CheckedChanged += (s, e) => _liveTimer.Enabled = _live.Checked;
            _liveTimer.Tick += async (s, e) => await LiveTick();

            // ---------- typed values + trainer
            Into = AddSection("Values & trainer", Glyph.Edit);
            var cols = AddColumns(2, 440);
            var typed = AddCard("Read / write a value", null, Glyph.Edit, cols);
            foreach (PokeType t in Enum.GetValues(typeof(PokeType))) _type.Items.Add(t);
            _type.SelectedItem = PokeType.UInt32;
            var r1 = new HStack();
            r1.Add(_valAddr); r1.Add(_type);
            typed.Add(r1);
            Field(typed, null, _value);
            Buttons(typed,
                Btn("Read", Glyph.Download, ReadValue, BtnKind.Secondary),
                Btn("Write", Glyph.Upload, WriteValue, BtnKind.Primary),
                Btn("Add to trainer", Glyph.Add, AddToTrainer));
            Note(typed, "Integers accept decimal or 0x hex. Strings are written with a terminating null. Hex bytes: e.g. 60 00 00 00 (nop).");

            var trainer = AddCard("Trainer / freeze list", "Frozen entries are rewritten every 250 ms.", Glyph.Snow, cols);
            _trainer.Height = Theme.S(180);
            _trainer.CheckBoxes = false;
            trainer.Add(_trainer);
            Buttons(trainer,
                Btn("Freeze / unfreeze", Glyph.Snow, ToggleFreeze),
                Btn("Write once", Glyph.Upload, WriteSelectedOnce),
                Btn("Edit value", Glyph.Edit, EditEntry),
                Btn("Remove", Glyph.Delete, RemoveEntries, BtnKind.Danger));
            Buttons(trainer,
                Btn("Load trainer...", Glyph.FolderOpen, LoadTrainer),
                Btn("Save trainer...", Glyph.Save, SaveTrainer));
            _trainer.DoubleClick += (s, e) => EditEntry();
            _freezeTimer.Tick += async (s, e) => await FreezeTick();

            // ---------- search
            Into = AddSection("Search & regions", Glyph.List);
            var scanCols = AddColumns(2, 440);
            var scan = AddCard("Memory search", "First scan finds every match in the range; next scan keeps only addresses that now hold the new value.", Glyph.Search, scanCols);
            _scanStart.Text = "82000000";
            _scanLen.Text = "0x1000000";
            var sr = new HStack();
            sr.Add(_scanStart); sr.Add(_scanLen); sr.Add(_scanValue); sr.FillChild = _scanValue;
            scan.Add(sr);
            Buttons(scan,
                Btn("First scan", Glyph.Search, () => Scan(true), BtnKind.Primary),
                Btn("Next scan", Glyph.Refresh, () => Scan(false)),
                Btn("Clear", Glyph.Clear, () => { _hits.Clear(); _scanResults.Items.Clear(); }));
            Note(scan, "Uses the type selected in 'Read / write a value'. Double-click a result to view it.");
            _scanResults.Height = Theme.S(170);
            scan.Add(_scanResults);
            _scanResults.DoubleClick += (s, e) =>
            {
                if (_scanResults.SelectedItems.Count == 0) return;
                string a = _scanResults.SelectedItems[0].Text.Replace("0x", "");
                _valAddr.Text = a;
                _addr.Text = a;
                Peek();
            };

            var regions = AddCard("Committed memory regions", "Double-click to view a region.", Glyph.List, scanCols);
            _regions.Height = Theme.S(230);
            regions.Add(_regions);
            regions.AddAction("Load", Glyph.Refresh, (s, e) => LoadRegions());
            _regions.DoubleClick += (s, e) =>
            {
                if (_regions.SelectedItems.Count == 0) return;
                _addr.Text = _regions.SelectedItems[0].Text.Replace("0x", "");
                _scanStart.Text = _addr.Text;
                _scanLen.Text = "0x" + ((uint)_regions.SelectedItems[0].Tag).ToString("X");
                Peek();
            };

            Hooks.ConsoleChanged(this, Sync);
            Sync();
        }

        private void Sync()
        {
            bool on = MemoryTargets.IsReady;
            if (!on) { _live.Checked = false; _freezeTimer.Stop(); }
            else if (_entries.Any(e => e.Frozen)) _freezeTimer.Start();
        }

        private static uint ParseAddr(string text)
        {
            try { return BE.ParseHex32(text); }
            catch { throw new FormatException("Addresses are hexadecimal, e.g. 82000000."); }
        }

        private static uint ParseLength(string text)
        {
            string t = (text ?? "").Trim();
            uint v = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt32(t.Substring(2), 16) : uint.Parse(t, CultureInfo.InvariantCulture);
            if (v == 0) throw new FormatException("Length must be greater than zero.");
            return v;
        }

        private PokeType SelectedType => _type.SelectedItem is PokeType t ? t : PokeType.UInt32;

        private bool Ready()
        {
            if (MemoryTargets.IsReady) return true;
            Dlg.Info("Not connected", MemoryTargets.NotReadyMessage);
            return false;
        }

        // ------------------------------------------------------------- view

        private void Peek()
        {
            if (!Ready()) return;
            uint addr, len;
            try { addr = ParseAddr(_addr.Text); len = ParseLength(_len.Text); }
            catch (Exception ex) { Dlg.Warn("Peek", ex.Message); return; }
            if (len > 0x1000000) { Dlg.Warn("Peek", "Read at most 16 MB at a time (use Dump to file for more)."); return; }
            Run(null, "Peeking 0x" + addr.ToString("X8"), async () =>
            {
                var data = await MemoryTargets.RunAsync(c => c.GetMemory(addr, len));
                ShowBytes(addr, data);
            });
        }

        private void ShowBytes(uint addr, byte[] data)
        {
            long sel = _hex.SelectionStart;
            _viewAddress = addr;
            _original = (byte[])data.Clone();
            _dyn = new DynamicByteProvider(data);
            _provider = new OverwriteOnlyProvider(_dyn);
            _hex.ByteProvider = _provider;
            _hex.LineInfoOffset = addr;
            if (sel >= 0 && sel < data.Length) { _hex.Select(sel, 1); }
        }

        private void Step(int dir)
        {
            uint addr, len;
            try { addr = ParseAddr(_addr.Text); len = ParseLength(_len.Text); }
            catch { return; }
            _addr.Text = (addr + (uint)(dir * (int)len)).ToString("X8");
            Peek();
        }

        private async Task LiveTick()
        {
            if (!MemoryTargets.IsReady || _provider == null || Status.IsBusy) return;
            if (_provider.HasChanges()) return;
            uint addr = _viewAddress, len = (uint)_original.Length;
            try
            {
                var data = await MemoryTargets.RunAsync(c => c.GetMemory(addr, len));
                if (!data.SequenceEqual(_original)) ShowBytes(addr, data);
            }
            catch { _live.Checked = false; }
        }

        private void Poke()
        {
            if (!Ready() || _provider == null) { Dlg.Info("Poke", "Peek some memory first, edit it, then Poke."); return; }
            var cur = _dyn.Bytes.ToArray();
            var runs = new List<(uint Addr, byte[] Data)>();
            int i = 0;
            while (i < cur.Length && i < _original.Length)
            {
                if (cur[i] == _original[i]) { i++; continue; }
                int start = i;
                while (i < cur.Length && i < _original.Length && cur[i] != _original[i]) i++;
                runs.Add(((uint)(_viewAddress + start), cur.Skip(start).Take(i - start).ToArray()));
            }
            if (runs.Count == 0) { Log.Info("No changed bytes to poke."); return; }
            Run(null, "Poking memory", async () =>
            {
                await MemoryTargets.RunAsync(c => { foreach (var r in runs) c.SetMemory(r.Addr, r.Data); });
                foreach (var r in runs) Log.Success($"Poked {r.Data.Length} byte(s) at 0x{r.Addr:X8}");
                _original = cur;
                _provider.ApplyChanges();
            });
        }

        private void Dump()
        {
            if (!Ready()) return;
            uint addr, len;
            try { addr = ParseAddr(_addr.Text); len = ParseLength(_len.Text); }
            catch (Exception ex) { Dlg.Warn("Dump", ex.Message); return; }
            string f = PickSave("Save memory dump", $"mem_{addr:X8}_{len:X}.bin", "Binary|*.bin|All files|*.*");
            if (f == null) return;
            Run(null, "Dumping memory", async () =>
            {
                long missing = await MemoryTargets.RunAsync(c =>
                {
                    long gaps = 0;
                    using (var fs = File.Create(f))
                        for (uint off = 0; off < len; off += 0x10000)
                        {
                            uint n = Math.Min(0x10000, len - off);
                            var holes = new List<KeyValuePair<uint, uint>>();
                            var d = c.GetMemoryWithGaps(addr + off, n, holes);
                            gaps += holes.Sum(h => (long)h.Value);
                            fs.Write(d, 0, d.Length);
                        }
                    return gaps;
                });
                if (missing == 0) Log.Success($"Dumped 0x{len:X} bytes from 0x{addr:X8} to {f}");
                else Log.Warn($"Dumped 0x{len:X} bytes from 0x{addr:X8} to {f} - {missing:N0} byte(s) are not mapped and are zero in the file.");
            });
        }

        private void SyncValueAddress()
        {
            if (_provider == null || _hex.SelectionStart < 0) return;
            _valAddr.Text = (_viewAddress + (uint)_hex.SelectionStart).ToString("X8");
        }

        // ------------------------------------------------------------- typed

        private void ReadValue()
        {
            if (!Ready()) return;
            uint addr;
            try { addr = ParseAddr(_valAddr.Text); }
            catch (Exception ex) { Dlg.Warn("Read", ex.Message); return; }
            var t = SelectedType;
            int size = t == PokeType.AsciiString || t == PokeType.UnicodeString ? 128 : t == PokeType.HexBytes ? 16 : PokeValue.Size(t);
            Run(null, "Reading value", async () =>
            {
                var b = await MemoryTargets.RunAsync(c => c.GetMemory(addr, (uint)size));
                _value.Text = PokeValue.Decode(t, b);
                Log.Info($"0x{addr:X8} ({t}) = {_value.Text}");
            });
        }

        private void WriteValue()
        {
            if (!Ready()) return;
            uint addr;
            byte[] data;
            try { addr = ParseAddr(_valAddr.Text); data = PokeValue.Encode(SelectedType, _value.Text); }
            catch (Exception ex) { Dlg.Warn("Write", ex.Message); return; }
            Run(null, "Writing value", async () =>
            {
                await MemoryTargets.RunAsync(c => c.SetMemory(addr, data));
                Log.Success($"Wrote {data.Length} byte(s) to 0x{addr:X8}");
            });
        }

        // ------------------------------------------------------------- trainer

        private void AddToTrainer()
        {
            uint addr;
            try { addr = ParseAddr(_valAddr.Text); PokeValue.Encode(SelectedType, _value.Text); }
            catch (Exception ex) { Dlg.Warn("Trainer", ex.Message); return; }
            string desc = Dlg.Input("Trainer entry", "Description (e.g. 'Infinite ammo')", "Entry " + (_entries.Count + 1));
            if (desc == null) return;
            _entries.Add(new TrainerEntry { Description = desc, Address = addr, Type = SelectedType, Value = _value.Text });
            FillTrainer();
        }

        private void FillTrainer()
        {
            _trainer.BeginUpdate();
            _trainer.Items.Clear();
            foreach (var e in _entries)
            {
                var li = new ListViewItem(e.Description) { Tag = e };
                li.SubItems.Add("0x" + e.Address.ToString("X8"));
                li.SubItems.Add(e.Type.ToString());
                li.SubItems.Add(e.Value);
                li.SubItems.Add(e.Frozen ? "❄ Yes" : "");
                if (e.Frozen) li.ForeColor = Theme.Info;
                _trainer.Items.Add(li);
            }
            _trainer.EndUpdate();
            if (_entries.Any(x => x.Frozen) && MemoryTargets.IsReady) _freezeTimer.Start(); else _freezeTimer.Stop();
        }

        private List<TrainerEntry> SelectedEntries() => _trainer.SelectedItems.Cast<ListViewItem>().Select(i => (TrainerEntry)i.Tag).ToList();

        private void ToggleFreeze()
        {
            var sel = SelectedEntries();
            if (sel.Count == 0) { Dlg.Info("Trainer", "Select one or more entries."); return; }
            foreach (var e in sel) e.Frozen = !e.Frozen;
            FillTrainer();
        }

        private void WriteSelectedOnce()
        {
            if (!Ready()) return;
            var sel = SelectedEntries();
            if (sel.Count == 0) return;
            Run(null, "Writing trainer values", async () =>
            {
                await MemoryTargets.RunAsync(c => { foreach (var e in sel) c.SetMemory(e.Address, PokeValue.Encode(e.Type, e.Value)); });
                Log.Success("Wrote " + sel.Count + " trainer value(s).");
            });
        }

        private void EditEntry()
        {
            var e = SelectedEntries().FirstOrDefault();
            if (e == null) return;
            string v = Dlg.Input("Edit value", e.Description + " (" + e.Type + " at 0x" + e.Address.ToString("X8") + ")", e.Value, true);
            if (v == null) return;
            try { PokeValue.Encode(e.Type, v); }
            catch (Exception ex) { Dlg.Warn("Invalid value", ex.Message); return; }
            e.Value = v;
            FillTrainer();
        }

        private void RemoveEntries()
        {
            foreach (var e in SelectedEntries()) _entries.Remove(e);
            FillTrainer();
        }

        private async Task FreezeTick()
        {
            if (_freezing || !MemoryTargets.IsReady) return;
            var frozen = _entries.Where(e => e.Frozen).ToList();
            if (frozen.Count == 0) { _freezeTimer.Stop(); return; }
            _freezing = true;
            try
            {
                await MemoryTargets.RunAsync(c =>
                {
                    foreach (var e in frozen)
                    {
                        try { c.SetMemory(e.Address, PokeValue.Encode(e.Type, e.Value)); } catch (FormatException) { }
                    }
                });
            }
            catch (Exception ex)
            {
                _freezeTimer.Stop();
                Log.Error("Freeze stopped: " + ex.Message);
            }
            finally { _freezing = false; }
        }

        private void SaveTrainer()
        {
            if (_entries.Count == 0) { Dlg.Info("Trainer", "The trainer list is empty."); return; }
            string f = PickSave("Save trainer", "trainer.x360trainer", "X360GameHack trainer|*.x360trainer|JSON|*.json");
            if (f == null) return;
            File.WriteAllText(f, JsonConvert.SerializeObject(_entries, Formatting.Indented,
                new Newtonsoft.Json.Converters.StringEnumConverter()));
            Log.Success("Trainer saved: " + f);
        }

        private void LoadTrainer()
        {
            string f = PickFile("Load trainer", "X360GameHack trainer|*.x360trainer;*.json|All files|*.*");
            if (f == null) return;
            try
            {
                var list = JsonConvert.DeserializeObject<List<TrainerEntry>>(File.ReadAllText(f), new Newtonsoft.Json.Converters.StringEnumConverter());
                _entries.Clear();
                foreach (var e in list ?? new List<TrainerEntry>()) { e.Frozen = false; _entries.Add(e); }
                FillTrainer();
                Log.Success("Loaded " + _entries.Count + " trainer entr(ies) from " + Path.GetFileName(f));
            }
            catch (Exception ex) { Dlg.Error("Load trainer", ex.Message); }
        }

        // ------------------------------------------------------------- search

        private void Scan(bool first)
        {
            if (!Ready()) return;
            uint start, len;
            byte[] pattern;
            var t = SelectedType;
            try
            {
                start = ParseAddr(_scanStart.Text);
                len = ParseLength(_scanLen.Text);
                pattern = PokeValue.Encode(t, _scanValue.Text);
                if (t == PokeType.AsciiString || t == PokeType.UnicodeString)
                    pattern = pattern.Take(pattern.Length - (t == PokeType.AsciiString ? 1 : 2)).ToArray(); // no terminator when searching
                if (pattern.Length == 0) throw new FormatException("Enter a value to search for.");
            }
            catch (Exception ex) { Dlg.Warn("Search", ex.Message); return; }
            if (!first && _hits.Count == 0) { Dlg.Info("Next scan", "Run a first scan first."); return; }

            var cts = new CancellationTokenSource();
            var prev = _hits;
            Run(null, first ? "Scanning memory" : "Filtering results", async () =>
            {
                using (Status.Begin(first ? "Scanning 0x" + start.ToString("X8") : "Next scan", cts))
                {
                    List<uint> hits;
                    if (first)
                    {
                        hits = new List<uint>();
                        const uint chunk = 0x10000;
                        var progress = Status.NewProgress();
                        var state = new TransferProgress { Total = len, File = "memory" };
                        for (uint off = 0; off < len && hits.Count < 100000; off += chunk)
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            uint n = Math.Min(chunk + (uint)pattern.Length - 1, len - off);
                            uint a = start + off;
                            var holes = new List<KeyValuePair<uint, uint>>();   // parts of this block nothing is mapped at
                            byte[] d = await MemoryTargets.RunAsync(c => c.GetMemoryWithGaps(a, n, holes), cts.Token);
                            for (int i = 0; i + pattern.Length <= d.Length && i < chunk; i++)
                            {
                                int j = 0;
                                while (j < pattern.Length && d[i + j] == pattern[j]) j++;
                                if (j != pattern.Length) continue;
                                uint at = a + (uint)i;
                                if (holes.Count == 0 || !holes.Any(h => at < h.Key + h.Value && at + (uint)pattern.Length > h.Key)) hits.Add(at);
                            }
                            state.Done = off + n;
                            progress.Report(state);
                        }
                    }
                    else
                    {
                        // Re-read the previous hits in 64 KB blocks instead of one round trip per address.
                        var sorted = prev.OrderBy(a => a).ToList();
                        hits = await MemoryTargets.RunAsync(c =>
                        {
                            var keep = new List<uint>();
                            int idx = 0;
                            while (idx < sorted.Count && !cts.IsCancellationRequested)
                            {
                                uint blockStart = sorted[idx];
                                int j = idx;
                                while (j < sorted.Count && sorted[j] - blockStart < 0x10000) j++;
                                uint len = sorted[j - 1] - blockStart + (uint)pattern.Length;
                                var holes = new List<KeyValuePair<uint, uint>>();
                                byte[] d = c.GetMemoryWithGaps(blockStart, len, holes);
                                for (int k = idx; k < j; k++)
                                {
                                    int o = (int)(sorted[k] - blockStart), m = 0;
                                    while (m < pattern.Length && d[o + m] == pattern[m]) m++;
                                    uint at = sorted[k];
                                    if (m == pattern.Length && !holes.Any(h => at < h.Key + h.Value && at + (uint)pattern.Length > h.Key)) keep.Add(at);
                                }
                                idx = j;
                            }
                            return keep;
                        }, cts.Token);
                    }
                    _hits = hits;
                    _scanResults.BeginUpdate();
                    _scanResults.Items.Clear();
                    foreach (var h in hits.Take(1000))
                    {
                        var li = new ListViewItem("0x" + h.ToString("X8"));
                        li.SubItems.Add(_scanValue.Text);
                        _scanResults.Items.Add(li);
                    }
                    _scanResults.EndUpdate();
                    Log.Info(hits.Count + " match(es)" + (hits.Count > 1000 ? " (showing the first 1000)" : "") + ".");
                }
            });
        }

        private void LoadRegions()
        {
            if (!Ready()) return;
            Run(null, "Walking memory", async () =>
            {
                var regions = await MemoryTargets.RunAsync(c => c.WalkCommittedMemory());
                _regions.BeginUpdate();
                _regions.Items.Clear();
                foreach (var r in regions)
                {
                    var li = new ListViewItem("0x" + r.BaseAddress.ToString("X8")) { Tag = r.Size };
                    li.SubItems.Add(FileSniffer.FormatSize(r.Size));
                    li.SubItems.Add(r.Flags.ToString());
                    _regions.Items.Add(li);
                }
                _regions.EndUpdate();
                Log.Info(regions.Count + " committed region(s).");
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _liveTimer.Dispose(); _freezeTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
