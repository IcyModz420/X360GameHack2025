using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// Xbox 360 flash (NAND) dumps, read only - every board: Xenon, Zephyr, Falcon, Opus, Jasper (16 MB small block and the
    /// big-block 256 / 512 MB), Trinity, Corona (16 MB and 4 GB) and xeBuild images. The boot chain and both patch slots
    /// (the dashboard each one boots, 17559 and every earlier one), the SMC, the keyvault (decrypted with the console's own CPU
    /// key, which stays in memory) and the flash file system. Read against J-Runner's own reader on 71 real dumps (NandTests).
    /// </summary>
    public sealed class NandPage : PageBase
    {
        public override string Title => "Flash Dumps";
        public override string Subtitle => "Look inside an Xbox 360 flash (NAND) dump: boot chain, patch slots, SMC, keyvault and the flash file system - every board, small and big block.";
        public override string PageGlyph => Glyph.Hdd;

        private NandImage _nand;
        private NandKeyVault _kv;
        /// <summary>fcrt.bin decrypted with the CPU key when it unlocked the keyvault (memory only).</summary>
        private byte[] _fcrtDec;
        /// <summary>The boot chain decrypted (with the CPU key once the keyvault is unlocked; zeros before, as J-Runner does).</summary>
        private List<NandDecryptedStage> _chain = new List<NandDecryptedStage>();
        private bool _chainHasCpuKey;
        private readonly KvGrid _dump = new KvGrid(), _smc = new KvGrid(), _smcConfig = new KvGrid(), _kvGrid = new KvGrid(), _checks = new KvGrid(), _lockdown = new KvGrid();
        private readonly ListView _boot = ListStyle.Create(("Stage", 80), ("Build", 70), ("Offset", 90), ("Size", 90), ("Flags", 60), ("What it is", 330), ("Decrypted with", 260));
        private readonly ListView _files = ListStyle.Create(("Name", 240), ("Size", 110), ("First unit", 90), ("Units", 70), ("Stored", 120));
        private readonly XTextBox _cpuKey = new XTextBox("The console's CPU key (32 hex digits)", false, true);
        private readonly TextBlock _keyNote = new TextBlock("", Theme.Small, Theme.TextDim) { Flags = TextFormatFlags.WordBreak };
        /// <summary>What the boot chain and SMC say about the image (glitch loaders, another board's CB or SMC), one line each.</summary>
        private readonly TextBlock _imageNotes = new TextBlock("", Theme.Small, Theme.Warning) { Flags = TextFormatFlags.WordBreak };
        private readonly XCombo _roots = new XCombo { Width = Theme.S(420) };
        private readonly TextBlock _empty;
        private readonly List<Control> _needsDump = new List<Control>();
        private bool _loadingRoots;

        public NandPage()
        {
            Into = AddSection("Overview", Glyph.Info);
            _empty = Note(Into, "Open or drop a flash dump (nanddump.bin, updflash.bin ... - 16 MB, 64 MB big block or 48 MB Corona 4 GB) to look inside it. Nothing in the dump is ever changed.", Theme.Warning);
            var dump = AddCard("This flash dump", null, Glyph.Hdd);
            dump.Add(_dump);
            dump.Add(_imageNotes);
            _needsDump.Add(dump);
            var boot = AddCard("Boot chain and patch slots",
                "Each stage as its own plain header gives it. CB_A and CB_B are the 2nd-stage loader (xeBuild glitch images add CB_X), CD and CE the 4th and 5th stage; " +
                "each patch slot holds a CF / CG pair that updates the kernel to the dashboard version shown.", Glyph.List);
            _boot.Height = Theme.S(250);
            _boot.MultiSelect = false;
            boot.Add(_boot);
            Note(boot, "Each stage is decrypted as J-Runner does it: CB_A and the patch slots' CF with the published 1BL key, each later stage with the key of the stage before it. " +
                       "Boards that pair the CB to the console also need the CPU key - unlock the keyvault (Keyvault tab) and those stages are decrypted again with it.", Theme.TextDim);
            boot.Add(_lockdown);
            Buttons(boot,
                Btn("Save the selected stage (as stored)...", Glyph.Save, () => SaveStage(false)),
                Btn("Save the selected stage (decrypted)...", Glyph.Save, () => SaveStage(true)));
            _needsDump.Add(boot);
            var smc = AddCard("SMC", "The system management controller's program, decrypted with its published key.", Glyph.Flash);
            smc.Add(_smc);
            Buttons(smc,
                Btn("Save SMC (decrypted)...", Glyph.Save, () => SaveBytes("smc_dec.bin", _nand?.Smc, "the decrypted SMC")),
                Btn("Save SMC (as stored)...", Glyph.Save, () => SaveBytes("smc_enc.bin", _nand?.SmcEncrypted, "the SMC as stored")));
            _needsDump.Add(smc);
            var cfg = AddCard("SMC config",
                "What the SMC reads at start-up: fan speeds, the temperatures it holds the chips at, the console's region, and this console's Family Settings reset code. Read where J-Runner reads it, its checksum checked.", Glyph.Settings);
            cfg.Add(_smcConfig);
            Buttons(cfg, Btn("Save SMC config (smc_config.bin)...", Glyph.Save, () => SaveBytes("smc_config.bin", _nand?.SmcConfig?.Area, "the SMC config")));
            Note(cfg, "Family Settings reset code: the four buttons shown are this console's own reset code. If a console's parental-controls (Family Settings) passcode is forgotten, " +
                      "enter it at the console's pass-code screen to clear the lock - it resets the console's settings but keeps games and saved data. The passcode itself is not kept in the flash; this code clears it.", Theme.TextDim);
            Note(cfg, "smc_config.bin is saved as J-Runner saves it: 64 KB with the config 0xC000 into it (512 KB and 0x60000 on big-block flash).", Theme.TextDim);
            _needsDump.Add(cfg);

            // ---------------- keyvault
            Into = AddSection("Keyvault", Glyph.Unlock);
            var key = AddCard("CPU key",
                "The keyvault is encrypted with the console's own CPU key. Type it, or keep J-Runner's cpukey.txt next to the dump - it is read from there. " +
                "The key and what it decrypts stay in memory: nothing is written anywhere unless you save it.", Glyph.Unlock);
            Field(key, "CPU key", _cpuKey, Btn("Unlock", Glyph.Unlock, () => UseKey(_cpuKey.Text, false), BtnKind.Primary));
            key.Add(_keyNote);
            _needsDump.Add(key);
            var kv = AddCard("Keyvault", "What the console's keyvault says, once the CPU key unlocks it.", Glyph.Unlock);
            kv.Add(_kvGrid);
            Buttons(kv,
                Btn("Copy DVD key", Glyph.Copy, () => { if (_kv?.DvdKey != null) { Clipboard.SetText(_kv.DvdKey); Log.Info("DVD key copied."); } }),
                Btn("Save keyvault (decrypted)...", Glyph.Save, SaveDecryptedKv),
                Btn("Save keyvault (as stored)...", Glyph.Save, () => SaveBytes("kv_enc.bin", _nand?.KeyVaultEncrypted, "the keyvault as stored (encrypted)")),
                Btn("Save fcrt.bin (decrypted)...", Glyph.Save, () =>
                {
                    if (_fcrtDec == null) { Dlg.Info("FCRT", _nand?.Fcrt == null ? "This flash has no fcrt.bin." : "Unlock the keyvault with the console's CPU key first - fcrt.bin is decrypted with the same key."); return; }
                    SaveBytes("fcrt_dec.bin", _fcrtDec, "fcrt.bin decrypted");
                }));
            _needsDump.Add(kv);
            var kvSwap = AddCard("Swap keyvault",
                "Put a different console's keyvault into a copy of this dump - for moving a keyvault to another console on RGH / JTAG. Give a keyvault saved \"as stored\" (another dump's KV_en.bin), or a flash dump to take its keyvault from. " +
                "The console the keyvault belongs to boots it with its own CPU key. The new dump is checked before it is saved; this one is never changed.", Glyph.Unlock);
            Buttons(kvSwap, Btn("Swap keyvault...", Glyph.Refresh, SwapKeyVault, BtnKind.Primary));
            Note(kvSwap, "Only for RGH / JTAG consoles. A keyvault holds a console's private keys - only use one that is yours to use, and keep the original dump as a backup. Made here, not yet tested on a console.", Theme.Warning);
            _needsDump.Add(kvSwap);

            // ---------------- flash file system
            Into = AddSection("Files", Glyph.Folder);
            var fs = AddCard("Flash file system",
                "The files the console keeps in flash: the dashboard and system programs (16 MB consoles), crl.bin, secdata.bin, extended.bin and more. " +
                "The newest root is the one in use; older roots are earlier states of the flash.", Glyph.Folder);
            Field(fs, "Root", _roots).FillChild = null;
            _roots.SelectedIndexChanged += (o, e) => { if (!_loadingRoots) UseRoot(); };
            _files.Height = Theme.S(320);
            _files.Columns[1].TextAlign = HorizontalAlignment.Right;
            _files.DoubleClick += (o, e) => OpenSelected();
            fs.Add(_files);
            Buttons(fs,
                Btn("Extract selected...", Glyph.Download, () => Extract(false)),
                Btn("Extract all...", Glyph.Download, () => Extract(true), BtnKind.Primary),
                Btn("Open in X360GameHack", Glyph.OpenFile, OpenSelected));
            _needsDump.Add(fs);

            var swap = AddCard("Swap boot animation",
                "Change the boot animation the console plays at start-up (the classic blades, the NXE one, or the current one) by writing a new bootanim.xex into a copy of this dump. " +
                "Pick a bootanim.xex, or another flash dump to take its boot animation from. The new dump is checked before it is saved; flash it like any NAND and keep this one as a backup.", Glyph.Flash);
            Buttons(swap,
                Btn("Swap boot animation...", Glyph.Refresh, SwapBootAnim, BtnKind.Primary),
                Btn("Save this dump's boot animation...", Glyph.Save, SaveBootAnim),
                Btn("Replace selected file...", Glyph.OpenFile, () => ReplaceSelectedFile()));
            Note(swap, "Only works on RGH / JTAG consoles. A custom animation only shows if it is a valid bootanim.xex and fits the space the current one uses (same size or smaller). Made here, not yet tested on a console - keep the original dump.", Theme.Warning);
            _needsDump.Add(swap);

            // ---------------- checks
            Into = AddSection("Checks", Glyph.Shield);
            var checks = AddCard("Pages, blocks and spare bytes",
                "Every page's 26-bit EDC (the NAND controller's error check) is recomputed; bad blocks come from their markers, and the block that took a bad block's place is used.", Glyph.Shield);
            checks.Add(_checks);
            Buttons(checks, Btn("Save without spare bytes...", Glyph.Save, () => SaveBytes(Path.GetFileNameWithoutExtension(_nand?.Path ?? "flash") + "_noecc.bin", _nand?.WithoutSpare(), "the dump without spare bytes")));
            _needsDump.Add(checks);

            OnWorkingFileChanged();
        }

        public override void OnWorkingFileChanged()
        {
            bool has = WorkingFile.Kind == WorkingFileKind.Nand;
            foreach (var c in _needsDump) c.Visible = has;
            _empty.Visible = !has;
            if (!has) { _nand = null; _kv = null; _fcrtDec = null; _chain = new List<NandDecryptedStage>(); _chainHasCpuKey = false; _cpuKey.Text = ""; return; }
            string path = WorkingFile.Path;
            Run(null, "Reading the flash dump", async () =>
            {
                var n = await Task.Run(() => NandImage.Open(path));
                var chain = await Task.Run(() => n.DecryptBootChain(null));
                if (path != WorkingFile.Path) return;
                _nand = n;
                _kv = null;
                _fcrtDec = null;
                _chain = chain;
                _chainHasCpuKey = false;
                ShowDump();
                // J-Runner keeps the CPU key in cpukey.txt next to the console's dumps
                string keyFile = Path.Combine(Path.GetDirectoryName(path), "cpukey.txt");
                _cpuKey.Text = "";
                if (File.Exists(keyFile))
                {
                    try
                    {
                        string k = File.ReadAllText(keyFile).Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                        if (NandImage.ParseCpuKey(k) != null) { _cpuKey.Text = k; UseKey(k, true); }
                    }
                    catch { }
                }
                if (_kv == null) ShowKv();
                Body.PerformLayout();
            });
        }

        private void ShowDump()
        {
            var n = _nand;
            _dump.Clear();
            _dump.Set("Path", n.Path);
            _dump.Set("Size", FileSniffer.FormatSize(n.FileLength) + " (" + n.FileLength.ToString("N0") + " bytes)");
            _dump.Set("Board", n.Board);
            var notes = n.ImageNotes;
            _imageNotes.Text = string.Join("\n", notes);
            _imageNotes.Visible = notes.Count > 0;
            _dump.Set("Layout", n.Kind == NandKind.BigBlock ? "Big-block flash, 128 KB blocks, spare bytes (meta type 2)" :
                                n.Kind == NandKind.SmallBlock0 ? "Small-block flash, 16 KB blocks, spare bytes (meta type 0)" :
                                n.Kind == NandKind.SmallBlock1 ? "Small-block flash, 16 KB blocks, spare bytes (meta type 1)" : "No spare bytes (Corona 4 GB eMMC, or spare bytes removed)");
            _dump.Set("Image", (n.Devkit ? "Devkit" : "Retail") + ", 1BL build " + n.Build + (n.Qfe != 0 ? "." + n.Qfe : "") + ", header flags " + n.HeaderFlags.ToString("X4"));
            _dump.Set("Copyright", n.Copyright);
            var slots = n.Bootloaders.Where(b => b.Magic == "CF" || b.Magic == "SF").Select(b => "slot " + b.Slot + ": " + b.Build).ToList();
            _dump.Set("Dashboard", slots.Count == 0 ? "no patch slot (base kernel " + (n.Stage("CE")?.Build ?? 1888) + ")" : string.Join(", ", slots));
            _dump.Set("Keyvault", "0x" + n.KvAddress.ToString("X") + ", " + n.KvLength + " bytes, version " + n.KvVersion.ToString("X4"));
            _dump.Set("SMC", "0x" + n.SmcAddress.ToString("X") + ", " + n.SmcLength + " bytes");
            _dump.Set("Patch slots", "0x" + n.PatchSlotAddress.ToString("X") + ", " + n.PatchSlotCount + " slots" + (n.PatchSlotSize != 0 ? " of 0x" + n.PatchSlotSize.ToString("X") : ""));

            ShowChain();

            _smc.Clear();
            if (n.Smc != null)
            {
                _smc.Set("Version", n.SmcVersion);
                _smc.Set("Written for", n.SmcBoard == NandBoard.Unknown ? "unknown (byte 0x100 is " + n.Smc[0x100].ToString("X2") + ")" : NandImage.BoardName(n.SmcBoard));
                _smc.Set("Patched", n.SmcPatched == true ? "yes - a glitch / JTAG image's SMC" : n.SmcPatched == false ? "no - Microsoft's SMC as it is" :
                                    "not known (J-Runner's test is for Xenon - Jasper SMCs)");
                _smc.Set("Size", n.Smc.Length + " bytes");
            }
            else _smc.Set("SMC", "not found");

            _smcConfig.Clear();
            var c = n.SmcConfig;
            if (c == null) _smcConfig.Set("SMC config", "none where J-Runner reads it (that place is blank)");
            else
            {
                var t = c.Temperatures;
                _smcConfig.Set("Checksum", c.ChecksumOk ? "right (" + c.StoredChecksum.ToString("X4") + ")" : "does not match: stored " + c.StoredChecksum.ToString("X4") + ", the bytes give " + c.ComputedChecksum.ToString("X4") + " - edited or damaged");
                _smcConfig.Set("Target temperatures", "CPU " + t[0] + " °C, GPU " + t[1] + " °C, eDRAM " + t[2] + " °C");
                _smcConfig.Set("Critical temperatures", "CPU " + t[3] + " °C, GPU " + t[4] + " °C, eDRAM " + t[5] + " °C");
                _smcConfig.Set("Fans", "CPU " + NandSmcConfig.FanText(c.CpuFan) + ", GPU " + NandSmcConfig.FanText(c.GpuFan));
                _smcConfig.Set("Region", c.Region + " - " + NandKeyVault.RegionName(c.Region));
                _smcConfig.Set("DVD region", NandSmcConfig.DvdRegionText(c.DvdRegion));
                _smcConfig.Set("MAC address", c.Mac);
                var buttons = c.ResetCodeButtons;
                _smcConfig.Set("Family Settings reset code", buttons == null ? "none (" + c.Hex(0x238, 4) + ")" : buttons + "  (\"" + c.ResetCode + "\") - enter on the console to clear a forgotten parental-controls passcode");
                _smcConfig.Set("Other values", "structure " + c.Structure.ToString("X2") + ", config " + c.Config.ToString("X2") + ", clock " + c.Clock.ToString("X2") + ", bit " + c.Bit.ToString("X2") +
                                               ", ANA " + c.Ana.ToString("X2") + " (backup " + c.AnaBackup.ToString("X2") + "), version " + c.Version.ToString("X8") + ", net " + c.Net.ToString("X8") +
                                               ", flags " + c.Flags.ToString("X8") + ", power mode " + c.PowerMode.ToString("X4") + ", power VCS " + c.PowerVcs.ToString("X4"));
                _smcConfig.Set("Sensor gains / offsets", "CPU " + c.Hex(0x18, 4) + ", GPU " + c.Hex(0x1C, 4) + ", eDRAM " + c.Hex(0x20, 4) + ", board " + c.Hex(0x24, 4));
                _smcConfig.Set("Place", "0x" + (c.Address + c.ConfigOffset).ToString("X") + " in the flash");
            }

            _checks.Clear();
            _checks.Set("Pages checked", n.HasSpare ? n.PagesChecked.ToString("N0") + " (erased pages skipped)" : "this dump has no spare bytes - nothing to check");
            _checks.Set("EDC errors", n.EccErrors.Count == 0 ? "none" : n.EccErrors.Count + " page(s): " + string.Join(", ", n.EccErrors.Take(12)) + (n.EccErrors.Count > 12 ? " ..." : ""));
            _checks.Set("Bad blocks", n.BadBlocks.Count == 0 ? "none" : string.Join(", ", n.BadBlocks.Select(b => n.Remapped.TryGetValue(b, out int r) ? b + " -> " + r : b + " (lost)")));
            if (n.ZeroBlocks.Count > 0) _checks.Set("All-zero blocks", n.ZeroBlocks.Count + " (" + string.Join(", ", n.ZeroBlocks.Take(10)) + ") - a failed read? Dump the flash again and compare.");

            _loadingRoots = true;
            _roots.Items.Clear();
            foreach (int r in n.FsRoots) _roots.Items.Add("Block " + r + ", sequence " + n.FsSequence(r) + (r == n.FsRoots[0] ? "  (the one in use)" : ""));
            if (_roots.Items.Count > 0) _roots.SelectedIndex = 0;
            _loadingRoots = false;
            ShowFiles();
        }

        /// <summary>The boot chain list (with how each stage was decrypted) and the lockdown / pairing grid.</summary>
        private void ShowChain()
        {
            var n = _nand;
            if (n == null) return;
            _boot.BeginUpdate();
            _boot.Items.Clear();
            foreach (var b in n.Bootloaders)
            {
                var li = new ListViewItem(b.Name) { Tag = b };
                li.SubItems.Add(b.Build.ToString());
                li.SubItems.Add("0x" + b.Offset.ToString("X"));
                li.SubItems.Add("0x" + b.Size.ToString("X"));
                li.SubItems.Add(b.Flags.ToString("X4"));
                li.SubItems.Add(StageNote(n, b));
                var d = _chain.FirstOrDefault(x => x.Stage == b);
                li.SubItems.Add(d == null ? "-" : d.How + (d.LooksRight == true ? " (checks out)" : d.LooksRight == false && !d.How.Contains("not look right") && !d.How.Contains("needs") ? " - does not look right" : ""));
                _boot.Items.Add(li);
            }
            _boot.EndUpdate();

            _lockdown.Clear();
            var l = n.ReadLockdown(_chain);
            if (_chain.Count == 0) { _lockdown.Set("Lockdown", "no boot chain to read"); return; }
            var cbB = _chain.FirstOrDefault(x => x.Stage.Name == "CB_B" || x.Stage.Name == "SB_B");
            bool cbBUnread = cbB != null && cbB.LooksRight != true;
            _lockdown.Set("CB", "LDV " + l.CbLdv + ", pairing data " + l.CbPairing + (l.ZeroPaired ? " (zero-paired: not tied to one console)" : "") +
                                (l.FromCbB ? " - from CB_B" : cbBUnread ? " - from CB_A; CB_B " + (_chainHasCpuKey ? "does not decrypt with this CPU key" : "needs the CPU key (Keyvault tab)") : " - from CB_A"));
            foreach (var s in l.Slots)
                _lockdown.Set("Patch slot " + s.Slot, "LDV " + s.Ldv + ", pairing data " + s.Pairing + "  (kernel " + s.Build + ")");
        }

        private static string StageNote(NandImage n, NandBootloader b)
        {
            bool loaders = n.Bootloaders.Any(x => x.Slot < 0 && x.Name.StartsWith("CB_X"));
            switch (b.Name)
            {
                case "CB_A": return "2nd-stage loader" + (loaders ? " (here a glitch image's loader)" : "");
                case "CB_B": return "2nd-stage loader, second part (decrypted with the CPU key on later boards)";
                case "CD": return "4th-stage loader";
                case "CE": return "5th stage: the base kernel " + b.Build;
                case "CF_0": case "CF_1": return "patch slot " + b.Slot + ": kernel update to " + b.Build;
                case "CG_0": case "CG_1": return "patch slot " + b.Slot + ": the update's data";
                default: return b.Name.StartsWith("CB_X") ? "extra loader stage of a glitch image" : b.Name.StartsWith("S") ? "devkit stage" : "";
            }
        }

        private void ShowFiles()
        {
            _files.BeginUpdate();
            _files.Items.Clear();
            if (_nand != null)
                foreach (var f in _nand.Files.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var li = new ListViewItem(f.Name) { Tag = f };
                    li.SubItems.Add(f.Size.ToString("N0"));
                    li.SubItems.Add(f.Start.ToString());
                    li.SubItems.Add(f.Chain.Count.ToString());
                    li.SubItems.Add(f.Contiguous ? "in one piece" : "in pieces");
                    _files.Items.Add(li);
                }
            _files.EndUpdate();
        }

        private void UseRoot()
        {
            if (_nand == null || _roots.SelectedIndex < 0 || _roots.SelectedIndex >= _nand.FsRoots.Count) return;
            _nand.UseFsRoot(_nand.FsRoots[_roots.SelectedIndex]);
            ShowFiles();
        }

        // ---------------------------------------------------------------- keyvault

        private void UseKey(string text, bool fromFile)
        {
            if (_nand == null) return;
            var key = NandImage.ParseCpuKey(text);
            if (key == null) { _keyNote.Text = "A CPU key is 32 hex digits."; _keyNote.ForeColor = Theme.Warning; return; }
            _kv = _nand.DecryptKeyVault(key);
            _fcrtDec = _kv != null && _kv.CpuKeyMatches ? NandImage.DecryptFcrt(_nand.Fcrt, key) : null;
            // the stages paired to the console (CB_B, and CD on later boards) open with the CPU key: decrypt the chain again
            if (_kv != null && _kv.CpuKeyMatches) { _chain = _nand.DecryptBootChain(key); _chainHasCpuKey = true; ShowChain(); }
            Array.Clear(key, 0, key.Length);
            if (_kv == null) { _keyNote.Text = "This dump has no keyvault."; _keyNote.ForeColor = Theme.Warning; ShowKv(); return; }
            if (!_kv.CpuKeyMatches)
            {
                _keyNote.Text = "This CPU key does not unlock this keyvault - it belongs to another console (or the keyvault is damaged).";
                _keyNote.ForeColor = Theme.Danger;
                _kv = null;
            }
            else
            {
                _keyNote.Text = "The CPU key unlocks this keyvault (its HMAC checks out)" + (fromFile ? " - read from cpukey.txt next to the dump." : ".");
                _keyNote.ForeColor = Theme.Success;
            }
            ShowKv();
        }

        private void ShowKv()
        {
            _kvGrid.Clear();
            if (_kv == null) { _kvGrid.Set("Keyvault", "locked - give the console's CPU key above"); return; }
            _kvGrid.Set("Console serial", _kv.Serial);
            _kvGrid.Set("Console ID", (_kv.ConsoleIdFriendly ?? "?") + "  (as the console shows it; in the keyvault " + _kv.ConsoleId + ")");
            _kvGrid.Set("Motherboard serial", _kv.MotherboardSerial);
            _kvGrid.Set("Region", _kv.Region + " - " + NandKeyVault.RegionName(_kv.Region));
            _kvGrid.Set("DVD key", _kv.DvdKey);
            _kvGrid.Set("DVD drive (OSIG)", _kv.Osig);
            _kvGrid.Set("Made", _kv.ManufacturingDate);
            _kvGrid.Set("Keyvault type", _kv.Type + (_kv.NeedsFcrt ? ", needs its FCRT (fcrt.bin)" : ""));
            bool hasFcrt = _nand?.Fcrt != null;
            _kvGrid.Set("fcrt.bin", !hasFcrt ? "not in the flash" : _fcrtDec != null ? "in the flash, decrypted with this CPU key (its SHA-1 checks out)" : "in the flash, but it does not decrypt with this CPU key");
        }

        private void SaveStage(bool decrypted)
        {
            var b = _boot.SelectedItems.Cast<ListViewItem>().Select(i => (NandBootloader)i.Tag).FirstOrDefault();
            if (_nand == null || b == null) { Dlg.Info("Save stage", "Select a stage in the list."); return; }
            if (!decrypted) { SaveBytes(b.Name + "_" + b.Build + ".bin", _nand.StageBytes(b), b.Name + " " + b.Build + " as stored (encrypted)"); return; }
            var d = _chain.FirstOrDefault(x => x.Stage == b);
            if (d == null) { Dlg.Info("Save stage", b.Name + " could not be decrypted (the stage it depends on is missing)."); return; }
            if (d.LooksRight == false)
            {
                Dlg.Info("Save stage", b.Name + " does not decrypt right " + (_chainHasCpuKey ? "with this CPU key." : "without the console's CPU key - unlock the keyvault on the Keyvault tab first, then save it."));
                return;
            }
            SaveBytes(b.Name + "_" + b.Build + "_dec.bin", d.Data, b.Name + " " + b.Build + " decrypted");
        }

        private void SaveDecryptedKv()
        {
            if (_kv?.Decrypted == null) { Dlg.Info("Keyvault", "Unlock the keyvault with the console's CPU key first."); return; }
            if (!Dlg.Confirm("Save the decrypted keyvault", "The decrypted keyvault holds this console's private keys and certificate. Keep the file private - anyone who has it can sign as this console.\n\nSave it?", "Save")) return;
            SaveBytes("kv_dec.bin", _kv.Decrypted, "the decrypted keyvault");
        }

        // ---------------------------------------------------------------- files

        private List<NandFile> Selected() => _files.SelectedItems.Cast<ListViewItem>().Select(i => (NandFile)i.Tag).ToList();

        private NandFile FindFile(string name) => _nand?.Files.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private void SwapBootAnim()
        {
            if (_nand == null) return;
            if (!_nand.CanReplaceFiles) { Dlg.Info("Swap boot animation", _nand.HasSpare ? "This dump has no flash file system." : "A Corona 4 GB keeps its files on the eMMC, not in a flash file system that can be edited here."); return; }
            if (FindFile("bootanim.xex") == null) { Dlg.Info("Swap boot animation", "This dump has no bootanim.xex."); return; }
            string pick = PickFile("Pick a boot animation (bootanim.xex) or a flash dump to take it from", "Boot animation or flash dump|*.xex;*.bin|All files (*.*)|*.*");
            if (pick == null) return;
            ReplaceFileFlow("bootanim.xex", pick, true);
        }

        private void SaveBootAnim()
        {
            var f = FindFile("bootanim.xex");
            if (_nand == null || f == null) { Dlg.Info("Save boot animation", "This dump has no bootanim.xex."); return; }
            var n = _nand;
            SaveBytes("bootanim.xex", n.ReadFile(f), "this dump's boot animation");
        }

        private void ReplaceSelectedFile()
        {
            var f = Selected().FirstOrDefault();
            if (_nand == null || f == null) { Dlg.Info("Replace file", "Select a file in the list first (Files tab)."); return; }
            if (!_nand.CanReplaceFiles) { Dlg.Info("Replace file", "This dump's files cannot be replaced."); return; }
            string pick = PickFile("Pick the replacement for " + f.Name);
            if (pick == null) return;
            ReplaceFileFlow(f.Name, pick, false);
        }

        /// <summary>Builds a new dump with one file replaced, reads it back to make sure it is sound, then saves it - never the open dump.</summary>
        private void ReplaceFileFlow(string name, string sourcePath, bool bootanim)
        {
            var n = _nand;
            string dest = PickSave("Save the new flash dump", Path.GetFileNameWithoutExtension(n.Path) + "-" + (bootanim ? "bootanim" : name.Replace('.', '_')) + ".bin", "Flash dump (*.bin)|*.bin|All files (*.*)|*.*");
            if (dest == null) return;
            if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(n.Path), StringComparison.OrdinalIgnoreCase)) { Dlg.Warn("Swap boot animation", "Choose a different file name - the original dump must stay as a backup."); return; }
            Run(null, "Writing the new flash dump", async () =>
            {
                string note = await Task.Run(() =>
                {
                    byte[] newData;
                    if (FileSniffer.Detect(sourcePath) == WorkingFileKind.Nand)
                    {
                        var from = NandImage.Open(sourcePath);
                        var bf = from.Files.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (bf == null) throw new InvalidDataException(Path.GetFileName(sourcePath) + " has no " + name + " to take.");
                        newData = from.ReadFile(bf);
                    }
                    else newData = File.ReadAllBytes(sourcePath);

                    var bytes = n.BuildWithReplacedFile(name, newData, out string warn);
                    File.WriteAllBytes(dest, bytes);
                    // read the written dump back and make sure it is sound before trusting it
                    var m = NandImage.Open(dest);
                    var mf = m.Files.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    bool ok = m.EccErrors.Count <= n.EccErrors.Count && mf != null && mf.Size == newData.Length && m.ReadFile(mf).SequenceEqual(newData)
                              && m.Files.Count == n.Files.Count && m.BadBlocks.SequenceEqual(n.BadBlocks)
                              && m.Bootloaders.Select(b => b.Name + b.Build).SequenceEqual(n.Bootloaders.Select(b => b.Name + b.Build))
                              && (m.KeyVaultEncrypted ?? new byte[0]).SequenceEqual(n.KeyVaultEncrypted ?? new byte[0])
                              && m.Files.Where(x => !x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).All(x => { var o = n.Files.First(y => y.Name == x.Name); return x.Size == o.Size && m.ReadFile(x).SequenceEqual(n.ReadFile(o)); });
                    if (!ok) { try { File.Delete(dest); } catch { } throw new InvalidDataException("the new dump did not read back correctly - it was deleted, nothing changed."); }
                    return (warn != null ? warn + " " : "") + name + " replaced (" + newData.Length + " bytes); the new dump read back correctly.";
                });
                Log.Success(note + " Saved " + dest + ". Flash it like any NAND on an RGH / JTAG console and keep the original as a backup (not tested on a console yet).");
            });
        }

        private void SwapKeyVault()
        {
            if (_nand == null) return;
            if (!_nand.HasSpare || _nand.KvLength == 0) { Dlg.Info("Swap keyvault", _nand.HasSpare ? "This dump has no keyvault in its flash." : "A Corona 4 GB keeps its keyvault on the eMMC, not where this can replace it."); return; }
            string pick = PickFile("Pick a keyvault (as stored, e.g. KV_en.bin) or a flash dump to take it from", "Keyvault or flash dump|*.bin|All files (*.*)|*.*");
            if (pick == null) return;
            var n = _nand;
            string dest = PickSave("Save the new flash dump", Path.GetFileNameWithoutExtension(n.Path) + "-newkv.bin", "Flash dump (*.bin)|*.bin|All files (*.*)|*.*");
            if (dest == null) return;
            if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(n.Path), StringComparison.OrdinalIgnoreCase)) { Dlg.Warn("Swap keyvault", "Choose a different file name - the original dump must stay as a backup."); return; }
            Run(null, "Writing the new flash dump", async () =>
            {
                string note = await Task.Run(() =>
                {
                    byte[] kv;
                    if (FileSniffer.Detect(pick) == WorkingFileKind.Nand)
                    {
                        var from = NandImage.Open(pick);
                        if (from.KeyVaultEncrypted == null) throw new InvalidDataException(Path.GetFileName(pick) + " has no keyvault to take.");
                        kv = from.KeyVaultEncrypted;
                    }
                    else kv = File.ReadAllBytes(pick);

                    var bytes = n.BuildWithReplacedKeyVault(kv, out string warn);
                    File.WriteAllBytes(dest, bytes);
                    var m = NandImage.Open(dest);
                    bool ok = m.EccErrors.Count <= n.EccErrors.Count && (m.KeyVaultEncrypted ?? new byte[0]).SequenceEqual(kv)
                              && m.Files.Count == n.Files.Count && m.BadBlocks.SequenceEqual(n.BadBlocks)
                              && m.Bootloaders.Select(b => b.Name + b.Build).SequenceEqual(n.Bootloaders.Select(b => b.Name + b.Build))
                              && m.Files.All(f => { var o = n.Files.First(y => y.Name == f.Name); return f.Size == o.Size && m.ReadFile(f).SequenceEqual(n.ReadFile(o)); });
                    if (!ok) { try { File.Delete(dest); } catch { } throw new InvalidDataException("the new dump did not read back correctly - it was deleted, nothing changed."); }
                    return (warn != null ? warn + " " : "") + "the keyvault was swapped (" + kv.Length + " bytes); the new dump read back correctly.";
                });
                Log.Success(note + " Saved " + dest + ". Flash it on an RGH / JTAG console with that keyvault's own CPU key, and keep the original as a backup (not tested on a console yet).");
            });
        }

        private void Extract(bool all)
        {
            if (_nand == null) return;
            var list = all ? _nand.Files.ToList() : Selected();
            if (list.Count == 0) { Dlg.Info("Extract", "Select files in the list."); return; }
            string dir = PickFolder(all ? "Extract every file of the flash file system to which folder?" : "Extract the selected files to which folder?");
            if (dir == null) return;
            var n = _nand;
            Run(null, "Extracting from the flash", async () =>
            {
                int done = await Task.Run(() =>
                {
                    int c = 0;
                    foreach (var f in list)
                    {
                        string target = PathSafety.CombineUnder(dir, PathSafety.SafeName(f.Name));
                        if (File.Exists(target)) { Log.Warn(f.Name + ": already in that folder - left as it is."); continue; }
                        File.WriteAllBytes(target, n.ReadFile(f));
                        c++;
                    }
                    return c;
                });
                Log.Success("Extracted " + done + " file(s) to " + dir);
            });
        }

        private void OpenSelected()
        {
            var f = Selected().FirstOrDefault();
            if (_nand == null || f == null) { Dlg.Info("Open", "Select a file."); return; }
            var n = _nand;
            Run(null, "Opening " + f.Name, async () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "X360GameHack", "from-flash", Path.GetFileNameWithoutExtension(n.Path));
                Directory.CreateDirectory(dir);
                string target = PathSafety.CombineUnder(dir, PathSafety.SafeName(f.Name));
                await Task.Run(() => File.WriteAllBytes(target, n.ReadFile(f)));
                Log.Info("Extracted a working copy: " + target + " (the dump is not changed).");
                WorkingFile.Set(target);
            });
        }

        private void SaveBytes(string name, byte[] data, string what)
        {
            if (data == null) { Dlg.Info("Save", "There is nothing to save - open a flash dump first."); return; }
            string f = PickSave("Save " + what, name, "Binary files|*.bin|All files|*.*");
            if (f == null) return;
            try { File.WriteAllBytes(f, data); Log.Success("Saved " + what + ": " + f); }
            catch (Exception ex) { Dlg.Warn("Save", ex.Message); }
        }
    }
}
