using System;
using System.IO;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>The file currently being worked on (shared by every tool page).</summary>
    public static class WorkingFile
    {
        public static string Path { get; private set; }
        public static WorkingFileKind Kind { get; private set; } = WorkingFileKind.None;
        public static event Action Changed;

        public static bool Has(WorkingFileKind kind) => Kind == kind && File.Exists(Path ?? "");

        public static string Name => string.IsNullOrEmpty(Path) ? "" : System.IO.Path.GetFileName(Path);

        public static string SizeText
        {
            get
            {
                try { return File.Exists(Path) ? FileSniffer.FormatSize(new FileInfo(Path).Length) : ""; }
                catch { return ""; }
            }
        }

        public static void Set(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (Directory.Exists(path))
            {
                Log.Warn("That is a folder. Use the Bulk XISO tool, Console Setup or the FTP / Neighborhood pages for folders.");
                return;
            }
            string err = PathGuard.CheckFile(path);
            if (err != null) { Log.Error(err); Dlg.Error("Cannot open file", err); return; }
            Path = System.IO.Path.GetFullPath(path);
            Kind = FileSniffer.Detect(Path);
            AppSettings.Current.AddRecent(Path);
            Log.Info("Opened " + Name + "  -  " + FileSniffer.Describe(Kind));
            Changed?.Invoke();
        }

        /// <summary>True while <see cref="Changed"/> is raised by <see cref="Refresh"/> (same file, re-read).</summary>
        public static bool IsRefresh { get; private set; }

        /// <summary>Re-reads the type after a tool modified the file (does not switch pages).</summary>
        public static void Refresh()
        {
            if (string.IsNullOrEmpty(Path)) return;
            Kind = FileSniffer.Detect(Path);
            IsRefresh = true;
            try { Changed?.Invoke(); }
            finally { IsRefresh = false; }
        }

        private static string _operation;

        /// <summary>
        /// Reserves the working file for one modifying operation (patches must never run concurrently on the
        /// same file). Returns null - after telling the user - when another operation is still running.
        /// </summary>
        public static IDisposable BeginOperation(string what)
        {
            if (_operation != null)
            {
                Dlg.Info("Please wait", "\"" + _operation + "\" is still running on " + Name + ". Try again when it has finished.");
                return null;
            }
            _operation = what;
            return new OperationScope();
        }

        private sealed class OperationScope : IDisposable
        {
            private bool _done;
            public void Dispose() { if (!_done) { _done = true; _operation = null; } }
        }

        public static void Clear()
        {
            Path = null;
            Kind = WorkingFileKind.None;
            Changed?.Invoke();
        }
    }
}
