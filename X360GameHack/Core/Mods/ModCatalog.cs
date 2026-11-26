using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Mods
{
    public sealed class CatalogOption { public string Name = "", Does = ""; }
    public sealed class CatalogGroup { public string Name = ""; public List<CatalogOption> Options = new List<CatalogOption>(); }

    /// <summary>One mod in the public catalog: what it is and what its options do - never its data, offsets or key.</summary>
    public sealed class CatalogMod
    {
        public string ModId = "", Name = "", Game = "", TitleId = "", Kind = "", Version = "", Description = "", Requires = "";
        public bool Free;
        public List<CatalogGroup> Groups = new List<CatalogGroup>();
        public int Options => Groups.Sum(g => g.Options.Count);
    }

    /// <summary>The catalog: every mod a licence unlocks, and where to get a licence.</summary>
    public sealed class ModCatalogList
    {
        public bool Ok;
        public string Error;
        public string Shop = "";
        public List<CatalogMod> Mods = new List<CatalogMod>();
        /// <summary>When the list was read from the server (for a saved copy, when it was saved).</summary>
        public DateTime Fetched;
        /// <summary>True when the server could not be reached and the copy saved last time is shown.</summary>
        public bool FromCache;

        private static readonly Regex ModId = new Regex(@"^[A-Za-z0-9._-]{1,64}$");
        private static string Cap(JToken t, int max) { string s = (t != null && t.Type == JTokenType.String ? (string)t : "") ?? ""; s = s.Trim(); return s.Length > max ? s.Substring(0, max) : s; }

        /// <summary>Reads the server's answer; every field is checked and capped, odd entries are left out.</summary>
        public static ModCatalogList FromJson(string text)
        {
            var list = new ModCatalogList { Fetched = DateTime.Now };
            JObject j;
            try { j = JObject.Parse(text ?? ""); }
            catch (Exception) { list.Error = "The mod list is not readable."; return list; }
            if (j["ok"] == null || j["ok"].Type != JTokenType.Boolean || !(bool)j["ok"]) { list.Error = Cap(j["error"], 200).Length > 0 ? Cap(j["error"], 200) : "The mod server did not send its list."; return list; }
            string shop = Cap(j["shop"], 300);
            if (Uri.TryCreate(shop, UriKind.Absolute, out Uri su) && su.Scheme == Uri.UriSchemeHttps) list.Shop = su.ToString();   // https links only
            if (j["mods"] is JArray mods)
                foreach (var m in mods.OfType<JObject>().Take(500))
                {
                    var c = new CatalogMod
                    {
                        ModId = Cap(m["modId"], 64), Name = Cap(m["name"], 100), Game = Cap(m["game"], 100), TitleId = Cap(m["titleId"], 8), Kind = Cap(m["kind"], 10),
                        Version = Cap(m["version"], 20), Description = Cap(m["description"], 500), Requires = Cap(m["requires"], 200),
                        Free = m["free"] != null && m["free"].Type == JTokenType.Boolean && (bool)m["free"],
                    };
                    if (!ModId.IsMatch(c.ModId) || c.Name.Length == 0) continue;
                    if (m["groups"] is JArray groups)
                        foreach (var g in groups.OfType<JObject>().Take(100))
                        {
                            var cg = new CatalogGroup { Name = Cap(g["name"], 80) };
                            if (g["options"] is JArray opts)
                                foreach (var o in opts.OfType<JObject>().Take(500))
                                {
                                    var co = new CatalogOption { Name = Cap(o["name"], 80), Does = Cap(o["does"], 300) };
                                    if (co.Name.Length > 0) cg.Options.Add(co);
                                }
                            if (cg.Options.Count > 0) c.Groups.Add(cg);
                        }
                    list.Mods.Add(c);
                }
            list.Ok = true;
            return list;
        }
    }

    /// <summary>
    /// The catalog as the program shows it: read from the mod server, and saved on this PC so the list still shows when
    /// the server cannot be reached. Only the public list is saved - names and descriptions, nothing of any mod.
    /// </summary>
    public static class ModCatalog
    {
        /// <summary>For automated tests only: stands in for the saved copy's file. Null in normal use.</summary>
        internal static string TestCacheFile;

        public static string CacheFile => TestCacheFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "X360GameHack", "mod-catalog.json");

        public static ModCatalogList Last { get; private set; }

        /// <summary>Reads the catalog from the mod server; when that fails, the copy saved last time (marked as such).</summary>
        public static ModCatalogList Load()
        {
            var fresh = ActivationClient.GetCatalog(ModManager.ServerUrl);
            if (fresh.Ok)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
                    var saved = new JObject
                    {
                        ["ok"] = true, ["shop"] = fresh.Shop, ["saved"] = DateTime.Now.ToString("o"),
                        ["mods"] = JArray.FromObject(fresh.Mods.Select(m => new
                        {
                            modId = m.ModId, name = m.Name, game = m.Game, titleId = m.TitleId, kind = m.Kind, version = m.Version, description = m.Description,
                            requires = m.Requires, free = m.Free, groups = m.Groups.Select(g => new { name = g.Name, options = g.Options.Select(o => new { name = o.Name, does = o.Does }) })
                        })),
                    };
                    File.WriteAllText(CacheFile, saved.ToString());
                }
                catch (Exception ex) { Log.Warn("Could not save the mod list: " + ex.Message); }
                return Last = fresh;
            }
            try
            {
                if (File.Exists(CacheFile))
                {
                    string text = File.ReadAllText(CacheFile);
                    var old = ModCatalogList.FromJson(text);
                    if (old.Ok)
                    {
                        old.FromCache = true;
                        old.Error = fresh.Error;
                        try { old.Fetched = DateTime.Parse((string)JObject.Parse(text)["saved"] ?? "", null, System.Globalization.DateTimeStyles.RoundtripKind); } catch (Exception) { }
                        return Last = old;
                    }
                }
            }
            catch (Exception) { }
            return Last = fresh;
        }
    }
}
