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
            if (inputPath.Any(char.IsWhiteSpace))
            {
                MessageBox.Show("File path contains a space.. Spaces are not allowed in file paths this includes folder names and files. \n Acceptable examples of paths include but are not limited to desktop, documents, folders like that, usbs with no spaces in folder or file names etc.. \n This is a limitation of XEXTool and the 14+ year old cmd apps.. I will code around it so you can rename it eventually but for now just remove the spaces from the folder names or drag it onto your desktop to patch it..");
                inputPath = "";
                return "";
            }
            if (Regex.IsMatch(inputPath, @"[&|;<>\`\n\r""]"))
            {
                MessageBox.Show("File path contains illegal characters.");
                inputPath = "";
                return "";
            }
            string extension = Path.GetExtension(Path.GetFullPath(inputPath)).ToLowerInvariant();
            var allowedExtensions = new[] { ".xex", ".xbe", ".iso", ".bin", ".exe", ".dll" };
            if (!string.IsNullOrEmpty(extension) && !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                if (inputPath.Contains(".py") || inputPath.Contains(".batch") || inputPath.Contains("cmd") || inputPath.Contains("powershell"))
                {
                    inputPath = "";
                    throw new SecurityException("X360GameHack: USER BE WARNED: Possible command injection was just blocked before it could cause harm! It did for a fact however contain a intended to be used .py .batch or a embedded cmd or powershell command.. THIS IS NOT A MISTAKE!");
                    Process.GetCurrentProcess().Kill(); // this is reachable keep it here to make sure the process is killed if the exception is not caught for some reason.
                }
                MessageBox.Show("Illegal extention file must be .xex, .xbe, .iso, .bin, .exe, .dll, or no extension.");
            }
            if (!File.Exists(Path.GetFullPath(inputPath)))
            {
                MessageBox.Show("File used in last path does not exist! \n // \n Goof azz dude you can't do that here! \n\nWhichever applys..", "X360GameHack: Sanitization Failed!");
                inputPath = "";
                return "";
            }
            return Path.GetFullPath(inputPath);
        }
    }
}