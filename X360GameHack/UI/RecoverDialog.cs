using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// What was deleted on a partition and can still be got back: tick what to recover. Nothing on the drive is changed -
    /// the caller writes the chosen items to a folder on this PC.
    /// </summary>
    public sealed class RecoverDialog : DialogForm
    {
        private readonly List<FatxDeleted> _all;
        private readonly ListView _list = ListStyle.Create(("Name", 300), ("Was in", 300), ("Size", 90), ("State", 140), ("What it is", 220), ("Date", 140));
        private readonly XCheck _overwritten = new XCheck("Show overwritten", false, "Files whose space now holds something else - nothing of them can come back.");
        private readonly XCheck _lost = new XCheck("Show lost data", true, "Clusters marked used that no file owns any more - no name, read as they are.");
        private readonly XCheck _empty = new XCheck("Show empty lost data", false, "Lost data that holds only zeros where it was looked at.");
        private readonly TextBlock _count = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(20) };

        /// <summary>The ticked items (a ticked deleted folder brings what was in it).</summary>
        public List<FatxDeleted> Chosen { get; private set; }

        /// <summary>Shows the list; the ticked items, or null when closed without recovering.</summary>
        public static List<FatxDeleted> Choose(string partition, List<FatxDeleted> found)
        {
            using (var d = new RecoverDialog(partition, found))
                return d.ShowDialog(Dlg.Owner) == DialogResult.OK ? d.Chosen : null;
        }

        public RecoverDialog(string partition, List<FatxDeleted> found) : base("Recover deleted files")
        {
            _all = FatxVolume.Flatten(found).ToList();
            var named = _all.Where(x => !x.LostChain).ToList();
            var lost = _all.Where(x => x.LostChain).ToList();
            var body = new VStack { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)), Spacing = Theme.S(10) };
            body.Add(new TextBlock("Deleted on " + partition, Theme.CardTitle, Theme.Text) { Height = Theme.S(30) });
            body.Add(new TextBlock(
                named.Count + " deleted file(s) and folder(s): " + named.Count(x => x.State == RecoveryState.Recoverable) + " recoverable, " +
                named.Count(x => x.State == RecoveryState.PartlyOverwritten) + " partly overwritten, " + named.Count(x => x.State == RecoveryState.Overwritten) + " overwritten.  " +
                "Lost data: " + lost.Count + " item(s), " + FileSniffer.FormatSize(lost.Sum(x => x.Size)) + " (" + lost.Count(x => x.Type == FatxVolume.EmptyType) + " look empty).",
                Theme.Body, Theme.Text) { Flags = TextFormatFlags.WordBreak });
            body.Add(new TextBlock(
                "Nothing on the drive is changed: what you tick is written to a folder on this PC, into the folders it was in. " +
                "Recoverable means the clusters it used are free now; a deleted file is read from where it started, in one piece - one the console had written " +
                "in pieces, or whose space another file used for a while, can come back mixed. Xbox packages can be checked afterwards with Verify integrity.", Theme.Small, Theme.TextDim) { Flags = TextFormatFlags.WordBreak });
            var filters = new WrapPanel();
            filters.Add(_overwritten); filters.Add(_lost); filters.Add(_empty);
            body.Add(filters);
            _list.CheckBoxes = true;
            _list.Height = Theme.S(360);
            body.Add(_list);
            body.Add(_count);

            var buttons = new HStack { Height = Theme.S(34), Spacing = Theme.S(8) };
            var all = new XButton("Tick all recoverable", Glyph.Check, BtnKind.Secondary, (s, e) => TickRecoverable());
            var none = new XButton("Untick all", null, BtnKind.Secondary, (s, e) => { foreach (ListViewItem it in _list.Items) it.Checked = false; });
            var gap = new Panel();
            var recover = new XButton("Recover ticked...", Glyph.Download, BtnKind.Primary, (s, e) => Done());
            var close = new XButton("Close", null, BtnKind.Secondary) { DialogResult = DialogResult.Cancel };
            foreach (var b in new Control[] { all, none, gap, recover, close }) buttons.Add(b);
            buttons.FillChild = gap;
            body.Add(buttons);
            Shrinkable = new Control[] { _list };
            Body(body, Theme.S(1240));
            CancelButton = close;

            _overwritten.CheckedChanged += (s, e) => Fill();
            _lost.CheckedChanged += (s, e) => Fill();
            _empty.CheckedChanged += (s, e) => Fill();
            _list.ItemChecked += (s, e) => Count();
            Fill();
        }

        private void Fill()
        {
            var ticked = new HashSet<FatxDeleted>(_list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (FatxDeleted)i.Tag));
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var x in _all)
            {
                if (x.State == RecoveryState.Overwritten && !_overwritten.Checked) continue;
                if (x.LostChain && (!_lost.Checked || (x.Type == FatxVolume.EmptyType && !_empty.Checked))) continue;
                var it = new ListViewItem(x.Name + (x.IsDirectory ? "\\" : "")) { Tag = x, Checked = ticked.Contains(x) };
                it.SubItems.Add(x.LostChain ? "(lost data - no folder)" : "\\" + x.Path);
                it.SubItems.Add(x.IsDirectory ? x.Children.Count + " inside" : FileSniffer.FormatSize(x.Size));
                it.SubItems.Add(x.State == RecoveryState.Recoverable ? (x.LostChain ? "Lost data" : "Recoverable" + (x.Pieces > 1 ? " (" + x.Pieces + " pieces)" : ""))
                              : x.State == RecoveryState.PartlyOverwritten ? "Partly overwritten (" + x.ClustersInUse + "/" + x.Clusters + ")" : "Overwritten");
                it.SubItems.Add(x.Note.Length > 0 && x.State == RecoveryState.Overwritten ? x.Note : x.Type.Length > 0 ? x.Type : x.IsDirectory ? "folder" : "");
                it.SubItems.Add(x.Modified == DateTime.MinValue ? "" : x.Modified.ToString("yyyy-MM-dd HH:mm"));
                if (x.State == RecoveryState.Overwritten) it.ForeColor = Theme.TextDim;
                else if (x.State == RecoveryState.PartlyOverwritten) it.ForeColor = Theme.Warning;
                _list.Items.Add(it);
            }
            _list.EndUpdate();
            Count();
        }

        private void TickRecoverable()
        {
            foreach (ListViewItem it in _list.Items)
            {
                var x = (FatxDeleted)it.Tag;
                it.Checked = x.State == RecoveryState.Recoverable && x.Type != FatxVolume.EmptyType;
            }
        }

        private void Count()
        {
            var t = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (FatxDeleted)i.Tag).ToList();
            _count.Text = _list.Items.Count + " shown, " + t.Count + " ticked (" + FileSniffer.FormatSize(t.Sum(x => x.IsDirectory ? 0 : x.Size)) + ")";
        }

        private void Done()
        {
            Chosen = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (FatxDeleted)i.Tag).ToList();
            if (Chosen.Count == 0) { Dlg.Info("Recover deleted files", "Tick what to recover first (Tick all recoverable ticks everything that is fully there)."); return; }
            if (Chosen.Any(x => x.State == RecoveryState.Overwritten) &&
                !Dlg.Confirm("Recover deleted files", "Overwritten items hold nothing of the file any more and are skipped. Recover the rest?", "Recover the rest"))
                return;
            DialogResult = DialogResult.OK;
        }
    }
}
