using System;
using System.Windows.Forms;
using X360GameHack.Core.Services;
using X360GameHack.Other;
using X360GameHack.Security;
using X360GameHack.UI;

namespace X360GameHack
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            new AntiAdmin().EnforceNonAdmin();     // never run elevated: tools we launch inherit our rights

            // "X360GameHack.exe cli <command> ...": no window - one JSON answer on standard output (for scripts and AI agents)
            string[] args = Environment.GetCommandLineArgs();
            // the update helper (a copy of this program in the user's Updates folder): applies a checked download
            if (args.Length > 1 && args[1] == "--apply-update")
            {
                var rest = new string[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                AppUpdater.ShowMessage = UI.UpdateMessage.Show;           // its messages in the saved theme too
                Environment.Exit(AppUpdater.RunApply(rest));
            }
            if (args.Length > 1 && string.Equals(args[1], "cli", StringComparison.OrdinalIgnoreCase))
            {
                var rest = new string[args.Length - 2];
                Array.Copy(args, 2, rest, 0, rest.Length);
                Environment.Exit(RunCli(rest));
            }
            // "X360GameHack.exe mcp": the same commands as a Model Context Protocol server on standard input / output
            if (args.Length > 1 && string.Equals(args[1], "mcp", StringComparison.OrdinalIgnoreCase))
            {
                var utf8 = new System.Text.UTF8Encoding(false);
                using (var stdin = new System.IO.StreamReader(Console.OpenStandardInput(), utf8))
                using (var stdout = new System.IO.StreamWriter(Console.OpenStandardOutput(), utf8))
                    Environment.Exit(AgentCli.Serve(stdin, stdout));
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception);

            AppSettings.Load();
            new MultiExecutionFix().CheckForMultiExecution();
            Application.Run(new Shell());
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private static int RunCli(string[] args)
        {
            // a windowed program has no console of its own: when the output is not captured, borrow the caller's
            bool captured;
            try { captured = Console.IsOutputRedirected; } catch { captured = false; }
            if (!captured) { try { AttachConsole(-1); } catch { } }
            using (var stdout = new System.IO.StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)))
                return AgentCli.Run(args, stdout);
        }

        private static void Report(Exception ex)
        {
            if (ex == null) return;
            while ((ex is System.Reflection.TargetInvocationException || ex is AggregateException) && ex.InnerException != null)
                ex = ex.InnerException;
            Log.Write(LogLevel.Error, "Unexpected error: " + ex);
            try { Dlg.Error("Unexpected error", ex.Message + "\n\nThe details were written to the activity log."); }
            catch { MessageBox.Show(ex.ToString(), "X360GameHack"); }
        }
    }
}
