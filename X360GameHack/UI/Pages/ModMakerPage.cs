using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Mods;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// For mod makers: build a .x360mod from your metadata and a description of what the mod does. You can make a
    /// free open mod anyone can load, a paid mod sealed with a key your server hands out, or a server-only mod
    /// whose data never ships in the file. The program keeps no copy of any key it makes.
    /// The payload box colors what you write and checks itself as you type, in plain words with line numbers, so
    /// beginners can find mistakes. Every field of the mod is needed.
    /// </summary>
    public sealed class ModMakerPage : PageBase
    {
        public override string Title => "Mod Maker";
        public override string Subtitle => "Build mod files (free, paid or server-only) that this program can load. For developers.";
        public override string PageGlyph => Glyph.Wrench;

        private readonly XTextBox _id = new XTextBox("A short unique id, e.g. bo2-max-money", false, true);
        private readonly XTextBox _name = new XTextBox("Shown in the mod list, e.g. BO2 Max Money");
        private readonly XTextBox _game = new XTextBox("Game name, e.g. Call of Duty: Black Ops II");
        private readonly XTextBox _title = new XTextBox("Title ID (8 hex), e.g. 415608C3", false, true);
        private readonly XTextBox _requires = new XTextBox("What it needs, e.g. Latest title update");
        private readonly XTextBox _desc = new XTextBox("One line describing the mod");
        private readonly Segmented _protect = new Segmented("Open (free)", "Protected (paid)", "Server-only");
        private readonly CodeEditor _payload = new CodeEditor { Height = Theme.S(400) };
        private readonly TextBlock _status = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(40), Flags = TextFormatFlags.WordBreak };
        private readonly TextBlock _check = new TextBlock("Insert a template to start, or paste a payload. It is checked as you type.", Theme.Small, Theme.TextDim) { Height = Theme.S(80), Flags = TextFormatFlags.WordBreak };
        private readonly Timer _later = new Timer { Interval = 700 };

        public ModMakerPage()
        {
            var cols = AddColumns(2, 440);

            var meta = AddCard("The mod", "What buyers see - every field is needed. The id is how your licence server knows this mod.", Glyph.Tag, cols);
            Field(meta, "Id", _id);
            Field(meta, "Name", _name);
            Field(meta, "Game", _game);
            Field(meta, "Title ID", _title);
            Field(meta, "Needs", _requires);
            Field(meta, "Description", _desc);

            var prot = AddCard("Protection", null, Glyph.Shield, cols);
            prot.Add(_protect);
            Note(prot,
                "Open (free): the mod is readable in the file; anyone can load it, no token. Good for free or open-source mods.\n" +
                "Protected (paid): the mod is sealed. The program makes a one-time key, seals the file, and gives you a key card to put on your licence server. It keeps no copy of the key. Buyers unlock with a token tied to their machine.\n" +
                "Server-only: the strongest. No mod data ships in the file at all - your server delivers it at unlock, so there is nothing to pull out of the file even in principle.");
            Buttons(prot, Btn("Build .x360mod...", Glyph.Package, Build, BtnKind.Primary, "Checks every field and the payload, then builds the mod file."));
            prot.Add(_status);

            var pay = AddCard("What it does (payload)",
                "Start from a template: it is full of notes (the lines that start with //) explaining every part. The colors show what is what, and the check under the box says straight away if something is wrong, and on which line.", Glyph.Code);
            var tpl = new WrapPanel();
            tpl.Add(new XButton("Insert game template", Glyph.Game, BtnKind.Secondary, (s, e) => InsertTemplate(ModBuilderService.GameTemplate())));
            tpl.Add(new XButton("Insert save template", Glyph.Save, BtnKind.Secondary, (s, e) => InsertTemplate(ModBuilderService.SaveTemplate())));
            tpl.Add(new XButton("Check syntax", Glyph.Check, BtnKind.Primary, (s, e) => CheckAll()));
            pay.Add(tpl);
            pay.Add(_payload);
            pay.Add(_check);
            Note(pay,
                "How it is written: lines that start with // are notes. Text goes in \"double quotes\"; numbers are plain (100) or hex with 0x in front; " +
                "true / false / null have no quotes. Notes, entry names, text, numbers and true / false / null each have their own color; a word in red needs " +
                "\"double quotes\" around it, and a line the check found a problem on is tinted red. A comma goes between items, but not after the last item " +
                "in a { } block or [ ] list. Check syntax selects the line with the first problem; Ctrl+Z undoes a change, including inserting a template.", Theme.TextDim);

            var key = AddCard("Your signing key", "Every mod package made here is signed with your key, and X360GameHack loads no mod without it - nobody else can make mods for it.", Glyph.Shield);
            Note(key, "The key is only on this PC, locked to your Windows account. Back it up: a new PC or a reinstalled Windows needs the backup (or a new key and a program update). " +
                      "The backup is a file sealed with a password you choose - keep the file on a USB stick and the password somewhere else.");
            Buttons(key, Btn("Back up signing key...", Glyph.Save, BackUpKey, BtnKind.Primary, "Saves your signing key to a file sealed with a password (PBKDF2 + AES-256 + HMAC). Restore it with: X360GameHack.exe cli owner-key-restore <file>"));

            // check a moment after typing stops, so the result follows along without getting in the way
            _payload.TextChanged += (s, e) => { _later.Stop(); _later.Start(); };
            _later.Tick += (s, e) => { _later.Stop(); RunCheck(false); };
            Disposed += (s, e) => _later.Dispose();
        }

        /// <summary>Asks for the backup's password twice, hidden as it is typed. Null when cancelled.</summary>
        internal static string AskNewPassword()
        {
            using (var f = new DialogForm("Back up signing key"))
            {
                var body = new VStack { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)), Spacing = Theme.S(10) };
                body.Add(new TextBlock("Back up signing key", Theme.CardTitle, Theme.Text) { Height = Theme.S(30) });
                body.Add(new TextBlock("Choose a password for the backup file - at least " + OwnerKey.MinPasswordLength + " characters. Without it the backup cannot be opened, and nobody can get it back for you.",
                                       Theme.Body, Theme.TextDim) { Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix, Height = Theme.S(48) });
                var a = new XTextBox("Password") { Name = "pw1" };
                var b = new XTextBox("The same password again") { Name = "pw2" };
                a.Box.UseSystemPasswordChar = true;
                b.Box.UseSystemPasswordChar = true;
                var err = new TextBlock("", Theme.Body, Theme.Danger) { Height = Theme.S(20), Name = "error" };
                string result = null;
                var ok = new XButton("Make backup", Glyph.Save, BtnKind.Primary, (s, e) =>
                {
                    if (a.Text.Length < OwnerKey.MinPasswordLength) { err.Text = "Use at least " + OwnerKey.MinPasswordLength + " characters."; return; }
                    if (a.Text != b.Text) { err.Text = "The two passwords are not the same."; return; }
                    result = a.Text;
                    f.DialogResult = DialogResult.OK;
                });
                var cancel = new XButton("Cancel") { DialogResult = DialogResult.Cancel };
                body.Add(a); body.Add(b); body.Add(err);
                var row = new HStack { Height = Theme.S(34), RightAlign = true, Spacing = Theme.S(8) };
                row.Add(cancel); row.Add(ok);
                body.Add(row);
                f.Body(body, Theme.S(500));
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Shown += (s, e) => a.Box.Focus();
                return f.ShowDialog(Dlg.Owner) == DialogResult.OK ? result : null;
            }
        }

        private void BackUpKey()
        {
            if (!OwnerKey.Available) { Dlg.Info("Back up signing key", "This PC does not hold the signing key."); return; }
            string pw = AskNewPassword();
            if (pw == null) return;
            string path = PickSave("Where to keep the backup (a USB stick is a good place)", "x360gamehack-signing-key.x360key", "Signing-key backup (*.x360key)|*.x360key");
            if (path == null) return;
            if (System.IO.File.Exists(path)) { Dlg.Info("Back up signing key", "A file with that name exists already - a backup never replaces a file. Pick a new name."); return; }
            Run(null, "Making the backup", async () =>
            {
                try
                {
                    await Task.Run(() => OwnerKey.ExportBackup(path, pw));
                    Log.Success("Signing key backed up to " + path + " (opened again with the password to check it).");
                    Dlg.Info("Back up signing key", "Saved and checked: " + path + "\n\nKeep the file and the password apart - the file on a USB stick, the password in your head or a password manager.\n\n" +
                                                    "To put the key on a new PC or Windows account: X360GameHack.exe cli owner-key-restore <file> (it asks for the password).");
                }
                catch (Exception ex) { Log.Error("Backing up the signing key failed: " + ex.Message); Dlg.Error("Back up signing key", ex.Message); }
            });
        }

        /// <summary>Puts a template in the payload box straight away. It is one change, so Ctrl+Z puts back what was there.</summary>
        private void InsertTemplate(string t)
        {
            bool had = _payload.Text.Trim().Length > 0;
            _payload.Text = t;
            var c = RunCheck(false);
            Set((c.Ok ? "Template inserted and checked. Change the names, addresses and values - the check under the payload box follows along." : "Template inserted.") +
                (had ? " Ctrl+Z puts back what was in the box before." : ""), c.Ok);
            _payload.Box.Focus();
        }

        /// <summary>Checks the payload and shows the result under the box; with <paramref name="jump"/>, selects the line of the first problem.</summary>
        private PayloadCheck RunCheck(bool jump)
        {
            _later.Stop();
            var c = ModBuilderService.Check(_payload.Text);
            if (c.Ok)
            {
                _check.Text = c.Summary + (c.Notes.Count > 0 ? "\n" + string.Join("\n", c.Notes.Take(3).Select(n => "Note - " + n)) + (c.Notes.Count > 3 ? "\n(and " + (c.Notes.Count - 3) + " more notes)" : "") : "");
                _check.ForeColor = c.Notes.Count > 0 ? Theme.Warning : Theme.Success;
            }
            else
            {
                _check.Text = string.Join("\n", c.Problems.Take(4).Select(p => p.ToString())) + (c.Problems.Count > 4 ? "\n(and " + (c.Problems.Count - 4) + " more)" : "");
                _check.ForeColor = Theme.Danger;
            }
            _payload.Box.Mark(c.Problems.Select(p => p.Line), c.Notes.Select(n => n.Line));
            if (jump)
            {
                var first = c.Ok ? c.Notes.FirstOrDefault() : c.Problems[0];
                if (first != null && first.Line > 0) SelectLine(first.Line);
            }
            _check.Parent?.PerformLayout();
            return c;
        }

        /// <summary>Check syntax: the payload (selecting the first problem) and every field of the mod.</summary>
        private void CheckAll()
        {
            var c = RunCheck(true);
            var fields = CheckFields(c, c.Ok);
            if (fields.Count > 0)
            {
                _check.Text += "\nAlso fill in the mod's fields marked in red: " + string.Join(", ", fields.Select(f => f.Label).Distinct()) + ".";
                if (c.Ok) _check.ForeColor = Theme.Warning;
                _check.Parent?.PerformLayout();
            }
            else if (c.Ok) Set("Every field is filled in and the payload is valid - ready to build.", true);
            else Set("Every field is filled in. Fix the payload - the check under the payload box says what is wrong.", false);
        }

        /// <summary>Marks the fields that are empty or not right in red, says what they need, and returns them.</summary>
        private List<FieldProblem> CheckFields(PayloadCheck payload, bool focus)
        {
            var boxes = new Dictionary<string, XTextBox>
            {
                ["id"] = _id, ["name"] = _name, ["game"] = _game, ["titleId"] = _title, ["requires"] = _requires, ["description"] = _desc,
            };
            var problems = ModBuilderService.CheckFields(Info(), payload);
            foreach (var b in boxes.Values) b.Invalid = false;
            foreach (var p in problems) if (boxes.TryGetValue(p.Field, out var b)) b.Invalid = true;
            if (problems.Count > 0)
            {
                Set(string.Join("\n", problems.Select(p => p.ToString())), false);
                if (focus && boxes.TryGetValue(problems[0].Field, out var first)) first.Box.Focus();
            }
            return problems;
        }

        /// <summary>Selects a line of the payload (counting the lines as they are written, not as they wrap) and scrolls to it.</summary>
        private void SelectLine(int line)
        {
            var tb = _payload.Box;
            string t = tb.Text ?? "";
            int start = 0;
            for (int i = 1; i < line; i++)
            {
                int nl = t.IndexOf('\n', start);
                if (nl < 0) return;
                start = nl + 1;
            }
            int end = t.IndexOf('\n', start);
            if (end < 0) end = t.Length;
            if (end > start && t[end - 1] == '\r') end--;
            tb.Focus();
            tb.Select(start, Math.Max(0, end - start));
            tb.ScrollToCaret();
        }

        private void Set(string msg, bool ok) { _status.Text = msg; _status.ForeColor = ok ? Theme.Success : Theme.Danger; }

        private ModInfo Info() => new ModInfo
        {
            Id = _id.Text.Trim(), Name = _name.Text.Trim(), Game = _game.Text.Trim(), TitleId = _title.Text.Trim(),
            Kind = ModBuilderService.KindOf(_payload.Text) == "game" ? "game" : "save", Version = "1", Requires = _requires.Text.Trim(), Description = _desc.Text.Trim(),
        };

        private void Build()
        {
            var protection = _protect.SelectedIndex == 0 ? ModProtection.Open : _protect.SelectedIndex == 2 ? ModProtection.ServerOnly : ModProtection.Protected;
            PayloadCheck c = protection == ModProtection.ServerOnly && _payload.Text.Trim().Length == 0 ? null : RunCheck(true);
            var fields = CheckFields(c, c == null || c.Ok);
            if (fields.Count > 0 || (c != null && !c.Ok))
            {
                if (fields.Count == 0) Set("Fix the payload first - the check under the payload box says what is wrong.", false);
                var all = fields.Select(f => f.ToString()).Concat(c == null ? Enumerable.Empty<string>() : c.Problems.Take(6).Select(p => "Payload - " + p));
                Dlg.Warn(fields.Count > 0 ? "Fill in every field first" : "Fix the payload first", string.Join("\n", all.Select(p => "• " + p)));
                return;
            }
            var result = ModBuilderService.Build(Info(), _payload.Text, protection);
            if (!result.Ok) { Set(string.Join("  ", result.Problems), false); Dlg.Warn("Fix these first", string.Join("\n", result.Problems.Select(p => "• " + p))); return; }

            string outPath = PickSave("Save mod file", _id.Text.Trim() + ".x360mod", "Mod files|*.x360mod");
            if (outPath == null) return;
            try
            {
                string extra = ModBuilderService.WriteTo(result, outPath);
                if (result.KeyCard != null)
                {
                    Log.Success("Built " + Path.GetFileName(outPath) + " (protected). Key card saved next to it: " + Path.GetFileName(extra));
                    Dlg.Info("Protected mod built",
                        "Saved: " + outPath + "\n\n" +
                        "A key card was saved beside it (" + Path.GetFileName(extra) + "). Put its record on your licence server so buyers can unlock this mod. " +
                        "The .x360mod file does not contain the key, and this program kept no copy of it - the key card is the only copy, so keep it safe.");
                    Set("Protected mod built. The key card is the only copy of the key - register it on your server and keep it safe.", true);
                }
                else if (result.ServerPayload != null)
                {
                    Log.Success("Built " + Path.GetFileName(outPath) + " (server-only). The data for your server was saved beside it: " + Path.GetFileName(extra));
                    Set("Server-only mod built. Put " + Path.GetFileName(extra) + " on your licence server; the mod file itself holds no data.", true);
                }
                else
                {
                    Log.Success("Built " + Path.GetFileName(outPath) + " (" + (protection == ModProtection.Open ? "free/open" : "server-only") + ").");
                    Set("Built " + Path.GetFileName(outPath) + ". Drop it in the mods folder to try it.", true);
                }
            }
            catch (Exception ex) { Set(ex.Message, false); Dlg.Error("Build mod", ex.Message); }
        }
    }
}
