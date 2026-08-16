using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack.Security
{
    public class AntiAdmin
    {
        public void EnforceNonAdmin()
        {
            if (IsRunningAsAdmin())
            {
                Debug.WriteLine("The application is running with administrative privileges. Exiting..."); // Don't leak the name intentionally make it harder to find
                // Chokeee
                Process.GetCurrentProcess().Kill();
                Environment.Exit(0);
                Application.Exit();
            }
        }

        /// <summary>
        /// Evaluates the current Windows identity principal role.
        /// </summary>
        private bool IsRunningAsAdmin()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {         
                return false;
            }
        }
    }
}
