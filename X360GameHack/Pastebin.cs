using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack
{
    internal class Pastebin
    {
        public string CurrentVersion = "1.1.2";







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
                if (DownloadRawText("https://pastebin.com/raw/emSPbb04") == CurrentVersion)
                {
                    return true;
                }
                else
                {
                    ShowUpdateURL();
                    return false;
                }
            }
            catch (Exception)
            {
                ShowUpdateURL();
                return false;
            }
        }

        public void ShowUpdateURL()
        {
            MessageBox.Show("An Update is avaliable I will now open the download link: \n\n Also note that the compiled version is located on the right under releases.");
            Process.Start("https://github.com/IcyModz420/X360GameHack2025");
        }


    }
}
