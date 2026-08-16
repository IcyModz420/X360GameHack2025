using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack
{
    public class Pastebin
    {
        public readonly string CurrentVersion = "v3.7.8";
        private readonly string link = "https://github.com/IcyModz420/X360GameHack2025/releases";
        public readonly string YYY;

        public string DownloadRawText(string pastebinRawUrl)
        {
            try
            {
                using (WebClient WebClient = new WebClient())
                {
                    WebClient.Proxy = null;
                    return WebClient.DownloadString(pastebinRawUrl);
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public bool IsLatestVersion()
        {
            try
            {
                if (IsInternetAvailable() == true && DownloadRawText("https://pastebin.com/raw/emSPbb04") != CurrentVersion)
                {
                    // is not current version
                    if (Properties.Settings.Default.HasBeenAskedToUpdate == false)
                    {
                        ShowUpdateURL();
                    }
                        return false;
                }
                else
                {
                    // is current version
                    return true;
                }
            }
            catch (Exception)
            {
                return false; // do not abort if exception
            }
        }

        public static int? GetOpenCount()
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Proxy = null;
                    string text = client.DownloadString("https://pastebin.com/emSPbb04");
                    // Match numbers not followed by a decimal point
                    MatchCollection matches = Regex.Matches(text, @"\b\d+\b(?!\.)");
                    foreach (Match match in matches)
                    {
                        if (int.TryParse(match.Value, out int number) && number > 2026)
                        {
                            return number;
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public void ShowUpdateURL()
        {
            DialogResult result = MessageBox.Show(
           "Update " + GetLatestVersion() + " is avaliable! Would you like to open the download link with default browser? \n\n Also note that the compiled version is located on the right under releases and that you may need to click releases and scroll to the top to find the latest version to download if its a pre release. \n After you click yes this message will no longer appear but it will still show in the title if it needs an update you can also toggle this message off in settings.",
           "X360GameHack Info!",
           MessageBoxButtons.YesNo,
           MessageBoxIcon.Question);
            if (result == DialogResult.No)
            {


            }
            if (result == DialogResult.Yes)
            {
                Properties.Settings.Default.HasBeenAskedToUpdate = true;
                Properties.Settings.Default.Save();
                Process.Start(link);

            }
        }

        public string GetLatestVersion()
        {
            return DownloadRawText("https://pastebin.com/raw/emSPbb04");
        }

        public bool IsInternetAvailable()
        {
            if (DownloadRawText("https://pastebin.com/raw/emSPbb04").Contains("."))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public string GetYYY()
        {
            return DownloadRawText("https://pastebin.com/raw/pyMcwJS1");
        }

        public string GetZZZ()
        {
            return DownloadRawText("https://pastebin.com/raw/ALRPJnMB");
        }

        public bool HasBeenAskedToUpdate()
        {
            if (!Properties.Settings.Default.HasBeenAskedToUpdate)
            {
                return false;
            }
            else if (Properties.Settings.Default.HasBeenAskedToUpdate)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
