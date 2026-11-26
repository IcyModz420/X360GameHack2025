using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace X360GameHack.Core.Mods
{
    public sealed class ActivationResult
    {
        public bool Ok;
        public string Error;
        public byte[] KeyAes, KeyMac;   // for a sealed mod
        public byte[] Payload;          // for a server-only mod (the data itself, never written to disk)
    }

    /// <summary>The licence server's answer for several mods at once.</summary>
    public sealed class BatchActivation
    {
        public bool Ok;
        public string Error;
        public readonly Dictionary<string, ActivationResult> Mods = new Dictionary<string, ActivationResult>(StringComparer.Ordinal);
    }

    /// <summary>One mod file the licence server holds for a licence (what it is - never its keys).</summary>
    public sealed class ServerPack
    {
        public string ModId = "", Name = "", Version = "", Sha256 = "";
        public long Size;
    }

    /// <summary>The mod files a licence may download.</summary>
    public sealed class PackList
    {
        public bool Ok;
        public string Error;
        public readonly List<ServerPack> Packs = new List<ServerPack>();
    }

    /// <summary>
    /// Talks to the licence server. It sends only the buyer's token, the mod ids and this machine's id, and gets
    /// back either the keys that open a sealed mod, or - for a server-only mod - the mod data itself. Nothing
    /// secret is stored on disk: the keys/data live in memory for the session and are wiped when the mod is
    /// locked again. The server is what enforces "one token, one machine": it binds a token to the first machine
    /// id it sees and refuses a second one.
    /// </summary>
    public static class ActivationClient
    {
        public static ActivationResult Activate(string serverUrl, string token, ModInfo mod, string hardwareId)
        {
            if (string.IsNullOrWhiteSpace(token)) return new ActivationResult { Error = "Enter your licence token." };
            var body = new { token = token.Trim(), modId = mod.Id, serverOnly = mod.ServerOnly, hardwareId, client = "X360GameHack" };
            string error;
            JObject j = Post(serverUrl, body, out error);
            return j == null ? new ActivationResult { Error = error } : Parse(j);
        }

        /// <summary>Every mod in one request: one licence token can cover all of them.</summary>
        public static BatchActivation ActivateAll(string serverUrl, string token, IList<ModInfo> mods, string hardwareId)
        {
            if (string.IsNullOrWhiteSpace(token)) return new BatchActivation { Error = "Enter your licence token." };
            if (mods == null || mods.Count == 0) return new BatchActivation { Ok = true };
            var body = new
            {
                token = token.Trim(),
                hardwareId,
                mods = mods.Select(m => new { modId = m.Id, serverOnly = m.ServerOnly }).ToArray(),
                client = "X360GameHack",
            };
            string error;
            JObject j = Post(serverUrl, body, out error);
            if (j == null) return new BatchActivation { Error = error };
            if (j["ok"] == null || j["ok"].Type != JTokenType.Boolean || !(bool)j["ok"])
                return new BatchActivation { Error = (string)j["error"] ?? "The licence server refused this token." };
            var result = new BatchActivation { Ok = true };
            if (j["mods"] is JObject each)
                foreach (var p in each.Properties())
                {
                    if (!(p.Value is JObject one)) continue;
                    try
                    {
                        result.Mods[p.Name] = one["error"] != null
                            ? new ActivationResult { Error = (string)one["error"] }
                            : new ActivationResult
                            {
                                Ok = true,
                                KeyAes = one["kAes"] != null ? Convert.FromBase64String((string)one["kAes"]) : null,
                                KeyMac = one["kMac"] != null ? Convert.FromBase64String((string)one["kMac"]) : null,
                                Payload = one["payload"] != null ? Convert.FromBase64String((string)one["payload"]) : null,
                            };
                    }
                    catch (FormatException) { result.Mods[p.Name] = new ActivationResult { Error = "The licence server's answer for this mod is not readable." }; }
                }
            return result;
        }

        private static readonly System.Text.RegularExpressions.Regex ModIdText = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9._-]{1,64}$");
        private static readonly System.Text.RegularExpressions.Regex Hex64 = new System.Text.RegularExpressions.Regex(@"^[0-9a-f]{64}$");
        private const long MaxPack = 1400000;

        /// <summary>The mod files this licence may download from the licence server (mod files are handed out only there).</summary>
        public static PackList ListPacks(string serverUrl, string token, string hardwareId)
        {
            if (string.IsNullOrWhiteSpace(token)) return new PackList { Error = "Enter your licence token." };
            string error;
            JObject j = Post(serverUrl, new { token = token.Trim(), hardwareId, client = "X360GameHack" }, out error, "/mods");
            if (j == null) return new PackList { Error = error };
            if (j["ok"] == null || j["ok"].Type != JTokenType.Boolean || !(bool)j["ok"]) return new PackList { Error = (string)j["error"] ?? "The licence server refused this token." };
            var list = new PackList { Ok = true };
            if (j["packs"] is JArray packs)
                foreach (var p in packs.OfType<JObject>())
                {
                    var x = new ServerPack { ModId = (string)p["modId"] ?? "", Name = (string)p["name"] ?? "", Version = (string)p["version"] ?? "", Sha256 = ((string)p["sha256"] ?? "").ToLowerInvariant(), Size = (long?)p["size"] ?? 0 };
                    // anything odd is left out rather than trusted
                    if (ModIdText.IsMatch(x.ModId) && Hex64.IsMatch(x.Sha256) && x.Size > 11 && x.Size <= MaxPack) list.Packs.Add(x);
                }
            return list;
        }

        /// <summary>
        /// Downloads one mod file for this licence. Kept only when its size and SHA-256 are what the server listed and it is
        /// an X360GameHack mod file with that id; otherwise null and <paramref name="error"/> says why.
        /// </summary>
        public static byte[] DownloadPack(string serverUrl, string token, string hardwareId, ServerPack pack, out string error)
        {
            JObject j = Post(serverUrl, new { token = token.Trim(), hardwareId, modId = pack.ModId, client = "X360GameHack" }, out error, "/download");
            if (j == null) return null;
            if (j["ok"] == null || j["ok"].Type != JTokenType.Boolean || !(bool)j["ok"]) { error = (string)j["error"] ?? "The licence server refused this download."; return null; }
            byte[] data;
            try { data = Convert.FromBase64String((string)j["data"] ?? ""); }
            catch (FormatException) { error = "The licence server's answer is not readable."; return null; }
            string sha;
            using (var h = System.Security.Cryptography.SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            if (data.Length != pack.Size || sha != pack.Sha256) { error = "The downloaded mod file does not match what the licence server listed (size / SHA-256) - it was not kept."; return null; }
            try
            {
                var p = ModPack.Parse(data);
                if (p.Info == null || p.Info.Id != pack.ModId) { error = "The downloaded mod file is for another mod - it was not kept."; return null; }
                if (!p.OwnerSigned) { error = "The downloaded mod file is not signed by the X360GameHack team - it was not kept."; return null; }
            }
            catch (InvalidDataException ex) { error = "The downloaded file is not a mod file (" + ex.Message + ") - it was not kept."; return null; }
            return data;
        }

        /// <summary>
        /// The public list of every mod a licence unlocks (GET /catalog: names and descriptions only - no token, nothing
        /// about this PC is sent). Fields are checked and capped; anything odd is left out rather than trusted.
        /// </summary>
        public static ModCatalogList GetCatalog(string serverUrl)
        {
            var list = new ModCatalogList();
            Uri uri;
            if (string.IsNullOrWhiteSpace(serverUrl) || !Uri.TryCreate(serverUrl.TrimEnd('/') + "/catalog", UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            { list.Error = "The mod server address is not a secure (https) address."; return list; }
            string text;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.Method = "GET";
                req.Timeout = 15000;
                req.UserAgent = "X360GameHack";
                if (uri.IsLoopback) req.Proxy = null;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream()))
                {
                    var buf = new char[8192]; var sb = new StringBuilder(); int n;
                    while ((n = r.Read(buf, 0, buf.Length)) > 0) { sb.Append(buf, 0, n); if (sb.Length > 2 * 1024 * 1024) { list.Error = "The mod list is too large."; return list; } }
                    text = sb.ToString();
                }
            }
            catch (WebException wex) when (wex.Response is HttpWebResponse hr)
            {
                list.Error = hr.StatusCode == HttpStatusCode.NotFound ? "the mod server does not offer the list of mods yet" : "the mod server answered " + (int)hr.StatusCode;
                return list;
            }
            catch (WebException) { list.Error = "the mod server could not be reached - check your internet connection"; return list; }
            catch (Exception ex) { list.Error = ex.Message; return list; }
            return ModCatalogList.FromJson(text);
        }

        /// <summary>Sends one request; null and an error message when there is no usable answer.</summary>
        private static JObject Post(string serverUrl, object body, out string error, string path = "/activate")
        {
            error = null;
            if (string.IsNullOrWhiteSpace(serverUrl)) { error = "The licence server could not be reached."; return null; }
            Uri uri;
            if (!Uri.TryCreate(serverUrl.TrimEnd('/') + path, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            {
                // a token must never travel in the clear; only a test server on this PC may use plain http
                error = "The licence server address is not a secure (https) address.";
                return null;
            }
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body));
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 20000;
                req.UserAgent = "X360GameHack";                 // never the user's name or email
                if (uri.IsLoopback) req.Proxy = null;            // a local test server must not go through a system proxy
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream()))
                {
                    // a mod file is at most 1.4 MB (about 1.9 MB as base64); anything bigger is not a real answer
                    var buf = new char[8192]; var sb = new StringBuilder(); int n;
                    while ((n = r.Read(buf, 0, buf.Length)) > 0) { sb.Append(buf, 0, n); if (sb.Length > 4 * 1024 * 1024) { error = "The licence server's answer is too large."; return null; } }
                    return JObject.Parse(sb.ToString());
                }
            }
            catch (WebException wex) when (wex.Response is HttpWebResponse hr)
            {
                try { using (var r = new StreamReader(hr.GetResponseStream())) return JObject.Parse(r.ReadToEnd()); }
                catch { error = "The licence server answered " + (int)hr.StatusCode + "."; return null; }
            }
            catch (WebException) { error = "The licence server could not be reached. Check your internet connection."; return null; }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private static ActivationResult Parse(JObject j)
        {
            if (j["ok"] == null || j["ok"].Type != JTokenType.Boolean || !(bool)j["ok"]) return new ActivationResult { Error = (string)j["error"] ?? "The licence server refused this token." };
            var r = new ActivationResult { Ok = true };
            try
            {
                if (j["kAes"] != null) r.KeyAes = Convert.FromBase64String((string)j["kAes"]);
                if (j["kMac"] != null) r.KeyMac = Convert.FromBase64String((string)j["kMac"]);
                if (j["payload"] != null) r.Payload = Convert.FromBase64String((string)j["payload"]);
            }
            catch (FormatException) { return new ActivationResult { Error = "The licence server's answer is not readable." }; }
            return r;
        }
    }
}
