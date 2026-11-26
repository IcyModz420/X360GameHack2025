using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Services
{
    public enum LogLevel { Info, Success, Warning, Error, Tool }

    public sealed class LogEntry
    {
        public DateTime Time;
        public LogLevel Level;
        public string Text;
    }

    /// <summary>Thread-safe application log. The UI subscribes to <see cref="Written"/>.</summary>
    public static class Log
    {
        private static readonly object Sync = new object();
        private static int _writes;
        private static readonly List<LogEntry> History = new List<LogEntry>();
        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "X360GameHack", "logs.txt");

        /// <summary>Raised on the calling thread; UI must marshal.</summary>
        public static event Action<LogEntry> Written;

        public static void Info(string text) => Write(LogLevel.Info, text);
        public static void Success(string text) => Write(LogLevel.Success, text);
        public static void Warn(string text) => Write(LogLevel.Warning, text);
        public static void Error(string text) => Write(LogLevel.Error, text);
        public static void Tool(string text) => Write(LogLevel.Tool, text);

        public static void Write(LogLevel level, string text)
        {
            if (text == null) return;
            var entry = new LogEntry { Time = DateTime.Now, Level = level, Text = text };
            lock (Sync)
            {
                History.Add(entry);
                if (History.Count > 5000) History.RemoveRange(0, 1000);
                if (AppSettings.Current.KeepLogFile)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(LogFile));
                        if (++_writes % 200 == 1 && File.Exists(LogFile) && new FileInfo(LogFile).Length > 5 * 1024 * 1024)
                        {
                            string old = Path.ChangeExtension(LogFile, ".old.txt");
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(LogFile, old);   // keep at most ~10 MB of history
                        }
                        File.AppendAllText(LogFile, Format(entry) + Environment.NewLine, Encoding.UTF8);
                    }
                    catch { /* logging must never crash the app */ }
                }
            }
            try { Written?.Invoke(entry); } catch { /* ignore subscriber faults */ }
        }

        public static string Format(LogEntry e)
        {
            string tag;
            switch (e.Level)
            {
                case LogLevel.Success: tag = "OK  "; break;
                case LogLevel.Warning: tag = "WARN"; break;
                case LogLevel.Error: tag = "ERR "; break;
                case LogLevel.Tool: tag = "TOOL"; break;
                default: tag = "INFO"; break;
            }
            return $"[{e.Time:HH:mm:ss}] {tag} {e.Text}";
        }

        public static string LogFilePath => LogFile;

        public static string Export()
        {
            var sb = new StringBuilder();
            lock (Sync)
                foreach (var e in History) sb.AppendLine(Format(e));
            return sb.ToString();
        }

        public static void Clear()
        {
            lock (Sync) History.Clear();
        }

        /// <summary>The most recent entries (oldest first) - used to refill the log after the window is rebuilt.</summary>
        public static List<LogEntry> Snapshot(int max)
        {
            lock (Sync) return History.Skip(Math.Max(0, History.Count - max)).ToList();
        }
    }
}
