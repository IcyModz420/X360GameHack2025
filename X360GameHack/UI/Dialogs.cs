using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace X360GameHack.UI
{
    /// <summary>Themed replacements for MessageBox and a simple input box.</summary>
    public static class Dlg
    {
        public static IWin32Window Owner { get; set; }

        public static void Info(string title, string text) => Show(title, text, Glyph.Info, Theme.Info, false);
        public static void Warn(string title, string text) => Show(title, text, Glyph.Warning, Theme.Warning, false);
        public static void Error(string title, string text) => Show(title, text, Glyph.Error, Theme.Danger, false);
        public static bool Confirm(string title, string text, string yes = "Yes", bool danger = false)
            => Show(title, text, danger ? Glyph.Warning : Glyph.Help, danger ? Theme.Warning : Theme.Info, true, yes, danger);

        private static bool Show(string title, string text, string glyph, Color glyphColor, bool ask, string yes = "OK", bool danger = false)
        {
            using (var f = new DialogForm(title))
            {
                var body = new VStack { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)), Spacing = Theme.S(14) };
                var head = new HStack { Height = Theme.S(36), Spacing = Theme.S(10) };
                var icon = new TextBlock(glyph, Theme.IconLarge, glyphColor) { Width = Theme.S(36), Height = Theme.S(36) };
                var titleText = new TextBlock(title, Theme.CardTitle, Theme.Text) { Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis };
                head.Add(icon); head.Add(titleText); head.FillChild = titleText;
                body.Add(head);
                var msg = new TextBox
                {
                    Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"),
                    ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None,
                    BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body, TabStop = false,
                    ScrollBars = ScrollBars.None
                };
                int textH = TextRenderer.MeasureText(msg.Text, Theme.Body, new Size(Theme.S(440), 10000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + Theme.S(10);
                msg.Height = Math.Min(textH, Theme.S(420));
                if (textH > Theme.S(420)) { msg.ScrollBars = ScrollBars.Vertical; Theme.DarkScrollbars(msg); }
                body.Add(msg);
                var buttons = new HStack { Height = Theme.S(34), RightAlign = true };
                if (ask) buttons.Add(new XButton("Cancel", null, BtnKind.Secondary) { DialogResult = DialogResult.Cancel, Width = Theme.S(96) });
                var ok = new XButton(yes, null, danger ? BtnKind.Danger : BtnKind.Primary) { DialogResult = DialogResult.OK, MinimumSize = new Size(Theme.S(96), 0) };
                buttons.Add(ok);
                body.Add(buttons);
                f.Body(body, Theme.S(500));
                f.AcceptButton = ok;
                f.CancelButton = ask ? (IButtonControl)buttons.Controls[0] : ok;
                return f.ShowDialog(Owner) == DialogResult.OK;
            }
        }

        /// <summary>Lets the user pick one item; returns its index or -1 when cancelled.</summary>
        public static int Choose(string title, string prompt, System.Collections.Generic.IList<string> items, string okText = "OK")
        {
            using (var f = new DialogForm(title))
            {
                var body = new VStack { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)), Spacing = Theme.S(12) };
                body.Add(new TextBlock(title, Theme.CardTitle, Theme.Text));
                if (!string.IsNullOrEmpty(prompt)) body.Add(new TextBlock(prompt, Theme.Body, Theme.TextDim));
                var combo = new XCombo();
                foreach (var i in items) combo.Items.Add(i);
                if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                body.Add(combo);
                var buttons = new HStack { Height = Theme.S(34), RightAlign = true };
                var cancel = new XButton("Cancel") { DialogResult = DialogResult.Cancel };
                var ok = new XButton(okText, null, BtnKind.Primary) { DialogResult = DialogResult.OK };
                buttons.Add(cancel); buttons.Add(ok);
                body.Add(buttons);
                f.Body(body, Theme.S(520));
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(Owner) == DialogResult.OK ? combo.SelectedIndex : -1;
            }
        }

        /// <summary>Returns null when cancelled.</summary>
        public static string Input(string title, string prompt, string initial = "", bool mono = false)
        {
            using (var f = new DialogForm(title))
            {
                var body = new VStack { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)), Spacing = Theme.S(12) };
                body.Add(new TextBlock(title, Theme.CardTitle, Theme.Text));
                if (!string.IsNullOrEmpty(prompt)) body.Add(new TextBlock(prompt, Theme.Body, Theme.TextDim));
                var box = new XTextBox(null, false, mono) { Text = initial ?? "" };
                body.Add(box);
                var buttons = new HStack { Height = Theme.S(34), RightAlign = true };
                var cancel = new XButton("Cancel") { DialogResult = DialogResult.Cancel };
                var ok = new XButton("OK", null, BtnKind.Primary) { DialogResult = DialogResult.OK, MinimumSize = new Size(Theme.S(90), 0) };
                buttons.Add(cancel); buttons.Add(ok);
                body.Add(buttons);
                f.Body(body, Theme.S(460));
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Shown += (s, e) => { box.Box.Focus(); box.Box.SelectAll(); };
                return f.ShowDialog(Owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }

    public class DialogForm : Form
    {
        public DialogForm(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            if (MainForm.TestHidden) Opacity = 0;                 // automated UI tests: not seen (see MainForm.TestHidden)
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }

        private VStack _body;
        private int _bodyWidth;
        private readonly Dictionary<Control, int> _fullHeight = new Dictionary<Control, int>();

        /// <summary>
        /// Parts whose height may be reduced (to 60 px at the least, first one first) so the window fits on the screen -
        /// lists and text areas, never the question or the buttons. Set before <see cref="Body"/>.
        /// </summary>
        protected Control[] Shrinkable { get; set; } = new Control[0];

        /// <summary>For tests: the tallest the window's inside may be, in place of the screen's working area.</summary>
        internal static int? TestMaxClientHeight;

        public void Body(VStack body, int width)
        {
            _body = body; _bodyWidth = width;
            body.BackColor = Theme.Surface;
            body.Width = width;
            ClientSize = new Size(width, FitBody());
            body.Dock = DockStyle.Fill;
            body.AutoHeight = false;
            Controls.Add(body);
        }

        private bool _fitWhenShown;

        /// <summary>Makes the window as tall as its content again after its texts changed - never taller than the screen.</summary>
        protected void FitHeight()
        {
            if (_body == null || IsDisposed) return;
            // before the window shows, Windows reports every control in it as hidden, so nothing would be measured and the
            // window would shrink to its padding: measured once it shows instead
            if (!Visible) { _fitWhenShown = true; return; }
            int h = FitBody();
            if (h != ClientSize.Height) ClientSize = new Size(ClientSize.Width, h);
            _body.PerformLayout();
            if (!IsHandleCreated) return;
            var area = Screen.FromControl(this).WorkingArea;
            if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
        }

        private int FitBody()
        {
            int max;
            if (TestMaxClientHeight.HasValue) max = TestMaxClientHeight.Value;
            else
            {
                var area = IsHandleCreated ? Screen.FromControl(this).WorkingArea : Screen.FromPoint(Cursor.Position).WorkingArea;
                max = Math.Max(Theme.S(240), area.Height - (Height - ClientSize.Height) - Theme.S(8));
            }
            foreach (var c in Shrinkable)
            {
                if (!_fullHeight.ContainsKey(c)) _fullHeight[c] = c.Height;
                c.Height = _fullHeight[c];
            }
            // measured as if the stack sized itself, so a fill child (a scroll area) counts with its whole content
            bool auto = _body.AutoHeight;
            _body.AutoHeight = true;
            try
            {
                int h = _body.MeasureHeight(_bodyWidth);
                foreach (var c in Shrinkable)
                {
                    if (h <= max) break;
                    int take = Math.Min(h - max, c.Height - Theme.S(60));
                    if (take > 0) { c.Height -= take; h -= take; }
                }
                // still taller than the screen: the window stops at the screen and its fill child scrolls
                return Math.Min(h, max);
            }
            finally { _body.AutoHeight = auto; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_fitWhenShown) { _fitWhenShown = false; FitHeight(); }
        }
    }
}
