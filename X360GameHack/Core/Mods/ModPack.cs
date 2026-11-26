using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace X360GameHack.Core.Mods
{
    /// <summary>The part of a mod file that is meant to be read by anyone: what it is, not what it does.</summary>
    public sealed class ModInfo
    {
        public string Id = "";              // stable id the server knows this mod by
        public string Name = "";
        public string Game = "";
        public string TitleId = "";         // 8 hex, for a game/save mod
        public string Kind = "save";        // "save" or "game"
        public string Version = "1";
        public string Requires = "";        // a human note, e.g. "Black Ops II, any title update"
        public string Description = "";
        /// <summary>True when the mod's data is NOT in the file at all and only ever comes from the server.</summary>
        public bool ServerOnly;
        /// <summary>True for a free/open mod: the payload is in the file in the clear, no token or server needed.</summary>
        public bool Open;
    }

    /// <summary>
    /// A ".x360mod" file: clear metadata (so the program can list it and show what it is) followed by the mod's
    /// payload, sealed with AES + HMAC. The key to open the payload is NOT in the file and NOT in this program -
    /// it is given out by the licence server after the buyer's token and this machine's id are checked. A
    /// server-only mod carries no payload at all; its data is delivered by the server at use time.
    /// </summary>
    public sealed class ModPack
    {
        private const string Magic = "X360MOD1";

        public ModInfo Info { get; private set; }
        /// <summary>For a sealed mod, iv+mac+ciphertext; for an open mod, the plaintext payload; null for a server-only mod.</summary>
        public byte[] Sealed { get; private set; }
        public string SourcePath { get; private set; }
        /// <summary>A free mod whose payload is readable in the file and needs no token.</summary>
        public bool IsOpen => Info != null && Info.Open;
        /// <summary>True when the file carries a valid signature by the X360GameHack owner's key (see <see cref="ModSignature"/>).</summary>
        public bool OwnerSigned { get; private set; }

        public static ModPack Parse(byte[] bytes, string sourcePath = null)
        {
            if (bytes == null || bytes.Length < 11) throw new InvalidDataException("Not a mod file.");
            if (Encoding.ASCII.GetString(bytes, 0, 8) != Magic) throw new InvalidDataException("Not an X360GameHack mod file.");
            int at = 8;
            byte flags = bytes[at++];
            int metaLen = bytes[at] | bytes[at + 1] << 8; at += 2;
            if (at + metaLen > bytes.Length) throw new InvalidDataException("The mod file is damaged (metadata).");
            var info = JsonConvert.DeserializeObject<ModInfo>(Encoding.UTF8.GetString(bytes, at, metaLen)) ?? throw new InvalidDataException("The mod file has no metadata.");
            info.ServerOnly = (flags & 1) != 0;
            info.Open = (flags & 2) != 0;
            at += metaLen;
            byte[] sealed_ = null;
            if (!info.ServerOnly)
            {
                if (at + 4 > bytes.Length) throw new InvalidDataException("The mod file is damaged (payload length).");
                int len = bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16 | bytes[at + 3] << 24; at += 4;
                if (len < 0 || at + len > bytes.Length) throw new InvalidDataException("The mod file is damaged (payload).");
                sealed_ = new byte[len]; Buffer.BlockCopy(bytes, at, sealed_, 0, len);
            }
            return new ModPack { Info = info, Sealed = sealed_, SourcePath = sourcePath, OwnerSigned = ModSignature.ByOwner(bytes) };
        }

        public static ModPack Load(string path) => Parse(File.ReadAllBytes(path), path);

        /// <summary>
        /// Builds a mod file. This is the maker's side (used by the private packing tool, not by the program in
        /// normal use): the payload is sealed with a fresh random key that only the licence server is told.
        /// </summary>
        public static byte[] Build(ModInfo info, byte[] payload, byte[] keyAes, byte[] keyMac)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(Encoding.ASCII.GetBytes(Magic), 0, 8);
                ms.WriteByte((byte)((info.ServerOnly ? 1 : 0) | (info.Open ? 2 : 0)));
                byte[] meta = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(info));
                ms.WriteByte((byte)(meta.Length & 0xFF)); ms.WriteByte((byte)(meta.Length >> 8));
                ms.Write(meta, 0, meta.Length);
                if (!info.ServerOnly)
                {
                    byte[] body = info.Open ? payload : CryptoBox.Seal(keyAes, keyMac, payload);   // open = clear, else sealed
                    ms.Write(BitConverter.GetBytes(body.Length), 0, 4);
                    ms.Write(body, 0, body.Length);
                }
                return ms.ToArray();
            }
        }
    }
}
