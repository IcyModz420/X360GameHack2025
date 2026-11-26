using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using X360GameHack.Core.Formats;

namespace X360GameHack.Core.Mods
{
    // The shape of a decrypted save-mod payload. Only ever built from bytes held in memory, never from disk.
    internal sealed class SaveModPayload
    {
        public string kind = "save";
        public string game = "";
        public string what = "";
        public string innerFile = "";
        public string titleId = "";
        public string ddl = "";
        public List<SaveModAction> actions = new List<SaveModAction>();
    }

    internal sealed class SaveModAction
    {
        public string name = "";
        public string does = "";
        public List<SaveModOp> ops = new List<SaveModOp>();
    }

    internal sealed class SaveModOp
    {
        public string contains, startsWith, endsWith, equals;
        public ulong value;
    }

    /// <summary>
    /// Turns a decrypted save-mod payload into the same <see cref="GameSaveKind"/> the built-in editors use, so a
    /// paid save mod appears in the Saves editor exactly like a free one. The offsets live only in the DDL text
    /// that came from the server; they are never written to disk by this program.
    /// </summary>
    public static class SaveModAdapter
    {
        public static GameSaveKind Build(byte[] payload)
        {
            var p = JsonConvert.DeserializeObject<SaveModPayload>(System.Text.Encoding.UTF8.GetString(payload))
                    ?? throw new InvalidOperationException("The mod data is not readable.");
            if (!string.Equals(p.kind, "save", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("That mod is not a save-game mod.");
            uint titleId = uint.Parse(p.titleId, System.Globalization.NumberStyles.HexNumber);
            var def = DdlDef.Parse(p.ddl);
            var kind = new GameSaveKind { Game = p.game, What = p.what, InnerFile = p.innerFile, TitleId = titleId, Def = def };
            foreach (var a in p.actions ?? new List<SaveModAction>())
            {
                var ops = a.ops ?? new List<SaveModOp>();
                kind.Actions.Add(new GameSaveAction
                {
                    Name = a.name,
                    Does = a.does,
                    Apply = s =>
                    {
                        int changed = 0;
                        foreach (var op in ops)
                            foreach (var f in s.Fields.Where(f => Match(f, op)))
                            {
                                ulong want = Math.Min(op.value, f.Max);
                                if (s.Get(f) != want) { s.Set(f, want); changed++; }
                            }
                        return changed;
                    }
                });
            }
            return kind;
        }

        private static bool Match(DdlField f, SaveModOp op)
        {
            if (f.IsText) return false;
            if (op.equals != null) return f.Path.Equals(op.equals, StringComparison.OrdinalIgnoreCase);
            if (op.contains != null && f.Path.IndexOf(op.contains, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (op.startsWith != null && !f.Path.StartsWith(op.startsWith, StringComparison.OrdinalIgnoreCase)) return false;
            if (op.endsWith != null && !f.Path.EndsWith(op.endsWith, StringComparison.OrdinalIgnoreCase)) return false;
            return op.contains != null || op.startsWith != null || op.endsWith != null;
        }
    }
}
