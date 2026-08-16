using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace X360GameHack
{
    class XboxUnityAPI
    {


        public void GetLinkUsers()
        {
            try
            {
                // Create the HTTP request
                System.Net.HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://xboxunity.net/Resources/Lib/Stats.php");
                request.Method = "GET";

                // Set the headers as specified
                request.Accept = "application/json, text/javascript, */*; q=0.01";
                request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                // request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Safari/537.36";
                request.Referer = "http://xboxunity.net/";
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                //request.Headers.Add("Proxy-Connection", "keep-alive");

                // Configure additional request properties
                request.AllowAutoRedirect = true; // Follow redirects if any
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; // Handle compressed responses

                // Send the request and get the response synchronously
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        // Read the response content
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            string content = reader.ReadToEnd();

                            // Add JSON parsing and number extraction
                            JObject json = JObject.Parse(content);
                            string link = (string)json["Link"];
                            string online = (string)json["Online"];
                            if (link != null && online != null)
                            {
                                string[] linkParts = link.Split(' ');
                                string[] onlineParts = online.Split(' ');
                                if (linkParts.Length > 0 && onlineParts.Length > 0)
                                {
                                    string usersOnLink = linkParts[0];
                                    string usersOnline = onlineParts[0];
                                    string message = $"Users on LiNK: {usersOnLink}\nUsers Online: {usersOnline}\n\nFull response copied to clipboard.";
                                    MessageBox.Show(message, "Response", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                                else
                                {
                                    MessageBox.Show("Failed to extract user numbers from response.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }
                            else
                            {
                                MessageBox.Show("Required fields missing in response.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }

                            Clipboard.SetText(content);
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                // Handle web-specific exceptions (e.g., network errors or server errors with a response)

                MessageBox.Show($"webexception: "+ex, "Request Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
            catch (Exception ex)
            {
                // Handle any other unexpected exceptions
                MessageBox.Show($"Exception: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }



}
