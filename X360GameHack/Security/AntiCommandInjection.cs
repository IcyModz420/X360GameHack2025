using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack.Security
{
    [Confuser.Obfuscate]
    public class AntiCommandInjection
    {
        AntiDebug AntiDebug = new AntiDebug();

        public string SanitizeInvokerFilePath(string inputPath)
        {
            AntiDebug.DoAntiDebugFunc();

            if (string.IsNullOrWhiteSpace(inputPath))
            {
                MessageBox.Show("File path is empty.");
                return "";
            }

            if (inputPath.Any(char.IsWhiteSpace))
            {
                MessageBox.Show("File path contains a space.. Spaces are not allowed in file paths this includes folder names and files. \n Acceptable examples of paths include but are not limited to desktop, documents, folders like that, usbs with no spaces in folder or file names etc.. \n This is a limitation of XEXTool and the 14+ year old cmd apps.. I will code around it so you can rename it eventually but for now just remove the spaces from the folder names or drag it onto your desktop to patch it..");
                return "";
            }

            // Reject any character Windows itself considers invalid in a path
            if (inputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                MessageBox.Show("File path contains invalid characters.");
                return "";
            }

            // Block shell/command metacharacters and traversal sequences up front.
            // Includes: pipes, redirects, separators, quoting, variable expansion,
            // subshell/glob chars, and backslash-escape tricks.
            if (Regex.IsMatch(inputPath, @"[&|;<>`\n\r""'$(){}\[\]*?%^!~#=]") ||
                inputPath.Contains("..") ||
                inputPath.Contains("%"))
            {
                MessageBox.Show("File path contains illegal characters.");
                return "";
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(inputPath);
            }
            catch (Exception)
            {
                MessageBox.Show("File path is malformed or not resolvable.");
                return "";
            }

            // Re-check the RESOLVED path too, so relative segments can't sneak
            // something past the raw-string checks above.
            if (fullPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                MessageBox.Show("File path contains invalid characters.");
                return "";
            }

            string extension = Path.GetExtension(fullPath).ToLowerInvariant();

            var allowedExtensions = new[] { ".xex", ".xbe", ".iso", ".bin", ".exe", ".dll" };
            if (!string.IsNullOrEmpty(extension) && !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            { //if the extension is not empty and is not in the allowed list, then show a message box and return an empty string
                MessageBox.Show("Illegal extension. File must be .xex, .xbe, .iso, .bin, .exe, .dll, or no extension.");
                return ""; // was previously missing — this used to fall through!
            }

            var notAllowedExtensions = new[]
            {
              ".py", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse",
              ".wsf", ".wsh", ".msi", ".scr", ".sh", ".reg", ".hta", ".com"
            };
            if (notAllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                KillAndThrow("X360GameHack: USER BE WARNED: Possible command injection was just blocked before it could cause harm! The path pointed to a script/executable type (.py, .bat, .cmd, .ps1, etc.) that is never allowed here. THIS IS NOT A MISTAKE!");
                return "";
            }

            // Check resolved full path (not raw input) so relative traversal can't bypass this.
            var notAllowedFolderNames = new[] { "WINDOWS", "system32", "SysWOW64", "Microsoft.NET", "Installer" };
            if (notAllowedFolderNames.Any(name => fullPath.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                KillAndThrow("X360GameHack: USER BE WARNED: Path resolved into a protected system folder (Windows/System32/SysWOW64/etc.). Blocked before it could cause harm. THIS IS NOT A MISTAKE!");
                return "";
            }

            // Reserved Windows device names can hang or misbehave when handed to
            // old cmd-line tools (CON, PRN, AUX, NUL, COM1-9, LPT1-9).
            string nameNoExt = Path.GetFileNameWithoutExtension(fullPath);
            var reservedNames = new[]
            {
              "CON", "PRN", "AUX", "NUL",
              "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
              "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
            };
            if (reservedNames.Contains(nameNoExt, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show("File name uses a reserved system device name.");
                return "";
            }

            if (!File.Exists(fullPath))
            {
                MessageBox.Show("File used in last path does not exist! \n // \n Goof azz dude you can't do that here! \n\nWhichever applys..", "X360GameHack: Sanitization Failed!");
                return "";
            }
            return fullPath;
        }

        // Actually terminates the process instead of relying on unreachable code after a throw.
        private void KillAndThrow(string message)
        {
            throw new SecurityException(message);
            Process.GetCurrentProcess().Kill(); // this is reachable
        }
    }
}