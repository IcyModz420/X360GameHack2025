using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// Asked before a game starts in Xenia (every start goes through XeniaHost.Start). Without a signed-in profile many
    /// games stop at their title screen: Start seems to do nothing, because the game waits for a player and a storage
    /// device - seen with Crackdown 2 on 2026-11-18, Xenia's log said "Found 0 Profiles". When Xenia already signs in to a
    /// profile it has, nothing is asked (one click). Otherwise: pick one of Xenia's profiles, or make a new one exactly the
    /// way Xenia's own Create Profile does, or start without.
    /// </summary>
    public static class XeniaSignIn
    {
        /// <summary>True: start the game. False: the user cancelled, or signing in failed.</summary>
        public static bool Ask(string xenia)
        {
            List<XeniaContent.XeniaProfile> profiles;
            try { profiles = XeniaContent.Profiles(xenia); }
            catch (Exception ex) { Log.Warn("Could not read Xenia's profiles: " + ex.Message); return true; }
            string current = XeniaContent.SignedIn(xenia);
            if (current.Length > 0 && profiles.Any(p => p.Id == current)) return true;       // signed in already

            string pick = null, newTag = null;
            DialogResult result;
            using (var f = new DialogForm("Sign in to Xenia"))
            {
                var body = new VStack { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)), Spacing = Theme.S(10) };
                body.Add(new TextBlock("Sign in to Xenia", Theme.CardTitle, Theme.Text) { Height = Theme.S(30) });
                body.Add(Para("Pick the profile the game plays as. Without one, many games stop at their title screen - pressing Start seems to do nothing.", Theme.Text));

                XButton signIn = null;
                if (profiles.Count > 0)
                {
                    var combo = new XCombo { Name = "profiles" };
                    foreach (var p in profiles) combo.Items.Add(p.Label);
                    combo.SelectedIndex = 0;
                    signIn = new XButton("Sign in and start", Glyph.Play, BtnKind.Primary, (s, e) =>
                    {
                        if (combo.SelectedIndex < 0) return;
                        pick = profiles[combo.SelectedIndex].Id;
                        f.DialogResult = DialogResult.OK;
                    });
                    var row = new HStack { Height = Theme.S(34), Spacing = Theme.S(8) };
                    row.Add(combo); row.Add(signIn); row.FillChild = combo;
                    body.Add(row);
                }

                body.Add(new TextBlock(profiles.Count > 0 ? "Or make a new profile:" : "Make a profile:", Theme.Body, Theme.TextDim) { Height = Theme.S(22) });
                var tag = new XTextBox("Gamertag") { Name = "gamertag" };
                var error = new TextBlock("", Theme.Body, Theme.Danger) { Height = Theme.S(20), Name = "error" };
                var create = new XButton("Create and start", Glyph.Add, profiles.Count > 0 ? BtnKind.Secondary : BtnKind.Primary, (s, e) =>
                {
                    string t = (tag.Text ?? "").Trim();
                    string problem = XeniaContent.GamertagProblem(t);
                    if (problem != null) { error.Text = problem; return; }
                    newTag = t;
                    f.DialogResult = DialogResult.OK;
                });
                var row2 = new HStack { Height = Theme.S(34), Spacing = Theme.S(8) };
                row2.Add(tag); row2.Add(create); row2.FillChild = tag;
                body.Add(row2);
                body.Add(error);
                body.Add(Para("Your console's profiles: Neighborhood > Profiles > Copy to Xenia - then they are listed here.", Theme.TextDim));

                var buttons = new HStack { Height = Theme.S(34), RightAlign = true, Spacing = Theme.S(8) };
                var without = new XButton("Start without signing in", null, BtnKind.Ghost) { DialogResult = DialogResult.Ignore };
                var cancel = new XButton("Cancel") { DialogResult = DialogResult.Cancel };
                buttons.Add(without); buttons.Add(cancel);
                body.Add(buttons);

                f.Body(body, Theme.S(560));
                f.CancelButton = cancel;
                f.AcceptButton = (IButtonControl)signIn ?? create;
                f.Shown += (s, e) => { if (profiles.Count == 0) tag.Box.Focus(); };
                tag.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; create.PerformClick(); } };
                result = f.ShowDialog(Dlg.Owner);
            }

            if (result == DialogResult.Ignore) { Log.Info("Xenia starts without a signed-in profile."); return true; }
            if (result != DialogResult.OK || (pick == null && newTag == null)) return false;
            try
            {
                if (newTag != null)
                {
                    pick = XeniaContent.CreateProfile(xenia, newTag);
                    Log.Success("Made the profile " + newTag + " (" + pick + ") in Xenia, the way Xenia's own Create Profile makes it.");
                }
                XeniaContent.SignIn(xenia, pick);
                var chosen = XeniaContent.Profiles(xenia).FirstOrDefault(p => p.Id == pick);
                Log.Success("Xenia signs in to " + (chosen != null ? chosen.Label : pick) + " when the game starts.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Signing in to Xenia failed: " + ex.Message);
                Dlg.Error("Sign in to Xenia", ex.Message);
                return false;
            }
        }

        /// <summary>A wrapped paragraph (a read-only text box measured to its text).</summary>
        private static TextBox Para(string text, Color color)
        {
            var box = new TextBox
            {
                Text = text, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface, ForeColor = color, Font = Theme.Body, TabStop = false, ScrollBars = ScrollBars.None
            };
            box.Height = TextRenderer.MeasureText(text, Theme.Body, new Size(Theme.S(516), 10000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + Theme.S(6);
            return box;
        }
    }
}
