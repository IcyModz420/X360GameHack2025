using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Net
{
    public sealed class TitleUpdateInfo
    {
        public string MediaId = "";
        public string TitleUpdateId = "";
        public int Version;
        public string BaseVersion = "";
        public string HeaderHash = "";     // STFS header hash (package offset 0x32C)
        public long SizeKb;
        public string UploadDate = "";
        public string Name = "";
    }

    /// <summary>
    /// Title-update lookup on XboxUnity (the list Aurora / FSD show). Files are never downloaded by this app -
    /// XboxUnity wants them fetched from its own site - but a file you downloaded is verified and identified here:
    /// it must be an intact STFS package, and its header hash tells exactly which listed update it is.
    /// </summary>
    public static class XboxUnity
    {
        public const string SiteUrl = "https://xboxunity.net/";
        private const string Base = "https://xboxunity.net/Resources/Lib/";
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("X360GameHack/4.0");
            return c;
        }

        /// <summary>The STFS header hash of a package after checking the package is intact (throws otherwise).</summary>
        public static string HeaderHash(string path)
        {
            Verify(path, null);
            byte[] head = BE.ReadHead(path, 0x10000);
            return BE.Hex(head, 0x32C, 20);
        }

        /// <summary>The listed update a downloaded file is, matched by header hash (null = not in the list).</summary>
        public static TitleUpdateInfo Identify(string path, IEnumerable<TitleUpdateInfo> listed)
        {
            string h = HeaderHash(path);
            return listed.FirstOrDefault(t => string.Equals(t.HeaderHash, h, StringComparison.OrdinalIgnoreCase));
        }

        public static async Task<List<TitleUpdateInfo>> FindAsync(uint titleId, CancellationToken ct)
        {
            string json;
            using (var resp = await Http.GetAsync(Base + "TitleUpdateInfo.php?titleid=" + titleId.ToString("X8"), ct))
            {
                resp.EnsureSuccessStatusCode();
                json = await resp.Content.ReadAsStringAsync();
            }
            var list = new List<TitleUpdateInfo>();
            JObject root;
            try { root = JObject.Parse(json); }
            catch { throw new InvalidDataException("XboxUnity returned an unexpected answer."); }
            foreach (var media in root["MediaIDS"] as JArray ?? new JArray())
            {
                string mediaId = (string)media["MediaID"] ?? "";
                foreach (var u in media["Updates"] as JArray ?? new JArray())
                {
                    var tu = new TitleUpdateInfo
                    {
                        MediaId = mediaId.ToUpperInvariant(),
                        TitleUpdateId = (string)u["TitleUpdateID"] ?? "",
                        BaseVersion = (string)u["BaseVersion"] ?? "",
                        HeaderHash = ((string)u["hash"] ?? "").ToUpperInvariant(),
                        UploadDate = (string)u["UploadDate"] ?? "",
                        Name = (string)u["Name"] ?? ""
                    };
                    int.TryParse((string)u["Version"], out tu.Version);
                    long.TryParse((string)u["Size"], out tu.SizeKb);
                    if (Regex.IsMatch(tu.TitleUpdateId, "^[0-9]{1,10}$") && Regex.IsMatch(tu.HeaderHash, "^[0-9A-F]{40}$")) list.Add(tu);
                }
            }
            return list;
        }

        /// <summary>
        /// STFS header hash = SHA1(package[0x344 .. HeaderSize rounded up to 4 KB]), stored at 0x32C.
        /// Both the stored value and XboxUnity's listed value must match the recomputed one.
        /// </summary>
        public static void Verify(string path, string expectedHeaderHash)
        {
            byte[] head = BE.ReadHead(path, 0x10000);
            string magic = head.Length >= 4 ? System.Text.Encoding.ASCII.GetString(head, 0, 4) : "";
            if (magic != "LIVE" && magic != "PIRS" && magic != "CON ") throw new InvalidDataException("The download is not an Xbox 360 package.");
            long headerSize = BE.U32(head, 0x340);
            long end = (headerSize + 0xFFF) & ~0xFFFL;
            if (end <= 0x344 || end > head.Length) throw new InvalidDataException("The downloaded package header is malformed.");
            string computed;
            using (var sha = SHA1.Create()) computed = BitConverter.ToString(sha.ComputeHash(head, 0x344, (int)(end - 0x344))).Replace("-", "");
            string stored = BE.Hex(head, 0x32C, 20);
            if (computed != stored) throw new InvalidDataException("The package is corrupt or incomplete (its header hash does not match).");
            if (!string.IsNullOrEmpty(expectedHeaderHash) && computed != expectedHeaderHash.ToUpperInvariant())
                throw new InvalidDataException("This file is not the selected update (its hash does not match XboxUnity's listing).");
        }
    }
}
