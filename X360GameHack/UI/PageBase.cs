using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>Status bar bridge: busy text, progress and a cancel hook.</summary>
    public static class Status
    {
        public static event Action Changed;
        private static readonly List<string> Busy = new List<string>();
        public static string Text { get; private set; } = "";
        public static int Percent { get; private set; } = -1;   // -1 = indeterminate
        public static CancellationTokenSource Cancel { get; private set; }

        public static bool IsBusy { get { lock (Busy) return Busy.Count > 0; } }
        public static string Current { get { lock (Busy) return Busy.Count > 0 ? Busy[Busy.Count - 1] : ""; } }

        public static IDisposable Begin(string text, CancellationTokenSource cancel = null)
        {
            lock (Busy) Busy.Add(text);
            Text = text;
            Percent = -1;
            if (cancel != null) Cancel = cancel;
            Changed?.Invoke();
            return new Scope(text, cancel);
        }

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static long _lastReportMs = -1000;
        private static int _lastPercent = -2;

        public static void Report(TransferProgress p)
        {
            // Transfers report every 64 KB; repaint at most ~8 times a second unless the percentage moved.
            long now = Clock.ElapsedMilliseconds;
            if (p.Percent == _lastPercent && now - _lastReportMs < 125) return;
            _lastReportMs = now;
            _lastPercent = p.Percent;
            Percent = p.Percent;
            Text = $"{p.File}  -  {Core.Formats.FileSniffer.FormatSize(p.Done)} of {Core.Formats.FileSniffer.FormatSize(p.Total)}";
            Changed?.Invoke();
        }

        private sealed class Scope : IDisposable
        {
            private readonly string _t;
            private readonly CancellationTokenSource _c;
            private bool _done;
            public Scope(string t, CancellationTokenSource c) { _t = t; _c = c; }
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                lock (Busy) Busy.Remove(_t);
                if (Cancel == _c) Cancel = null;
                Percent = -1;
                Text = Current;
                Changed?.Invoke();
            }
        }

        /// <summary>Sets the status-bar line directly (for a phase of a job), keeping the spinner going.</summary>
        public static void Say(string text, int percent = -1)
        {
            Text = text;
            Percent = percent;
            _lastPercent = -2;
            Changed?.Invoke();
        }

        /// <summary>A progress reporter that marshals to the UI thread.</summary>
        public static IProgress<TransferProgress> NewProgress() => new Progress<TransferProgress>(Report);
    }

    /// <summary>Subscriptions to app-wide events that end automatically when the owning control is disposed.</summary>
    public static class Hooks
    {
        /// <summary>Runs <paramref name="handler"/> on the UI thread whenever the XBDM connection changes.</summary>
        public static void ConsoleChanged(Control owner, Action handler)
        {
            Action h = () =>
            {
                if (owner.IsDisposed || !owner.IsHandleCreated) return;
                try { owner.BeginInvoke(handler); } catch (InvalidOperationException) { }
            };
            Core.Net.ConsoleService.StateChanged += h;
            owner.Disposed += (s, e) => Core.Net.ConsoleService.StateChanged -= h;
        }
    }

    /// <summary>Base class for every page in the shell.</summary>
    public abstract class PageBase : UserControl
    {
        public abstract string Title { get; }
        public abstract string Subtitle { get; }
        public abstract string PageGlyph { get; }

        protected readonly VStack Body;
        private readonly Panel _scroller;

        protected PageBase(bool scrolling = true)
        {
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            DoubleBuffered = true;
            var pad = new Padding(Theme.S(28), Theme.S(8), Theme.S(28), Theme.S(24));
            if (scrolling)
            {
                _scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Window };
                Body = new VStack { Padding = pad, Spacing = Theme.S(16), Location = new System.Drawing.Point(0, 0) };
                _scroller.Controls.Add(Body);
                _scroller.Resize += (s, e) => FitBody();
                _scroller.ControlAdded += (s, e) => FitBody();
                Theme.DarkScrollbars(_scroller);
                Controls.Add(_scroller);
            }
            else
            {
                Body = new VStack { Padding = pad, Spacing = Theme.S(14), Dock = DockStyle.Fill, AutoHeight = false };
                Controls.Add(Body);
            }
        }

        private void FitBody()
        {
            int w = _scroller.ClientSize.Width;
            if (w > 0 && Body.Width != w) Body.Width = w;
        }

        // ------------------------------------------------------------ sections (tabs)

        /// <summary>Tab strip shown when the page is split into sections (created by the first <see cref="AddSection"/>).</summary>
        protected SectionTabs Tabs { get; private set; }

        /// <summary>
        /// Adds a tabbed section; cards added to the returned stack only show while its tab is selected.
        /// On non-scrolling pages the selected section fills the remaining height.
        /// </summary>
        protected VStack AddSection(string title, string glyph = null)
        {
            if (Tabs == null)
            {
                Tabs = new SectionTabs();
                Body.Controls.Add(Tabs);
                Tabs.SelectedChanged += (s, e) =>
                {
                    if (_scroller != null) _scroller.AutoScrollPosition = new System.Drawing.Point(0, 0);
                    if (!Body.AutoHeight) Body.FillChild = Tabs.SelectedPanel;
                    FitHosted();
                    ActivateHosted();
                    OnSectionChanged();
                };
            }
            var section = new VStack { Spacing = Body.Spacing, Padding = new Padding(0), AutoHeight = Body.AutoHeight };
            Body.Controls.Add(section);
            Tabs.AddTab(title, glyph, section);
            if (!Body.AutoHeight && Tabs.SelectedPanel == section) Body.FillChild = section;
            return section;
        }

        protected virtual void OnSectionChanged() { }

        // ------------------------------------------------------------ pages that live inside a tab of this page

        private readonly Dictionary<Control, PageBase> _hosted = new Dictionary<Control, PageBase>();

        /// <summary>
        /// Adds a tab whose content is a whole page of its own (the GPD viewer inside Saves, for instance). The
        /// page keeps working as before - other pages still reach it through MainForm.GetPage / Navigate - it is
        /// just shown here instead of having its own entry in the sidebar. It gets the height that is left under
        /// the tabs, so its own lists fill the window instead of the outer page scrolling.
        /// </summary>
        protected VStack AddPageSection(string title, string glyph, PageBase child)
        {
            var section = AddSection(title, glyph);
            child.Dock = DockStyle.None;
            child.Visible = true;
            child.Body.Padding = new Padding(0, Theme.S(2), 0, 0);        // the host page already has the side margins
            section.Controls.Add(child);
            _hosted[section] = child;
            if (_hosted.Count == 1) Resize += (s, e) => FitHosted();
            MainForm.Instance?.Adopt(child);
            FitHosted();
            return section;
        }

        private void FitHosted()
        {
            if (_hosted.Count == 0 || Tabs == null) return;
            int available = ClientSize.Height - (Tabs.Top + Tabs.Height) - Body.Spacing - Body.Padding.Bottom;
            foreach (var child in _hosted.Values)
            {
                int h = Math.Max(Theme.S(380), available);
                if (child.Height != h) child.Height = h;
            }
        }

        /// <summary>Selects the tab that holds the page of that type. False when this page does not hold it.</summary>
        public bool ShowHosted(Type pageType)
        {
            foreach (var kv in _hosted)
                if (kv.Value.GetType() == pageType) { Tabs.Select(kv.Key); FitHosted(); kv.Value.OnActivated(); return true; }
            return false;
        }

        /// <summary>Tells the page inside the selected tab (when there is one) that it is on screen.</summary>
        internal void ActivateHosted()
        {
            if (Tabs != null && Tabs.SelectedPanel != null && _hosted.TryGetValue(Tabs.SelectedPanel, out PageBase child)) child.OnActivated();
        }

        /// <summary>True while the selected tab holds a whole page of its own.</summary>
        protected bool HostedSelected => Tabs != null && Tabs.SelectedPanel != null && _hosted.ContainsKey(Tabs.SelectedPanel);

        /// <summary>True when the page of that type is the one showing inside this page's tabs.</summary>
        public bool IsHostedShowing(Type pageType) =>
            Tabs != null && Tabs.SelectedPanel != null && _hosted.TryGetValue(Tabs.SelectedPanel, out PageBase child) && child.GetType() == pageType;

        protected void ShowSection(VStack section) => Tabs?.Select(section);

        /// <summary>Called whenever the page becomes visible.</summary>
        public virtual void OnActivated() { }

        /// <summary>Called when another page replaces this one.</summary>
        public virtual void OnDeactivated() { }

        /// <summary>Called when the shared working file changes (page may be hidden).</summary>
        public virtual void OnWorkingFileChanged() { }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Where cards and columns go when no parent is named. A page that is split into tabs sets this to the tab it
        /// is filling (the stack <see cref="AddSection"/> returned), so a long page becomes tabs without every
        /// card having to name its tab.
        /// </summary>
        protected Control Into;

        /// <summary>The place new top-level content goes: the tab being filled, or the page body.</summary>
        protected Control Home => Into ?? Body;

        protected Card AddCard(string title, string subtitle = null, string glyph = null, Control parent = null)
        {
            var c = new Card(title, subtitle, glyph);
            (parent ?? Home).Controls.Add(c);
            return c;
        }

        protected Columns AddColumns(int maxColumns = 2, int minWidth = 380, Control parent = null)
        {
            var c = new Columns { MaxColumns = maxColumns, MinColumnWidth = Theme.S(minWidth) };
            (parent ?? Home).Controls.Add(c);
            return c;
        }

        protected static WrapPanel Buttons(Control parent, params XButton[] buttons)
        {
            var w = new WrapPanel();
            foreach (var b in buttons) w.Controls.Add(b);
            parent.Controls.Add(w);
            return w;
        }

        protected static XButton Btn(string text, string glyph, Action onClick, BtnKind kind = BtnKind.Secondary, string tip = null)
        {
            var b = new XButton(text, glyph, kind);
            if (onClick != null) b.Click += (s, e) => onClick();
            if (tip != null) Tip.Set(b, tip);
            return b;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, System.Collections.Generic.List<TextBlock>> FieldLabels =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, System.Collections.Generic.List<TextBlock>>();

        /// <summary>
        /// Labelled row: [label][fill control][extra buttons...]. All labels in the same card share one width -
        /// wide enough for the longest label - so the inputs line up and nothing is cut off.
        /// </summary>
        public static HStack Field(Control parent, string label, Control fill, params Control[] extras)
        {
            var row = new HStack { Height = Theme.S(34) };
            if (label != null)
            {
                var tb = new TextBlock(label, Theme.Body, Theme.TextDim)
                {
                    Height = Theme.S(30),
                    Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
                };
                var labels = FieldLabels.GetOrCreateValue(parent);
                labels.Add(tb);
                int w = Theme.S(110);
                foreach (var l in labels) w = Math.Max(w, TextRenderer.MeasureText(l.Text, l.Font).Width + Theme.S(16));
                w = Math.Min(w, Theme.S(240));
                tb.Width = w;
                foreach (var l in labels) if (l.Width != w) { l.Width = w; l.Parent?.PerformLayout(); }
                row.Add(tb);
            }
            row.Add(fill);
            row.FillChild = fill;
            foreach (var x in extras) row.Add(x);
            parent.Controls.Add(row);
            return row;
        }

        protected static TextBlock Note(Control parent, string text, System.Drawing.Color? color = null)
        {
            var t = new TextBlock(text, Theme.Small, color ?? Theme.TextDim);
            parent.Controls.Add(t);
            return t;
        }

        /// <summary>
        /// Runs async work with the button disabled, a status-bar busy indicator and error reporting.
        /// </summary>
        protected async void Run(Control trigger, string busyText, Func<Task> work)
        {
            await RunAsync(trigger, busyText, work);
        }

        protected async Task RunAsync(Control trigger, string busyText, Func<Task> work)
        {
            if (trigger != null) trigger.Enabled = false;
            using (Status.Begin(busyText))
            {
                try { await work(); }
                catch (OperationCanceledException) { Log.Warn(busyText + " - cancelled."); }
                catch (Exception ex)
                {
                    Log.Error(busyText + " failed: " + ex.Message);
                    Dlg.Error(busyText + " failed", ex.Message);
                }
                finally
                {
                    if (trigger != null && !trigger.IsDisposed) trigger.Enabled = true;
                }
            }
        }

        /// <summary>
        /// For automated UI tests only: when set, answers the file and folder pickers instead of showing them, given
        /// the kind ("file", "files", "folder", "save") and the picker's title. Null (never set) in normal use.
        /// </summary>
        internal static Func<string, string, string> TestPicker;

        // internal so the browsers and finders (not pages themselves) use the same pickers - and the test hook above
        internal static string PickFile(string title, string filter = "All files (*.*)|*.*")
        {
            if (TestPicker != null) return TestPicker("file", title);
            using (var d = new OpenFileDialog { Title = title, Filter = filter, RestoreDirectory = true })
                return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
        }

        internal static string[] PickFiles(string title, string filter = "All files (*.*)|*.*")
        {
            if (TestPicker != null) { string a = TestPicker("files", title); return a == null ? null : a.Split('|'); }
            using (var d = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true, RestoreDirectory = true })
                return d.ShowDialog() == DialogResult.OK ? d.FileNames : null;
        }

        internal static string PickFolder(string description, string initial = null)
        {
            if (TestPicker != null) return TestPicker("folder", description);
            using (var d = new FolderBrowserDialog { Description = description, ShowNewFolderButton = true })
            {
                if (!string.IsNullOrEmpty(initial) && System.IO.Directory.Exists(initial)) d.SelectedPath = initial;
                return d.ShowDialog() == DialogResult.OK ? d.SelectedPath : null;
            }
        }

        internal static string PickSave(string title, string fileName, string filter = "All files (*.*)|*.*")
        {
            if (TestPicker != null) return TestPicker("save", title + "|" + fileName);
            using (var d = new SaveFileDialog { Title = title, FileName = fileName, Filter = filter, RestoreDirectory = true })
                return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
        }

        /// <summary>Creates a ".bak" copy once (never overwrites an existing backup).</summary>
        public static void BackupOnce(string path)
        {
            if (!AppSettings.Current.BackupBeforePatch || string.IsNullOrEmpty(path)) return;
            string bak = path + ".bak";
            try
            {
                if (!System.IO.File.Exists(bak))
                {
                    // Read with full sharing so a file held open elsewhere (e.g. the hex editor) can still be backed up.
                    using (var src = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    using (var dst = new System.IO.FileStream(bak, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
                        src.CopyTo(dst);
                    Log.Info("Backup created: " + bak);
                }
            }
            catch (Exception ex) { Log.Warn("Backup failed: " + ex.Message); }
        }
    }
}
