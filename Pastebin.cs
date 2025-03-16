using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack
{
    internal class Pastebin
    {
        public static string CurrentVersion = "v1.2.3";
        public string LatestVersion;

        public string DownloadRawText(string pastebinRawUrl)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    return client.DownloadString(pastebinRawUrl);
                }
            }
            catch (Exception ex)
            {
                return string.Empty;
            }
        }

        public bool IsLatestVersion()
        {
                try
                {
                    if (DownloadRawText("https://pastebin.com/raw/emSPbb04") != CurrentVersion && IsInternetAvailable() == true)
                    {
                        ShowUpdateURL();
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    return true; // abort if exception
                }
        }

        public void ShowUpdateURL()
        {
            MessageBox.Show("An Update is avaliable I will now open the download link: \n\n Also note that the compiled version is located on the right under releases.");
            Process.Start("https://github.com/IcyModz420/X360GameHack2025");
        }

        public string GetLatestVersion()
        {
            return DownloadRawText("https://pastebin.com/raw/emSPbb04");
        }

        public static bool IsInternetAvailable()
        {
            try
            {
                Ping myPing = new Ping();
                String host = "8.8.8.8"; // Google's public DNS
                byte[] buffer = new byte[32];
                int timeout = 1000; // 1 second timeout
                PingOptions pingOptions = new PingOptions();

                PingReply reply = myPing.Send(host, timeout, buffer, pingOptions);

                return (reply.Status == IPStatus.Success);
            }
            catch (Exception)
            {
                return false; // Exception indicates no internet connection
            }
        }
    }
}
