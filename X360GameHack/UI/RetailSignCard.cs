using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// The keyvault used to sign for stock consoles. The key lives in this process's memory only and is never copied,
    /// saved, logged or sent; only the file's location is remembered (AppSettings.KeyVaultPath) so later sessions can
    /// use it without asking.
    /// </summary>
    public static class KeyVaultSession
    {
        public static KeyVault Current { get; private set; }
        public static event Action Changed;

        /// <summary>Why the remembered keyvault could not be used ("" when it was, or when none is remembered).</summary>
        public static string AutoProblem { get; private set; } = "";

        /// <summary>File name of the remembered keyvault ("" when none).</summary>
        public static string RememberedName
        {
            get
            {
                string p = AppSettings.Current.KeyVaultPath;
                try { return string.IsNullOrEmpty(p) ? "" : Path.GetFileName(p); }
                catch (ArgumentException) { return ""; }
            }
        }

        private static bool _scanned;   // common-places scan runs at most once per session

        /// <summary>
        /// The session keyvault: the one in memory, else the remembered file, else one found in common places
        /// (next to the app, your Documents\JRunner / xeBuild folders). Loaded silently; never prompts.
        /// </summary>
        public static KeyVault Auto()
        {
            if (Current != null) return Current;
            string path = AppSettings.Current.KeyVaultPath;
            if (string.IsNullOrEmpty(path))
            {
                if (_scanned) return null;
                _scanned = true;
                var hit = FindKeyVault();
                if (hit == null) return null;
                Current = hit.Value.Kv;
                AppSettings.Current.KeyVaultPath = hit.Value.Path;
                AppSettings.Save();
                AutoProblem = "";
                Log.Success("Found your keyvault (" + Path.GetFileName(hit.Value.Path) + "): " + Current.Summary +
                            ". Rehash & resign will now sign for stock consoles too. Only the file's location is remembered - the key is never copied, saved or sent.");
                if ((Current.ConsoleType & 3) != 2)
                    Log.Warn("It is a " + Current.TypeName + " keyvault: stock retail consoles only accept a retail keyvault's signature.");
                Changed?.Invoke();
                return Current;
            }
            string problem = "";
            if (!IsLocalFile(path)) problem = "the remembered keyvault is not on a local drive";
            else if (!File.Exists(path)) problem = "the remembered keyvault (" + RememberedName + ") is missing";
            else
            {
                try
                {
                    Current = KeyVault.Load(path);
                    Log.Info("Using your keyvault: " + Current.Summary + ".");
                }
                catch (KeyVaultEncryptedException) { problem = "your keyvault is encrypted - load it again with its CPU key"; }
                catch (Exception ex) { problem = "your keyvault could not be read (" + ex.Message + ")"; }
            }
            bool changed = Current != null || problem != AutoProblem;
            AutoProblem = problem;
            if (changed) Changed?.Invoke();
            return Current;
        }

        /// <summary>Looks for a usable keyvault in common places; prefers a retail one. Null when none is found.</summary>
        private static (string Path, KeyVault Kv)? FindKeyVault()
        {
            (string Path, KeyVault Kv) best = default;
            bool found = false, bestRetail = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in CandidatePaths())
            {
                string full;
                try { full = Path.GetFullPath(candidate); } catch { continue; }
                if (!seen.Add(full)) continue;
                var kv = TryLoad(full);
                if (kv == null) continue;
                bool retail = (kv.ConsoleType & 3) == 2;
                if (!found || (retail && !bestRetail)) { best = (full, kv); found = true; bestRetail = retail; }
                if (bestRetail) break;   // a retail keyvault signs for every console - stop here
            }
            return found ? best : ((string Path, KeyVault Kv)?)null;
        }

        /// <summary>Loads a file only if it is a local, decrypted, self-proving keyvault; null (never throws) otherwise.</summary>
        private static KeyVault TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path) || !IsLocalFile(path)) return null;
                long len = new FileInfo(path).Length;
                if (len != KeyVault.FullSize && len != KeyVault.FullSize - 0x10) return null;
                return KeyVault.Load(path);   // encrypted or not a keyvault -> throws -> skipped
            }
            catch { return null; }
        }

        /// <summary>Places a keyvault commonly sits: next to the app, and the usual Xbox 360 tool folders in Documents.</summary>
        private static IEnumerable<string> CandidatePaths()
        {
            var names = new[] { "kv.bin", "keyvault.bin" };
            foreach (var n in names) yield return Path.Combine(Tools.AppDir, n);
            string docs = null;
            try { docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch { }
            if (string.IsNullOrEmpty(docs)) yield break;
            foreach (var n in names) yield return Path.Combine(docs, n);
            foreach (var sub in new[] { "JRunner", "xeBuild", "J-Runner", "Xbox 360", "Xbox360" })
                foreach (var f in BoundedFind(Path.Combine(docs, sub), names)) yield return f;
        }

        /// <summary>kv.bin / keyvault.bin under <paramref name="root"/>, a few levels deep, skipping donor folders. Bounded and swallows I/O errors.</summary>
        private static IEnumerable<string> BoundedFind(string root, string[] names)
        {
            if (!Directory.Exists(root)) yield break;
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((root, 0));
            int examined = 0;
            while (queue.Count > 0 && examined < 6000)
            {
                var (dir, depth) = queue.Dequeue();
                string[] files = null;
                try { files = Directory.GetFiles(dir); } catch { }
                if (files != null)
                    foreach (var f in files)
                    {
                        if (++examined > 6000) break;
                        if (names.Any(n => string.Equals(Path.GetFileName(f), n, StringComparison.OrdinalIgnoreCase))) yield return f;
                    }
                if (depth >= 4) continue;
                string[] dirs = null;
                try { dirs = Directory.GetDirectories(dir); } catch { }
                if (dirs != null)
                    foreach (var sd in dirs)
                        if (Path.GetFileName(sd).IndexOf("donor", StringComparison.OrdinalIgnoreCase) < 0)
                            queue.Enqueue((sd, depth + 1));
            }
        }

        /// <summary>Asks for a KV.bin (and the CPU key if it is encrypted). Returns true when a keyvault was loaded.</summary>
        public static bool LoadInteractive()
        {
            string path = PageBase.PickFile("Open a keyvault (KV.bin)", "Keyvault (*.bin)|*.bin|All files (*.*)|*.*");
            if (path == null) return false;
            KeyVault kv;
            try { kv = KeyVault.Load(path); }
            catch (KeyVaultEncryptedException)
            {
                string cpu = Dlg.Input("CPU key needed",
                    "This keyvault is encrypted (as stored in the console's flash). Enter the CPU key of the console it came from - 32 hex digits. " +
                    "It is only used to decrypt the file and is not stored.", "", true);
                if (string.IsNullOrWhiteSpace(cpu)) return false;
                try { kv = KeyVault.Load(path, cpu); }
                catch (Exception ex) { Dlg.Error("Keyvault", ex.Message); return false; }
            }
            catch (Exception ex) { Dlg.Error("Keyvault", ex.Message); return false; }
            Current = kv;
            AutoProblem = "";
            bool remember = IsLocalFile(path);
            AppSettings.Current.KeyVaultPath = remember ? Path.GetFullPath(path) : "";
            AppSettings.Save();
            Changed?.Invoke();
            Log.Success("Keyvault loaded and verified: " + kv.Summary + ". " +
                        (remember ? "Rehash & resign now signs with it automatically (only the file's location is remembered)."
                                  : "It is on a network drive, so it is used for this session only."));
            if ((kv.ConsoleType & 3) != 2)
                Log.Warn("This is a " + kv.TypeName + " keyvault: stock retail consoles only accept files signed with a retail keyvault.");
            return true;
        }

        /// <summary>Drops the key from memory when the app closes; the remembered location is kept.</summary>
        public static void Release() => Current = null;

        /// <summary>Drops the key from memory and forgets where the file is.</summary>
        public static void Forget()
        {
            bool had = Current != null || AppSettings.Current.KeyVaultPath.Length > 0;
            Current = null;
            AutoProblem = "";
            _scanned = true;   // don't silently re-detect one after the user cleared it this session
            if (AppSettings.Current.KeyVaultPath.Length > 0)
            {
                AppSettings.Current.KeyVaultPath = "";
                AppSettings.Save();
            }
            Changed?.Invoke();
            if (had) Log.Info("Keyvault forgotten. Rehash & resign signs for RGH / JTAG consoles until you add one again.");
        }

        /// <summary>Hooks <see cref="Changed"/> for as long as <paramref name="owner"/> lives.</summary>
        public static void Watch(Control owner, Action handler)
        {
            Action h = () => { if (!owner.IsDisposed) handler(); };
            Changed += h;
            owner.Disposed += (s, e) => Changed -= h;
        }

        /// <summary>Only plain local paths are opened without asking (never UNC / device paths or network drives).</summary>
        private static bool IsLocalFile(string path)
        {
            try
            {
                if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return false;
                string full = Path.GetFullPath(path);
                if (full.Length < 3 || full[1] != ':' || full[2] != '\\' || !char.IsLetter(full[0])) return false;
                return new DriveInfo(full.Substring(0, 1)).DriveType != DriveType.Network;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// One-click Rehash &amp; resign for STFS files (saves, profiles, DLC...): writes the IDs, rebuilds every hash and -
    /// when a keyvault is loaded - signs for stock consoles too. Shows which consoles will load the file.
    /// </summary>
    public sealed class RetailSignCard : Card
    {
        private readonly TextBlock _status = new TextBlock("", Theme.BodyBold, Theme.TextDim);
        private readonly TextBlock _detail = new TextBlock("", Theme.Small, Theme.TextDim);
        private readonly TextBlock _mode = new TextBlock("", Theme.Small, Theme.TextDim);
        private readonly TextBlock _kv = new TextBlock("", Theme.Small, Theme.TextDim);
        private readonly TextBlock _pending = new TextBlock("", Theme.Small, Theme.Warning) { Visible = false };
        private readonly XTextBox _profile = new XTextBox("16 hex digits - the profile that owns it", false, true);
        private readonly XTextBox _device = new XTextBox("40 hex digits (optional)", false, true);
        private readonly XTextBox _console = new XTextBox("10 hex digits (optional, informational)", false, true);
        private readonly XButton _resign, _forget, _putBack;
        private string _path;
        private string _fileProfile = "", _fileDevice = "", _fileConsole = "";     // the IDs the open file has now
        private ConSignature.Result _state;
        private bool _busy;

        /// <summary>Raised after the file is checked: true = no console will load it (hashes out of date).</summary>
        public event Action<bool> NeedsAttention;

        public RetailSignCard() : base("Rehash & resign",
            "One click fixes every hash and re-signs the file after you edit it or move it to another profile - no keyvault needed.", Glyph.Shield)
        {
            Add(_status);
            Add(_detail);
            PageBase.Field(this, "Profile ID", _profile, new XButton("From a save or profile...", Glyph.Copy, BtnKind.Secondary, (s, e) => TakeIds()));
            PageBase.Field(this, "Device ID", _device);
            PageBase.Field(this, "Console ID", _console);
            Add(_pending);
            foreach (var box in new[] { _profile, _device, _console }) box.TextChanged += (s, e) => ShowPending();

            var row = new WrapPanel();
            _resign = row.Add(new XButton("Rehash & resign", Glyph.Check, BtnKind.Primary, (s, e) => RehashAndResign()));
            _putBack = row.Add(new XButton("Put back", Glyph.Upload, BtnKind.Secondary, (s, e) => PutBack()) { Visible = false });
            var many = row.Add(new XButton("Many files...", Glyph.Library, BtnKind.Secondary, (s, e) => RehashMany()));
            Tip.Set(many, "Rehash and re-sign several saves, profiles or packages in one go. Each file keeps its own IDs; files that are already good are left alone.");
            Add(row);
            Tip.Set(_resign, "Writes the IDs, rebuilds every hash and signs - with your keyvault when one is loaded. A .bak backup is made first.");
            Add(_mode);

            var kvRow = new HStack { Height = Theme.S(34) };
            kvRow.Add(new TextBlock(Glyph.Lock, Theme.Icon, Theme.Accent) { Width = Theme.S(24), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter });
            _kv.Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            _kv.Height = Theme.S(30);
            kvRow.Add(_kv);
            kvRow.FillChild = _kv;
            kvRow.Add(new XButton("Load keyvault...", Glyph.FolderOpen, BtnKind.Secondary, (s, e) => KeyVaultSession.LoadInteractive()));
            _forget = kvRow.Add(new XButton("Forget", Glyph.Clear, BtnKind.Ghost, (s, e) => KeyVaultSession.Forget()) { Compact = true });
            Add(kvRow);
            Add(new TextBlock(
                "Stock (unmodded) consoles also check the signature. Any retail console's keyvault works: the file carries the certificate it was signed " +
                "with, so every console can check it. X360GameHack looks for one automatically (next to the app and in your Documents\\JRunner / xeBuild " +
                "folders); otherwise load it once here. It is verified and only the file's location is remembered - the key is never copied, saved or sent.",
                Theme.Small, Theme.TextDim));

            KeyVaultSession.Watch(this, ShowKeyVault);
            ShowKeyVault();
            Load(null);
        }

        private void ShowKeyVault()
        {
            var kv = KeyVaultSession.Current;
            string remembered = KeyVaultSession.RememberedName;
            _kv.ForeColor = kv != null ? Theme.Text : Theme.TextDim;
            _kv.Text = kv != null ? "Keyvault: " + kv.Summary
                     : remembered.Length > 0 ? "Keyvault: " + remembered + " (remembered)"
                     : "No keyvault - files are signed for RGH / JTAG consoles.";
            _forget.Visible = kv != null || remembered.Length > 0;
            if (_state != null) ShowState();
            ShowMode();
            PerformLayout();
        }

        /// <summary>The line under the button: which consoles the next Rehash &amp; resign signs for.</summary>
        private void ShowMode()
        {
            var kv = KeyVaultSession.Current;
            string problem = KeyVaultSession.AutoProblem, remembered = KeyVaultSession.RememberedName;
            if (_state != null && _state.State != ConSignature.State.NotPackage && !_state.IsCon)
                _mode.Text = _state.Magic + " packages are Microsoft's format: Rehash & resign fixes the hashes for RGH / JTAG consoles; no console keyvault can sign them.";
            else if (kv != null)
                _mode.Text = (kv.ConsoleType & 3) == 1
                    ? "Signs with your development kit keyvault (console " + kv.ConsoleId + "): devkits and RGH / JTAG consoles load the result."
                    : "Signs with your keyvault (console " + kv.ConsoleId + "): the result is valid on any console - stock and modded.";
            else if (remembered.Length > 0 && problem.Length == 0)
                _mode.Text = "Signs with your keyvault (" + remembered + "): the result is valid on any console - stock and modded.";
            else
                _mode.Text = "No keyvault in use" + (problem.Length > 0 ? " (" + problem + ")" : "") +
                             ": after edits or a profile change the file loads on RGH / JTAG consoles. Load a keyvault below to make it valid on any console.";
        }

        public async void Load(string path)
        {
            _path = path;
            bool canPutBack = path != null && SaveOrigins.CanPutBack(path);     // not _putBack.Visible: that reads false while this tab is hidden
            _putBack.Visible = canPutBack;
            if (canPutBack) _putBack.Text = "Put back on " + SaveOrigins.Describe(path);
            if (path == null || !File.Exists(path))
            {
                _state = null;
                _fileProfile = _fileDevice = _fileConsole = "";
                SetStatus("", "Open a save or STFS package to check it.", Theme.TextDim);
                _detail.Text = "";
                ShowPending();
                _resign.Enabled = false;
                ShowMode();
                return;
            }
            KeyVaultSession.Auto();
            try
            {
                var (state, pkg) = await Task.Run(() => (ConSignature.Inspect(path), StfsPackage.Read(path)));
                if (path != _path) return;
                _state = state;
                _fileProfile = pkg.ProfileId ?? ""; _fileDevice = pkg.DeviceId ?? ""; _fileConsole = pkg.ConsoleId ?? "";
                _profile.Text = pkg.ProfileId;
                _device.Text = pkg.DeviceId;
                _console.Text = pkg.ConsoleId;
                ShowState();
                ShowPending();
                _resign.Enabled = state.State != ConSignature.State.NotPackage;
                NeedsAttention?.Invoke(state.State != ConSignature.State.NotPackage && (!state.Loads || !state.HashesOk));
            }
            catch (Exception ex)
            {
                if (path != _path) return;
                _state = null;
                SetStatus("✖", "Could not check the file: " + ex.Message, Theme.Danger);
                _resign.Enabled = true;
            }
            ShowMode();
        }

        private void ShowState()
        {
            var s = _state;
            if (s.State == ConSignature.State.NotPackage || !s.Loads) SetStatus("✖", s.Describe(), Theme.Danger);
            else if (!s.HashesOk) SetStatus("⚠", s.Describe(), Theme.Warning);
            else if (s.State == ConSignature.State.Valid && !s.IsDevkitSigned) SetStatus("✔", s.Describe(), Theme.Success);
            else if (s.State == ConSignature.State.Microsoft) SetStatus("ⓘ", s.Describe(), Theme.Info);
            else SetStatus("✔", WithKeyVault(s), Theme.Info);
            _detail.Text = s.IsCon && s.ConsoleId.Length > 0
                ? "Certificate: console " + s.ConsoleId + (s.PartNumber.Length > 0 ? " (" + s.PartNumber + ")" : "") + "."
                : "";
        }

        /// <summary>
        /// What a CON file that only lacks a stock console's signature needs, by the keyvault in use: with a retail one,
        /// one press of Rehash &amp; resign makes it valid on any console.
        /// </summary>
        private static string WithKeyVault(ConSignature.Result s)
        {
            if (!s.IsCon) return s.Describe();
            var kv = KeyVaultSession.Current;
            bool retail = kv != null && (kv.ConsoleType & 3) == 2;
            if (s.State == ConSignature.State.BadSignature)
                return kv == null ? "Valid on RGH / JTAG consoles. Load a keyvault below and press Rehash & resign to make it valid on any console."
                     : retail ? "Valid on RGH / JTAG consoles now. Press Rehash & resign - your keyvault makes it valid on any console."
                     : "Valid on RGH / JTAG consoles. Press Rehash & resign to sign it with your development kit keyvault (devkits load it too).";
            if (s.State == ConSignature.State.Valid && s.IsDevkitSigned && retail)
                return s.Describe() + " Press Rehash & resign - your keyvault makes it valid on any console.";
            return s.Describe();
        }

        private static string HexOnly(string text) => new string((text ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();

        /// <summary>Under the IDs: says so when they are not the ones the file has - Rehash &amp; resign writes them.</summary>
        private void ShowPending()
        {
            var changed = new List<string>();
            if (_state != null && _state.State != ConSignature.State.NotPackage)
            {
                if (HexOnly(_profile.Text).Length > 0 && HexOnly(_profile.Text) != HexOnly(_fileProfile)) changed.Add("profile ID");
                if (HexOnly(_device.Text).Length > 0 && HexOnly(_device.Text) != HexOnly(_fileDevice)) changed.Add("device ID");
                if (HexOnly(_console.Text).Length > 0 && HexOnly(_console.Text) != HexOnly(_fileConsole)) changed.Add("console ID");
            }
            string text = changed.Count == 0 ? "" : "New " + string.Join(", ", changed) + " - press Rehash & resign to write " + (changed.Count == 1 ? "it" : "them") + " into the file.";
            if (_pending.Text == text && _pending.Visible == (text.Length > 0)) return;
            _pending.Text = text;
            _pending.Visible = text.Length > 0;
            PerformLayout();
        }

        /// <summary>Fills in the IDs to write (the ones given; an empty one keeps what is there). Nothing is written until Rehash &amp; resign.</summary>
        public void SetIds(string profileId, string deviceId, string consoleId)
        {
            if (!string.IsNullOrEmpty(profileId)) _profile.Text = profileId;
            if (!string.IsNullOrEmpty(deviceId)) _device.Text = deviceId;
            if (!string.IsNullOrEmpty(consoleId)) _console.Text = consoleId;
            ShowPending();
        }

        /// <summary>The IDs in the boxes now (as typed).</summary>
        public (string ProfileId, string DeviceId, string ConsoleId) Ids => (_profile.Text, _device.Text, _console.Text);

        /// <summary>True while a file is open that Rehash &amp; resign can write.</summary>
        public bool HasFile => _path != null && _state != null && _state.State != ConSignature.State.NotPackage;

        private void SetStatus(string icon, string text, System.Drawing.Color color)
        {
            _status.ForeColor = color;
            _status.Text = icon.Length > 0 ? icon + "  " + text : text;
        }

        private static byte[] ReadHex(XTextBox box, int bytes, string what)
        {
            string hex = new string(box.Text.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
            if (hex.Length == 0) return null;
            if (hex.Length != bytes * 2 || !hex.All(Uri.IsHexDigit))
                throw new FormatException("The " + what + " must be exactly " + bytes * 2 + " hex digits (or empty to keep it).");
            return BE.ParseHexBytes(hex);
        }

        private void TakeIds()
        {
            string f = PageBase.PickFile("Pick a save or profile that belongs to the target profile");
            if (f == null) return;
            try
            {
                var (profile, device, note) = ConSignature.ReadIds(f);
                if (profile == null) { Dlg.Warn("Profile ID", "That file has no profile ID (it is not tied to a profile)."); return; }
                _profile.Text = BitConverter.ToString(profile).Replace("-", "");
                if (device != null) _device.Text = BitConverter.ToString(device).Replace("-", "");
                Log.Info("Took the profile ID" + (device != null ? " and device ID" : "") + " from the " + note + " " + Path.GetFileName(f) + ". Click Rehash & resign to apply.");
            }
            catch (Exception ex) { Dlg.Error("Profile ID", ex.Message); }
        }

        /// <summary>The one-click action: never asks for a keyvault, and does nothing when the file is already up to date.</summary>
        public async void RehashAndResign() => await RehashAndResignAsync();

        /// <summary>The same action, awaitable - for pages that run it as the last step of an edit.</summary>
        public async Task RehashAndResignAsync()
        {
            if (_busy) return;
            if (_path == null || !File.Exists(_path)) { Dlg.Info("Rehash & resign", "Open a save or STFS package first."); return; }
            var ids = new ConSignature.Ids();
            try { ids.ProfileId = ReadHex(_profile, ConSignature.ProfileIdLength, "profile ID"); }
            catch (FormatException ex) { Dlg.Warn("Check the profile ID", ex.Message); return; }
            try
            {
                ids.DeviceId = ReadHex(_device, ConSignature.DeviceIdLength, "device ID");
                ids.ConsoleId = ReadHex(_console, ConSignature.ConsoleIdLength, "console ID");
            }
            catch (FormatException ex) { Dlg.Warn("Check the IDs", ex.Message); return; }
            var kv = KeyVaultSession.Auto();
            if (_state != null && _state.State != ConSignature.State.NotPackage && !_state.IsCon) kv = null;   // LIVE / PIRS: rehash only
            string path = _path, name = Path.GetFileName(path);
            var op = WorkingFile.BeginOperation("Rehash & resign");
            if (op == null) return;
            _busy = true;
            _resign.Enabled = false;
            using (op)
            using (Status.Begin("Rehash & resign " + name))
            {
                try
                {
                    if (await Task.Run(() => ConSignature.IsUpToDate(path, ids, kv)))
                    {
                        // Nothing to rewrite. Without a keyvault that can also mean "hashes fine, signature not renewable".
                        var now = await Task.Run(() => ConSignature.Check(path));
                        if (now.IsCon && now.State != ConSignature.State.Valid)
                            Log.Info(name + ": every hash is up to date. Its console signature is not valid and no keyvault is in use, so it loads on RGH / JTAG consoles only.");
                        else
                            Log.Success(name + " is already rehashed and signed - nothing to change.");
                    }
                    else
                    {
                        PageBase.BackupOnce(path);
                        var r = await Task.Run(() => ConSignature.Apply(path, ids, kv));
                        Log.Success("Rehashed and resigned " + name + (kv != null ? " with your keyvault (console " + kv.ConsoleId + ")" : "") +
                                    " - every hash verified. " + r.Describe());
                        WorkingFile.Refresh();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Rehash & resign failed: " + ex.Message);
                    Dlg.Error("Rehash & resign failed", ex.Message + "\n\nThe file was not changed.");
                }
                finally
                {
                    _busy = false;
                    if (!_resign.IsDisposed) _resign.Enabled = true;
                }
            }
            Load(path);
        }

        /// <summary>Rehash &amp; resign for a whole selection of files (each keeps its own IDs).</summary>
        private async void RehashMany()
        {
            if (_busy) return;
            string[] files = PageBase.PickFiles("Choose the saves, profiles or packages to rehash and re-sign");
            if (files == null) return;
            if (files.Length == 0) return;
            var kv = KeyVaultSession.Auto();
            _busy = true;
            using (Status.Begin("Rehash & resign " + files.Length + " files"))
            {
                try
                {
                    var results = await Task.Run(() => ConSignature.ApplyMany(files, kv, PageBase.BackupOnce, (path, outcome, detail) =>
                    {
                        string name = Path.GetFileName(path);
                        switch (outcome)
                        {
                            case ConSignature.BatchOutcome.Resigned: Log.Success("Rehashed and resigned " + name + ". " + detail); break;
                            case ConSignature.BatchOutcome.AlreadyGood: Log.Info(name + " is already rehashed and signed - left as it is."); break;
                            case ConSignature.BatchOutcome.NotPackage: Log.Warn(name + " is not an Xbox 360 package - skipped."); break;
                            default: Log.Error(name + " could not be rehashed: " + detail + " (it was not changed)"); break;
                        }
                    }));
                    int done = results.Values.Count(o => o == ConSignature.BatchOutcome.Resigned), good = results.Values.Count(o => o == ConSignature.BatchOutcome.AlreadyGood),
                        failed = results.Values.Count(o => o == ConSignature.BatchOutcome.Failed), skipped = results.Values.Count(o => o == ConSignature.BatchOutcome.NotPackage);
                    Log.Info("Rehash & resign of " + results.Count + " files: " + done + " rewritten" + (kv != null ? " and signed with your keyvault" : "") + ", " + good + " already good" +
                             (skipped > 0 ? ", " + skipped + " not packages" : "") + (failed > 0 ? ", " + failed + " FAILED (see above)" : "") + ".");
                    if (_path != null && results.ContainsKey(_path)) WorkingFile.Refresh();
                }
                catch (Exception ex) { Log.Error("Rehash & resign failed: " + ex.Message); }
                finally { _busy = false; }
            }
            if (_path != null) Load(_path);
        }

        private async void PutBack()
        {
            if (_path == null) return;
            if (_state != null && _state.State != ConSignature.State.NotPackage && !_state.HashesOk &&
                !Dlg.Confirm("Rehash & resign first", _state.Describe() + "\n\nPut it back anyway?", "Put back", true))
                return;
            // hashes fine but the signature is not this console's: fine on a modified console, "corrupted" on a stock one
            if (_state != null && _state.IsCon && _state.HashesOk && _state.State != ConSignature.State.Valid &&
                !Dlg.Confirm("Not signed for a stock console", "This save has been changed and not re-signed. A modified console (RGH / JTAG) loads it as it is. A stock console will call it corrupted - press Rehash & resign first for that.\n\nPut it back as it is?", "Put back"))
                return;
            await SaveOrigins.PutBackAsync(_path);
        }
    }
}
