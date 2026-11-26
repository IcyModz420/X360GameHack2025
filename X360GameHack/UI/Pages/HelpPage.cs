using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class HelpPage : PageBase
    {
        public override string Title => "Q & A";
        public override string Subtitle => "Built-in answers about patching, saves, consoles and this app. Works offline.";
        public override string PageGlyph => Glyph.Help;

        private readonly XTextBox _search = new XTextBox("Search questions and answers...");
        private readonly VStack _list = new VStack { Spacing = Theme.S(10), Padding = new Padding(0) };
        private readonly TextBlock _count = new TextBlock("", Theme.Small, Theme.TextFaint) { Width = Theme.S(150), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Right };
        private readonly WrapPanel _chips = new WrapPanel { HGap = Theme.S(6), VGap = Theme.S(6) };
        private readonly List<QaItem> _items = new List<QaItem>();
        private string _category;

        public HelpPage()
        {
            var top = AddCard(null);
            top.Padding = new Padding(Theme.S(14));
            var row = new HStack { Height = Theme.S(34) };
            row.Add(new TextBlock(Glyph.Search, Theme.Icon, Theme.TextDim) { Width = Theme.S(26), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter });
            row.Add(_search);
            row.FillChild = _search;
            row.Add(_count);
            row.Add(new XButton("Expand all", Glyph.ChevronDown, BtnKind.Ghost, (s, e) => SetAll(true)) { Compact = true });
            row.Add(new XButton("Collapse all", Glyph.ChevronRight, BtnKind.Ghost, (s, e) => SetAll(false)) { Compact = true });
            top.Add(row);
            top.Add(_chips);

            AddChip("All", null);
            foreach (var c in KnowledgeBase.Entries.Select(e => e.Category).Distinct()) AddChip(c, c);

            _search.TextChanged += (s, e) => Fill();
            Body.Add(_list);
            Fill();
        }

        private void AddChip(string text, string category)
        {
            var b = new XButton(text, null, category == _category ? BtnKind.Primary : BtnKind.Secondary) { Height = Theme.S(28), Compact = true, Tag = category };
            b.Click += (s, e) =>
            {
                _category = category;
                foreach (XButton x in _chips.Controls) x.Kind = (string)x.Tag == _category ? BtnKind.Primary : BtnKind.Secondary;
                Fill();
            };
            _chips.Add(b);
        }

        private void SetAll(bool open)
        {
            foreach (var i in _items) i.Open = open;
            _list.PerformLayout();
        }

        private void Fill()
        {
            string q = _search.Text.Trim();
            _list.SuspendLayout();
            foreach (Control c in _list.Controls.Cast<Control>().ToList()) { _list.Controls.Remove(c); c.Dispose(); }
            _items.Clear();
            var entries = KnowledgeBase.Entries.Where(e => (_category == null || e.Category == _category) && (q.Length == 0 ||
                e.Question.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Answer.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Category.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            foreach (var group in entries.GroupBy(e => e.Category))
            {
                _list.Add(new TextBlock(group.Key.ToUpperInvariant(), Theme.SmallBold, Theme.TextFaint) { Padding = new Padding(Theme.S(4), Theme.S(8), 0, 0) });
                foreach (var e in group) _items.Add(_list.Add(new QaItem(e, q.Length > 0)));
            }
            if (entries.Count == 0)
                _list.Add(new TextBlock("Nothing matches \"" + q + "\". Try fewer or different words.", Theme.Body, Theme.TextDim));
            _count.Text = entries.Count + " of " + KnowledgeBase.Entries.Count + " answers";
            _list.ResumeLayout();
            _list.PerformLayout();
        }

        /// <summary>Expandable question card.</summary>
        private sealed class QaItem : Card
        {
            private readonly TextBlock _body;
            private bool _open;

            public QaItem(QaEntry e, bool open) : base(e.Question, null, Glyph.ChevronRight)
            {
                Cursor = Cursors.Hand;
                Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), Theme.S(12));
                _body = new TextBlock(e.Answer, Theme.Body, Theme.Text) { Visible = false, Cursor = Cursors.IBeam };
                Add(_body);
                Click += (s, a) => Open = !Open;
                Open = open;
            }

            public bool Open
            {
                get => _open;
                set
                {
                    if (_open == value) return;
                    _open = value;
                    _body.Visible = _open;
                    PerformLayout();
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (_open)
                {
                    // overdraw the chevron to point down when expanded
                    using (var b = new System.Drawing.SolidBrush(BackColor))
                        e.Graphics.FillRectangle(b, Padding.Left, Padding.Top, Theme.S(22), Theme.S(24));
                    TextRenderer.DrawText(e.Graphics, Glyph.ChevronDown, Theme.Icon,
                        new System.Drawing.Rectangle(Padding.Left, Padding.Top, Theme.S(22), Theme.S(24)), Theme.Accent,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
    }
}
