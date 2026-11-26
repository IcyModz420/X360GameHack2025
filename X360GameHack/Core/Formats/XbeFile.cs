using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>Original Xbox XBE header reader and patcher (little-endian x86 format).</summary>
    public sealed class XbeFile
    {
        private const uint RetailEntryKey = 0xA8FC57AB;
        private const uint DebugEntryKey = 0x94859D4B;
        private const int InitFlagsOffset = 0x124;
        private const uint Limit64MbFlag = 0x00000004;

        public string Path { get; private set; }
        public bool Valid { get; private set; }
        public uint BaseAddress { get; private set; }
        public uint ImageSize { get; private set; }
        public DateTime TimeDate { get; private set; }
        public uint InitFlags { get; private set; }
        public uint EntryPoint { get; private set; }
        public bool IsDebugBuild { get; private set; }
        public int SectionCount { get; private set; }
        public uint TitleId { get; private set; }
        public string TitleName { get; private set; } = "";
        public uint AllowedMedia { get; private set; }
        public uint GameRegion { get; private set; }
        public uint GameRatings { get; private set; }
        public uint DiscNumber { get; private set; }
        public uint CertVersion { get; private set; }
        public long CertificateOffset { get; private set; } = -1;

        public bool Limit64Mb => (InitFlags & Limit64MbFlag) != 0;

        public string TitleIdString
        {
            get
            {
                // XBE title IDs are a 2-char publisher code + game number, e.g. MS-004C
                char a = (char)((TitleId >> 24) & 0xFF), b = (char)((TitleId >> 16) & 0xFF);
                string code = char.IsLetterOrDigit(a) && char.IsLetterOrDigit(b) ? "" + a + b : "??";
                return $"{TitleId:X8}  ({code}-{TitleId & 0xFFFF:D3})";
            }
        }

        public string RegionString
        {
            get
            {
                var r = new List<string>();
                if ((GameRegion & 0x1) != 0) r.Add("North America");
                if ((GameRegion & 0x2) != 0) r.Add("Japan");
                if ((GameRegion & 0x4) != 0) r.Add("Rest of World");
                if ((GameRegion & 0x80000000) != 0) r.Add("Manufacturing");
                return r.Count == 0 ? "None" : string.Join(", ", r);
            }
        }

        public string MediaString
        {
            get
            {
                var r = new List<string>();
                if ((AllowedMedia & 0x1) != 0) r.Add("HDD");
                if ((AllowedMedia & 0x2) != 0) r.Add("DVD X2");
                if ((AllowedMedia & 0x4) != 0) r.Add("DVD/CD");
                if ((AllowedMedia & 0x8) != 0) r.Add("CD");
                if ((AllowedMedia & 0x10) != 0) r.Add("DVD-5 RO");
                if ((AllowedMedia & 0x20) != 0) r.Add("DVD-9 RO");
                if ((AllowedMedia & 0x40) != 0) r.Add("DVD-5 RW");
                if ((AllowedMedia & 0x80) != 0) r.Add("DVD-9 RW");
                if ((AllowedMedia & 0x100) != 0) r.Add("Dongle");
                if ((AllowedMedia & 0x200) != 0) r.Add("Media Board");
                if ((AllowedMedia & 0x40000000) != 0) r.Add("Nonsecure HDD");
                if ((AllowedMedia & 0x80000000) != 0) r.Add("Nonsecure Mode");
                return r.Count == 0 ? "None" : string.Join(", ", r);
            }
        }

        public static XbeFile Read(string path) => FromHead(BE.ReadHead(path, 0x10000), path);

        /// <summary>The header from the first bytes of an XBE (64 KB holds the certificate of every real one) - e.g. read off an Xbox drive.</summary>
        public static XbeFile FromHead(byte[] h, string path = null)
        {
            var x = new XbeFile { Path = path };
            if (h == null || h.Length < 0x178 || Encoding.ASCII.GetString(h, 0, 4) != "XBEH") return x;
            x.BaseAddress = BE.LE32(h, 0x104);
            x.ImageSize = BE.LE32(h, 0x10C);
            x.TimeDate = DateTimeOffset.FromUnixTimeSeconds(BE.LE32(h, 0x114)).UtcDateTime;
            uint certAddr = BE.LE32(h, 0x118);
            x.SectionCount = (int)BE.LE32(h, 0x11C);
            x.InitFlags = BE.LE32(h, InitFlagsOffset);
            uint enc = BE.LE32(h, 0x128);
            uint retail = enc ^ RetailEntryKey;
            if (retail >= x.BaseAddress && retail < x.BaseAddress + x.ImageSize) x.EntryPoint = retail;
            else { x.EntryPoint = enc ^ DebugEntryKey; x.IsDebugBuild = true; }

            long cert = (long)certAddr - x.BaseAddress;
            if (cert > 0 && cert + 0xB0 <= h.Length)
            {
                x.CertificateOffset = cert;
                int c = (int)cert;
                x.TitleId = BE.LE32(h, c + 0x08);
                x.TitleName = BE.Utf16LE(h, c + 0x0C, 80);
                x.AllowedMedia = BE.LE32(h, c + 0x9C);
                x.GameRegion = BE.LE32(h, c + 0xA0);
                x.GameRatings = BE.LE32(h, c + 0xA4);
                x.DiscNumber = BE.LE32(h, c + 0xA8);
                x.CertVersion = BE.LE32(h, c + 0xAC);
            }
            x.Valid = true;
            return x;
        }

        private static void WriteLE32(string path, long offset, uint value)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                fs.Position = offset;
                fs.Write(BitConverter.GetBytes(value), 0, 4);
            }
        }

        public static void SetTitleId(string path, uint titleId)
        {
            var x = Read(path);
            if (!x.Valid || x.CertificateOffset < 0) throw new InvalidOperationException("Not a valid XBE (no certificate).");
            WriteLE32(path, x.CertificateOffset + 0x08, titleId);
        }

        public static void SetRegion(string path, uint region)
        {
            var x = Read(path);
            if (!x.Valid || x.CertificateOffset < 0) throw new InvalidOperationException("Not a valid XBE (no certificate).");
            WriteLE32(path, x.CertificateOffset + 0xA0, region);
        }

        public static void SetAllowedMedia(string path, uint media)
        {
            var x = Read(path);
            if (!x.Valid || x.CertificateOffset < 0) throw new InvalidOperationException("Not a valid XBE (no certificate).");
            WriteLE32(path, x.CertificateOffset + 0x9C, media);
        }

        /// <summary>true = limit to 64MB (stock), false = allow 128MB on upgraded consoles.</summary>
        public static void SetRamLimit(string path, bool limit64)
        {
            var x = Read(path);
            if (!x.Valid) throw new InvalidOperationException("Not a valid XBE.");
            uint flags = limit64 ? (x.InitFlags | Limit64MbFlag) : (x.InitFlags & ~Limit64MbFlag);
            WriteLE32(path, InitFlagsOffset, flags);
        }

        /// <summary>
        /// Replaces the stock 733.33 MHz CPU constant used by the game's timing code.
        /// Returns the number of locations patched (0 = constant not found, e.g. already patched).
        /// </summary>
        public static int PatchCpuSpeed(string path, int mhz, int fromMhzConstant = 733333333)
        {
            byte[] data = File.ReadAllBytes(path);
            byte[] find = BitConverter.GetBytes(fromMhzConstant);
            byte[] repl = BitConverter.GetBytes(mhz == 733 ? 733333333 : mhz * 1000000);
            int hits = 0;
            for (int i = 0; i <= data.Length - 4; i++)
            {
                if (data[i] == find[0] && data[i + 1] == find[1] && data[i + 2] == find[2] && data[i + 3] == find[3])
                {
                    Buffer.BlockCopy(repl, 0, data, i, 4);
                    hits++;
                    i += 3;
                }
            }
            if (hits > 0) File.WriteAllBytes(path, data);
            return hits;
        }

        /// <summary>The stock 733.33 MHz CPU clock constant games time themselves with.</summary>
        public const int StockCpuConstant = 733333333;

        /// <summary>
        /// Clock values this program writes that are unusual enough to be found again safely (1400 and 1480 MHz).
        /// 1000 MHz is NOT one of them: 1,000,000,000 is a common number in games (nanoseconds per second), and
        /// "patching" those would break the game.
        /// </summary>
        public static readonly int[] RefindableCpuConstants = { 1400000000, 1480000000 };

        /// <summary>
        /// The CPU clock constant in the file that can be patched: the stock 733.33 MHz value, or a 1400 / 1480 MHz value
        /// written by this program. 0 when there is none (the game has no such constant, or it was set to a value that
        /// cannot be told apart from ordinary numbers - restore the original to change it again).
        /// </summary>
        public static int DetectCpuSpeed(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            foreach (int k in new[] { StockCpuConstant }.Concat(RefindableCpuConstants))
            {
                byte[] f = BitConverter.GetBytes(k);
                for (int i = 0; i <= data.Length - 4; i++)
                    if (data[i] == f[0] && data[i + 1] == f[1] && data[i + 2] == f[2] && data[i + 3] == f[3])
                        return k;
            }
            return 0;
        }

        /// <summary>True when a clock patched to <paramref name="mhz"/> can be found again (and so changed again) later.</summary>
        public static bool CanRefind(int mhz) => mhz == 733 || RefindableCpuConstants.Contains(mhz * 1000000);
    }
}
