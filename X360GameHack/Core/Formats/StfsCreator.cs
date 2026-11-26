using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>What a new CON package says about itself. Layout checked against console-written saves and profiles.</summary>
    public sealed class StfsCreateOptions
    {
        public uint ContentType = 1;            // 1 = saved game
        public uint TitleId, MediaId, Version, BaseVersion, SaveGameId;
        public byte Platform, ExecutableType, DiscNumber, DiscCount;
        public byte TransferFlags = 0x40;       // what every console-written save and profile examined carries
        public string DisplayName = "", Description = "", Publisher = "", TitleName = "";
        /// <summary>Console ID (5 bytes), profile ID (8), device ID (20) and creator XUID (8); null = zeros.</summary>
        public byte[] ConsoleId, ProfileId, DeviceId, CreatorXuid;
        public byte[] ThumbnailPng, TitleThumbnailPng;

        /// <summary>The same details as an existing package (a save of the same game, for instance).</summary>
        public static StfsCreateOptions From(string packagePath)
        {
            byte[] h = BE.ReadHead(packagePath, 0x971A);
            if (h.Length < 0x971A) throw new InvalidDataException("That file is too small to be a package.");
            string magic = Encoding.ASCII.GetString(h, 0, 4);
            if (magic != "CON " && magic != "LIVE" && magic != "PIRS") throw new InvalidDataException("That file is not an Xbox 360 package.");
            var p = StfsPackage.FromHeader(h, packagePath);
            Func<int, int, byte[]> take = (o, n) => { var b = new byte[n]; Buffer.BlockCopy(h, o, b, 0, n); return b; };
            uint thumb = BE.U32(h, 0x1712), titleThumb = BE.U32(h, 0x1716);
            return new StfsCreateOptions
            {
                ContentType = BE.U32(h, 0x344), MediaId = BE.U32(h, 0x354), Version = BE.U32(h, 0x358), BaseVersion = BE.U32(h, 0x35C), TitleId = BE.U32(h, 0x360),
                Platform = h[0x364], ExecutableType = h[0x365], DiscNumber = h[0x366], DiscCount = h[0x367], SaveGameId = BE.U32(h, 0x368),
                ConsoleId = take(0x36C, 5), ProfileId = take(0x371, 8), CreatorXuid = take(0x3AD, 8), DeviceId = take(0x3FD, 20),
                DisplayName = p.DisplayName, Description = p.Description, Publisher = p.Publisher, TitleName = p.TitleName, TransferFlags = h[0x1711],
                ThumbnailPng = thumb > 0 && thumb <= 0x4000 ? take(0x171A, (int)thumb) : null,
                TitleThumbnailPng = titleThumb > 0 && titleThumb <= 0x4000 ? take(0x571A, (int)titleThumb) : null
            };
        }
    }

    /// <summary>
    /// Builds a new CON package (a save, for instance) from files. The header is written field by field in the layout
    /// console-written packages use; the volume starts empty and the files go in through the same verified engine
    /// that edits existing packages. The result has valid hashes and no console signature - Rehash &amp; resign signs
    /// it when a keyvault is loaded.
    /// </summary>
    public static class StfsCreator
    {
        private const int HeaderSize = 0x971A, Base = 0xA000, Block = 0x1000;

        /// <summary>The 0xA000-byte header for an empty volume (hash fields filled in by <see cref="Create"/>).</summary>
        public static byte[] BuildHeader(StfsCreateOptions o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            Check(o.ConsoleId, 5, "console ID"); Check(o.ProfileId, 8, "profile ID"); Check(o.DeviceId, 20, "device ID"); Check(o.CreatorXuid, 8, "creator XUID");
            if ((o.DisplayName ?? "").Length > 127 || (o.Description ?? "").Length > 127) throw new ArgumentException("The display name and the description are at most 127 characters.");
            if ((o.Publisher ?? "").Length > 63 || (o.TitleName ?? "").Length > 63) throw new ArgumentException("The publisher and the game name are at most 63 characters.");
            foreach (var png in new[] { o.ThumbnailPng, o.TitleThumbnailPng })
                if (png != null && (png.Length > StfsEditor.MaxPictureBytes || png.Length < 8 || png[0] != 0x89 || png[1] != 0x50)) throw new ArgumentException("Package pictures are PNG files of at most " + StfsEditor.MaxPictureBytes.ToString("N0") + " bytes.");

            var h = new byte[Base];
            Encoding.ASCII.GetBytes("CON ").CopyTo(h, 0);
            for (int i = 0; i < 8; i++) h[0x22C + i] = 0xFF;                 // first licence: unrestricted
            BE.W32(h, 0x340, HeaderSize);
            BE.W32(h, 0x344, o.ContentType);
            BE.W32(h, 0x348, 2);                                            // metadata version
            BE.W32(h, 0x354, o.MediaId);
            BE.W32(h, 0x358, o.Version);
            BE.W32(h, 0x35C, o.BaseVersion);
            BE.W32(h, 0x360, o.TitleId);
            h[0x364] = o.Platform; h[0x365] = o.ExecutableType; h[0x366] = o.DiscNumber; h[0x367] = o.DiscCount;
            BE.W32(h, 0x368, o.SaveGameId);
            if (o.ConsoleId != null) Buffer.BlockCopy(o.ConsoleId, 0, h, 0x36C, 5);
            if (o.ProfileId != null) Buffer.BlockCopy(o.ProfileId, 0, h, 0x371, 8);
            h[0x379] = 0x24;                                                // STFS volume descriptor: two table copies, file table = 1 block at block 0
            h[0x37C] = 1;
            BE.W32(h, 0x395, 1);                                            // one block allocated (the file table)
            if (o.CreatorXuid != null) Buffer.BlockCopy(o.CreatorXuid, 0, h, 0x3AD, 8);
            if (o.DeviceId != null) Buffer.BlockCopy(o.DeviceId, 0, h, 0x3FD, 20);
            BE.WriteUtf16BE(h, 0x411, 0x100, o.DisplayName ?? "");
            BE.WriteUtf16BE(h, 0xD11, 0x100, o.Description ?? "");
            BE.WriteUtf16BE(h, 0x1611, 0x80, o.Publisher ?? "");
            BE.WriteUtf16BE(h, 0x1691, 0x80, o.TitleName ?? "");
            h[0x1711] = o.TransferFlags;
            if (o.ThumbnailPng != null) { BE.W32(h, 0x1712, (uint)o.ThumbnailPng.Length); Buffer.BlockCopy(o.ThumbnailPng, 0, h, 0x171A, o.ThumbnailPng.Length); }
            if (o.TitleThumbnailPng != null) { BE.W32(h, 0x1716, (uint)o.TitleThumbnailPng.Length); Buffer.BlockCopy(o.TitleThumbnailPng, 0, h, 0x571A, o.TitleThumbnailPng.Length); }
            return h;
        }

        private static void Check(byte[] b, int length, string what)
        {
            if (b != null && b.Length != length) throw new ArgumentException("The " + what + " must be " + length + " bytes.");
        }

        /// <summary>
        /// Writes a new package to <paramref name="outputPath"/> (which must not exist) holding <paramref name="files"/>
        /// (name in the package -> content; names are 1-40 ASCII characters). Verified completely before it is kept.
        /// </summary>
        public static void Create(string outputPath, StfsCreateOptions options, IDictionary<string, byte[]> files)
        {
            if (File.Exists(outputPath)) throw new IOException("That file already exists - choose a new name.");
            byte[] head = BuildHeader(options);
            var table = new byte[Block];                                   // hash table: one entry, for the (empty) file table block
            byte[] fileTable = new byte[Block];
            using (var sha = SHA1.Create())
            {
                Buffer.BlockCopy(sha.ComputeHash(fileTable), 0, table, 0, 20);
                table[0x14] = 0x80; table[0x15] = 0xFF; table[0x16] = 0xFF; table[0x17] = 0xFF;   // in use, end of chain
                Buffer.BlockCopy(sha.ComputeHash(table), 0, head, 0x381, 20);
                BE.W64(head, 0x34C, 3 * Block);                             // content size: everything after the header
                Buffer.BlockCopy(sha.ComputeHash(head, 0x344, head.Length - 0x344), 0, head, 0x32C, 20);
            }
            string tmp = outputPath + ".creating";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(head, 0, head.Length);
                    fs.Write(table, 0, Block);                              // both copies of the table
                    fs.Write(table, 0, Block);
                    fs.Write(fileTable, 0, Block);
                    fs.Flush(true);
                }
                if (files != null && files.Count > 0) StfsEditor.Update(tmp, null, files);
                using (var v = StfsVolume.Open(tmp))
                {
                    var (_, bad) = v.VerifyAll();
                    if (bad > 0 || !v.VerifyTree(out string why)) throw new InvalidDataException("The new package did not verify - nothing was created.");
                    int count = v.Entries.Count(e => !e.IsDirectory);
                    if (count != (files?.Count ?? 0)) throw new InvalidDataException("The new package does not list every file - nothing was created.");
                }
                File.Move(tmp, outputPath);
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }
    }
}
