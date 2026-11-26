using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace X360GameHack.Core.Services
{
    /// <summary>One Xbox 360 achievement as the public title database lists it.</summary>
    public sealed class OnlineAchievement
    {
        public uint Id, Gamerscore, Flags;
        public string Name = "", Description = "", LockedDescription = "";
    }

    /// <summary>A game found in the public title database.</summary>
    public sealed class TitleMatch
    {
        public uint TitleId;
        public string Name = "";
        public string TitleIdHex => TitleId.ToString("X8");
        public override string ToString() => TitleIdHex + "  " + Name;
    }

    /// <summary>
    /// Game names and achievement lists by Title ID, from the public Xbox title database at dbox.tools. Only ever
    /// asked when the user presses a button (Look up, Add a game by Title ID); the request carries the Title ID or the
    /// name typed, nothing about the user or the PC. Names found are kept on this PC (title-names.json) so the next
    /// lookup of the same game needs no internet. Answers are checked and cleaned before use: text is cut to a sane
    /// length and stripped of control characters, numbers are range-checked, and anything unexpected is ignored.
    /// </summary>
    public static class TitleLookup
    {
        public const string Api = "https://dbox.tools/api/";
        /// <summary>For automated tests only: a loopback address standing in for <see cref="Api"/>. Null in normal use.</summary>
        internal static string TestApi;

        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly object Gate = new object();
        private static Dictionary<uint, string> _names;

        private static string CacheFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "X360GameHack", "title-names.json");

        /// <summary>Parses "415608FC" / "0x415608FC". False for anything else, for 0 and for the system's own IDs (FFFExxxx, FFFFxxxx).</summary>
        public static bool TryParseTitleId(string text, out uint titleId)
        {
            titleId = 0;
            string t = (text ?? "").Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            if (t.Length != 8 || !uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out titleId)) return false;
            return titleId != 0 && titleId >> 16 != 0xFFFE && titleId >> 16 != 0xFFFF;
        }

        /// <summary>The name of a game looked up before on this PC, or null (no internet used).</summary>
        public static string Cached(uint titleId)
        {
            lock (Gate) { Load(); return _names.TryGetValue(titleId, out string n) ? n : null; }
        }

        /// <summary>The game's name: from this PC when known, else from the title database. Null when nobody knows it.</summary>
        public static async Task<string> NameAsync(uint titleId, CancellationToken ct)
        {
            string known = Cached(titleId);
            if (known != null) return known;
            var o = await GetJsonAsync("title_ids/" + titleId.ToString("X8"), 64 * 1024, ct).ConfigureAwait(false) as JObject;
            if (o == null || !string.Equals((string)o["title_id"], titleId.ToString("X8"), StringComparison.OrdinalIgnoreCase)) return null;
            string name = Clean((string)o["name"], 127);
            if (name.Length == 0) return null;
            Remember(titleId, name);
            return name;
        }

        /// <summary>Xbox 360 games whose name contains <paramref name="text"/> (at most 25).</summary>
        public static async Task<List<TitleMatch>> SearchAsync(string text, CancellationToken ct)
        {
            string q = Clean(text, 80);
            var found = new List<TitleMatch>();
            if (q.Length < 2) return found;
            var o = await GetJsonAsync("title_ids/?system=XBOX360&limit=25&name=" + Uri.EscapeDataString(q), 512 * 1024, ct).ConfigureAwait(false) as JObject;
            if (!(o?["items"] is JArray items)) return found;
            foreach (var it in items.OfType<JObject>())
            {
                if (!TryParseTitleId((string)it["title_id"], out uint id)) continue;
                string name = Clean((string)it["name"], 127);
                if (name.Length == 0) continue;
                found.Add(new TitleMatch { TitleId = id, Name = name });
                Remember(id, name, false);
            }
            Save();
            return found;
        }

        /// <summary>
        /// The game's Xbox 360 achievements (English text when the database has it), revoked ones left out. Empty when
        /// the database lists none.
        /// </summary>
        public static async Task<List<OnlineAchievement>> AchievementsAsync(uint titleId, CancellationToken ct)
        {
            var list = new List<OnlineAchievement>();
            var a = await GetJsonAsync("achievements/v1/" + titleId.ToString("X8"), 8 * 1024 * 1024, ct).ConfigureAwait(false) as JArray;
            if (a == null) return list;
            var seen = new HashSet<uint>();
            foreach (var e in a.OfType<JObject>())
            {
                if ((bool?)e["is_revoked"] == true) continue;
                if (!string.Equals((string)e["title_id"], titleId.ToString("X8"), StringComparison.OrdinalIgnoreCase)) continue;
                long? id = (long?)e["achievement_id"], gs = (long?)e["gamerscore"], flags = (long?)e["flags"];
                if (id == null || id < 0 || id > 0xFFFF || gs == null || gs < 0 || gs > 1000 || flags == null || flags < 0 || flags > uint.MaxValue) continue;
                if (!seen.Add((uint)id.Value)) continue;
                var locs = (e["localizations"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                var text = locs.FirstOrDefault(l => (string)l["locale"]?["locale"] == "en-US") ?? locs.FirstOrDefault(l => ((string)l["locale"]?["locale"] ?? "").StartsWith("en-")) ?? locs.FirstOrDefault();
                list.Add(new OnlineAchievement
                {
                    Id = (uint)id.Value, Gamerscore = (uint)gs.Value, Flags = (uint)flags.Value,
                    Name = Clean((string)text?["name"], 127), Description = Clean((string)text?["description"], 400), LockedDescription = Clean((string)text?["locked_description"], 400)
                });
                if (list.Count > 1000) break;
            }
            return list.OrderBy(x => x.Id).ToList();
        }

        private static async Task<JToken> GetJsonAsync(string path, int limit, CancellationToken ct)
        {
            string url = (TestApi ?? Api) + path;
            var uri = new Uri(url);
            if (TestApi == null ? uri.Scheme != Uri.UriSchemeHttps || uri.Host != "dbox.tools" : !uri.IsLoopback) throw new InvalidOperationException("Unexpected address.");
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                using (var r = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                    if (!r.IsSuccessStatusCode) throw new IOException("The title database answered " + (int)r.StatusCode + ".");
                    using (var s = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var m = new MemoryStream())
                    {
                        var buf = new byte[16384];
                        int n;
                        while ((n = await s.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false)) > 0)
                        {
                            if (m.Length + n > limit) throw new IOException("The title database's answer is larger than expected.");
                            m.Write(buf, 0, n);
                        }
                        try { return JToken.Parse(Encoding.UTF8.GetString(m.ToArray())); }
                        catch (Newtonsoft.Json.JsonException) { throw new IOException("The title database's answer could not be read."); }
                    }
                }
            }
        }

        /// <summary>One line of plain text: control characters removed, spaces collapsed, at most <paramref name="max"/> characters.</summary>
        private static string Clean(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(Math.Min(s.Length, max));
            bool space = false;
            foreach (char c in s)
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
                if (sb.Length >= max) break;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the names kept on this PC

        private static void Load()
        {
            if (_names != null) return;
            _names = new Dictionary<uint, string>();
            try
            {
                if (!File.Exists(CacheFile) || new FileInfo(CacheFile).Length > 8 * 1024 * 1024) return;
                if (!(JToken.Parse(File.ReadAllText(CacheFile, Encoding.UTF8)) is JObject o)) return;
                foreach (var p in o.Properties())
                {
                    string name = Clean((string)p.Value, 127);
                    if (TryParseTitleId(p.Name, out uint id) && name.Length > 0) _names[id] = name;
                }
            }
            catch (Exception) { /* a damaged cache is simply rebuilt */ }
        }

        public static void Remember(uint titleId, string name, bool save = true)
        {
            name = Clean(name, 127);
            if (name.Length == 0 || titleId == 0) return;
            lock (Gate)
            {
                Load();
                if (_names.TryGetValue(titleId, out string old) && old == name) return;
                _names[titleId] = name;
            }
            if (save) Save();
        }

        private static void Save()
        {
            try
            {
                JObject o;
                lock (Gate) { Load(); o = new JObject(_names.OrderBy(k => k.Key).Select(k => new JProperty(k.Key.ToString("X8"), k.Value))); }
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
                string tmp = CacheFile + ".tmp";
                File.WriteAllText(tmp, o.ToString(), new UTF8Encoding(false));
                if (File.Exists(CacheFile)) File.Replace(tmp, CacheFile, null); else File.Move(tmp, CacheFile);
            }
            catch (Exception) { /* only a convenience */ }
        }
    }
}
