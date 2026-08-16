using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack
{
    class AntiDebug
    {
        [DllImport("kernel32.dll")]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);
        private readonly string Creator = new StringXORing("DESKTOP-K0QIDKI"); // protected no need to obfuscate

        [Confuser.Obfuscate]
        public void DoAntiDebugFunc()
        {
            if (Debugger.IsAttached || IsDebuggerPresent())
            {
                if (Environment.MachineName == Creator){}
                else
                {
                    throw new SecurityException ("Debugger Detected! This is not allowed in the release version! If you want to debug it manually disable this.");
                }
            }
        }
        [Confuser.Obfuscate]
        private static bool IsDebuggerPresent()
        {
            bool isDebuggerPresent = false;
            CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref isDebuggerPresent);
            return isDebuggerPresent;
        }
    }
}
