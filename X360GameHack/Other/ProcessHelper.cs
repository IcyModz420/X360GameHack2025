using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace X360GameHack.Other
{
    public class ProcessHelper
    {
        public void WaitForProcessByNameAsync(string processName, int appearTimeoutMs = 60000, int exitTimeoutMs = 0)
        {
            // --- Phase 1: wait for the process to appear ---
            var stopwatch = Stopwatch.StartNew();
            Process[] procs;

            do
            {
                procs = Process.GetProcessesByName(processName);
                if (procs.Length > 0)
                    break;

                 Task.Delay(200);
            }
            while (appearTimeoutMs <= 0 || stopwatch.ElapsedMilliseconds < appearTimeoutMs);

            if (procs.Length == 0)
            {
                // Never showed up within the timeout — decide whether that's an error
                // or just means "nothing to wait on." Here we just return quietly.
                return;
            }

            // --- Phase 2: wait for ALL matching instances to exit ---
            var exitStopwatch = Stopwatch.StartNew();

            while (true)
            {
                procs = Process.GetProcessesByName(processName);
                if (procs.Length == 0)
                    break; // all instances closed

                if (exitTimeoutMs > 0 && exitStopwatch.ElapsedMilliseconds > exitTimeoutMs)
                    throw new TimeoutException($"{processName} did not exit within {exitTimeoutMs}ms");

                Task.Delay(300);
            }
        }

        public void KillAllProcessesByName(string processName)
        {
            // processName WITHOUT ".exe" — e.g. "extract-xiso"
            Process[] processes = Process.GetProcessesByName(processName);

            foreach (var proc in processes)
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(5000); // give it a moment to actually die
                }
                catch (Exception ex)
                {
                    // process may have already exited between GetProcessesByName and Kill(),
                    // or you may lack permission (e.g. elevated process, protected process)
                    Console.WriteLine($"Failed to kill {processName} (PID {proc.Id}): {ex.Message}");
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
    }
}
