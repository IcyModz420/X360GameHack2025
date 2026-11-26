using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// Puts a BETA label on every part of a page that has not been tested (Core\Services\TestStatus.cs lists what has).
    /// The label goes on the highest level that is untested as a whole - the side-menu entry, a tab, a card or a single
    /// button - so a card where nothing is tested shows one label instead of one per button. Untested buttons also get a
    /// tooltip line saying so, wherever their label sits.
    /// </summary>
    internal static class BetaMarks
    {
        private static readonly HashSet<Control> Tipped = new HashSet<Control>();

        /// <summary>True when nothing on the page with this side-menu name has been tested.</summary>
        public static bool PageIsBeta(string nav) => !TestStatus.IsTextOnly(nav) && !TestStatus.AnyTested(nav);

        public static void Apply(PageBase top, string nav)
        {
            if (top == null || string.IsNullOrEmpty(nav) || TestStatus.IsTextOnly(nav)) return;
            bool pageBeta = PageIsBeta(nav);

            // every tab strip on the page, including those of pages shown inside its tabs
            var tabOf = new Dictionary<Control, (SectionTabs Strip, string Text)>();
            foreach (var st in All(top).OfType<SectionTabs>())
                foreach (var (text, panel) in st.Items) tabOf[panel] = (st, text);

            // tabs, outermost first so an inner tab of an untested outer tab stays unlabelled
            var tabBeta = new Dictionary<Control, bool>();
            foreach (var panel in tabOf.Keys.OrderBy(p => Depth(p)))
            {
                bool inherited = pageBeta || Ancestors(panel).Any(a => tabBeta.TryGetValue(a, out bool b) && b);
                bool beta = !inherited && HasFeatures(panel) && !TestStatus.AnyTested(nav, TabPath(panel, tabOf));
                tabBeta[panel] = beta;
                tabOf[panel].Strip.SetBeta(panel, beta);
            }

            foreach (var c in All(top))
            {
                bool covered = pageBeta || Ancestors(c).Any(a => tabBeta.TryGetValue(a, out bool b) && b)
                               || Ancestors(c).OfType<Card>().Any(k => k.Beta);
                if (c is Card card && !string.IsNullOrEmpty(card.Title))
                {
                    card.Beta = !covered && HasFeatures(card) && !TestStatus.AnyTested(nav, TabPath(card, tabOf), card.Title);
                }
                else if (c is XButton b && !string.IsNullOrEmpty(b.Text))
                {
                    string tab = TabPath(b, tabOf), cardTitle = CardOf(b);
                    bool untested = TestStatus.Find(nav, tab, cardTitle, b.Text) == null;
                    b.Beta = untested && !covered;
                    if (untested && Tipped.Add(b))
                    {
                        string note = TestStatus.NoteFor(nav, tab, cardTitle, b.Text);
                        string beta = Theme.BetaTip + (note == null ? "" : "\nSo far: " + note);
                        string tip = Tip.Get(b);
                        Tip.Set(b, tip.Length == 0 ? beta : tip + "\n\n" + beta);
                    }
                }
                else if (!covered && (c is XCheck || c is XCombo || c is Segmented))
                {
                    MarkChoices(c, nav, TabPath(c, tabOf), CardOf(c));
                }
            }
        }

        /// <summary>Labels the single choices of a tick box, drop-down or switch that TestStatus lists as unproven.</summary>
        private static void MarkChoices(Control c, string nav, string tab, string card)
        {
            bool any = false;
            if (c is XCheck x)
            {
                x.Beta = TestStatus.ChoiceUnproven(nav, tab, card, x.Text);
                any = x.Beta;
            }
            else if (c is XCombo combo)
            {
                combo.BetaItems.Clear();
                foreach (object item in combo.Items)
                {
                    string text = combo.GetItemText(item);
                    if (TestStatus.ChoiceUnproven(nav, tab, card, text)) combo.BetaItems.Add(text);
                }
                combo.Invalidate();
                any = combo.BetaItems.Count > 0;
            }
            else if (c is Segmented seg)
            {
                for (int i = 0; i < seg.Items.Count; i++)
                {
                    bool beta = TestStatus.ChoiceUnproven(nav, tab, card, seg.Items[i]);
                    seg.SetBeta(i, beta);
                    any |= beta;
                }
            }
            if (c is XCheck)
            {
                if (any && Tipped.Add(c))
                {
                    string tip = Tip.Get(c);
                    Tip.Set(c, tip.Length == 0 ? Theme.BetaTip : tip + "\n\n" + Theme.BetaTip);
                }
                return;
            }
            // a drop-down or a switch: its tip speaks of BETA only while the choice that applies (the selected one) is
            // unproven - a proven choice shows its own tip alone; the unproven choices keep their BETA labels
            if (!any && !ChoiceTips.ContainsKey(c)) return;
            if (!ChoiceTips.ContainsKey(c))
            {
                ChoiceTips[c] = Tip.Get(c);
                if (c is XCombo cb) cb.SelectedIndexChanged += (s, e) => ShowChoiceTip(c);
                else if (c is Segmented sg) sg.SelectedIndexChanged += (s, e) => ShowChoiceTip(c);
                c.Disposed += (s, e) => ChoiceTips.Remove(c);
            }
            ShowChoiceTip(c);
        }

        /// <summary>The tip of each drop-down / switch with unproven choices, without any BETA line.</summary>
        private static readonly Dictionary<Control, string> ChoiceTips = new Dictionary<Control, string>();

        /// <summary>The selected choice of a drop-down or switch when it is unproven, else null.</summary>
        internal static string UnprovenSelection(Control c)
        {
            if (c is XCombo cb && cb.SelectedIndex >= 0)
            {
                string text = cb.GetItemText(cb.SelectedItem);
                return cb.BetaItems.Contains(text) ? text : null;
            }
            if (c is Segmented sg && sg.SelectedIndex >= 0 && sg.SelectedIndex < sg.Items.Count && sg.IsBeta(sg.SelectedIndex)) return sg.Items[sg.SelectedIndex];
            return null;
        }

        private static void ShowChoiceTip(Control c)
        {
            if (c.IsDisposed || !ChoiceTips.TryGetValue(c, out string own)) return;
            string chosen = UnprovenSelection(c);
            string beta = chosen == null ? "" : "BETA - \"" + chosen + "\" is not proven to work end to end yet. It may not work as expected: keep a backup of anything it changes.";
            Tip.Set(c, beta.Length == 0 ? own : own.Length == 0 ? beta : own + "\n\n" + beta);
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control c in root.Controls) { yield return c; foreach (var d in All(c)) yield return d; }
        }

        private static IEnumerable<Control> Ancestors(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent) yield return p;
        }

        private static int Depth(Control c) => Ancestors(c).Count();

        private static string TabPath(Control c, Dictionary<Control, (SectionTabs Strip, string Text)> tabOf)
        {
            var parts = new List<string>();
            for (var p = c; p != null; p = p.Parent) if (tabOf.TryGetValue(p, out var t)) parts.Insert(0, t.Text);
            return string.Join(" > ", parts);
        }

        private static string CardOf(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent) if (p is Card k && !string.IsNullOrEmpty(k.Title)) return k.Title;
            return "";
        }

        /// <summary>Something a user can use: a button, a box, a list, a switch - or a card that is itself a button.</summary>
        private static bool HasFeatures(Control root)
        {
            if (root is Card k && k.Interactive) return true;
            foreach (Control c in root.Controls)
            {
                if (c is ButtonBase || c is TextBoxBase || c is ListView || c is ListBox || c is ComboBox || c is TreeView ||
                    c is Segmented || c is TrackBar || c is NumericUpDown || c is Card ck && ck.Interactive) return true;
                if (HasFeatures(c)) return true;
            }
            return false;
        }
    }
}
