using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace X360GameHack.Core.Formats
{
    public enum XboxConsoleKind { Retail, Devkit }

    /// <summary>
    /// The encrypted "Account" file inside an Xbox 360 profile (gamertag, XUID, region, LIVE settings).
    /// Format and keys are the published, open-source scene constants (same as Velocity / Horizon):
    ///   file  = HMAC[0x10] + RC4( confounder[8] + payload[0x17C] )      (0x194 bytes total)
    ///   RC4 key = HMAC-SHA1(accountKey, HMAC)[0..0x10]
    ///   HMAC    = HMAC-SHA1(accountKey, confounder||payload)[0..0x10]
    /// Retail and devkit profiles use different account keys; <see cref="Load"/> tries both.
    /// All multi-byte fields are big-endian (console byte order).
    /// </summary>
    public sealed class ProfileAccount
    {
        public const int FileSize = 0x194;      // 404
        public const int PayloadSize = 0x17C;   // 380

        private static readonly byte[] RetailKey =
            { 0xE1, 0xBC, 0x15, 0x9C, 0x73, 0xB1, 0xEA, 0xE9, 0xAB, 0x31, 0x70, 0xF3, 0xAD, 0x47, 0xEB, 0xF3 };
        private static readonly byte[] DevkitKey =
            { 0xDA, 0xB6, 0x9A, 0xD9, 0x8E, 0x28, 0x76, 0x4F, 0x97, 0x7E, 0xE2, 0x48, 0x7E, 0x4F, 0x3F, 0x68 };
        private readonly byte[] _payload;      // decrypted 0x17C-byte account record
        private readonly byte[] _confounder;   // the 8 random bytes stored in front of it (kept, so an unchanged account re-encrypts to the same file)
        private readonly byte[] _tail;         // anything stored after the 0x194-byte record (kept as is)
        /// <summary>
        /// Which account key the file is encrypted with. Changing it re-encrypts the same record with the other key, so
        /// a profile made on a development kit can be read by a retail console and the other way round.
        /// </summary>
        public XboxConsoleKind Kind { get; set; }

        private ProfileAccount(byte[] payload, byte[] confounder, byte[] tail, XboxConsoleKind kind)
        {
            _payload = payload;
            _confounder = confounder;
            _tail = tail;
            Kind = kind;
        }

        private static byte[] KeyFor(XboxConsoleKind k) => k == XboxConsoleKind.Retail ? RetailKey : DevkitKey;

        /// <summary>
        /// A new account made the way Xenia's own "Create Profile" makes one (ProfileManager::CreateAccount and
        /// EncryptAccountFile in Xenia's source): an all-zero record holding only the gamertag, the confounder set to eight
        /// 0xFD bytes, encrypted with the retail key.
        /// </summary>
        public static ProfileAccount NewLikeXenia(string gamertag)
        {
            string problem = GamertagProblem(gamertag);
            if (problem != null) throw new ArgumentException(problem);
            var account = new ProfileAccount(new byte[PayloadSize], Enumerable.Repeat((byte)0xFD, 8).ToArray(), new byte[0], XboxConsoleKind.Retail);
            account.Gamertag = gamertag;
            return account;
        }

        // ------------------------------------------------------------------ load / save

        public static ProfileAccount Load(byte[] file)
        {
            if (file == null || file.Length < FileSize) throw new InvalidDataException("Account file is too small.");
            var tail = new byte[file.Length - FileSize];
            Buffer.BlockCopy(file, FileSize, tail, 0, tail.Length);
            foreach (var kind in new[] { XboxConsoleKind.Retail, XboxConsoleKind.Devkit })
                if (TryDecrypt(file, kind, out var payload, out var confounder)) return new ProfileAccount(payload, confounder, tail, kind);
            throw new InvalidDataException("Could not decrypt the Account file - the HMAC check failed for both the retail and devkit keys (file may be corrupt or console-bound).");
        }

        private static bool TryDecrypt(byte[] file, XboxConsoleKind kind, out byte[] payload, out byte[] confounder)
        {
            payload = confounder = null;
            byte[] key = KeyFor(kind);

            var storedHmac = new byte[0x10];
            Buffer.BlockCopy(file, 0, storedHmac, 0, 0x10);

            byte[] rc4Key;
            using (var h = new HMACSHA1(key)) rc4Key = h.ComputeHash(storedHmac);   // use first 0x10 bytes

            var dec = new byte[0x184];
            Buffer.BlockCopy(file, 0x10, dec, 0, 0x184);
            Rc4.Apply(rc4Key, 0x10, dec, 0, 0x184);

            byte[] check;
            using (var h = new HMACSHA1(key)) check = h.ComputeHash(dec);
            for (int i = 0; i < 0x10; i++) if (check[i] != storedHmac[i]) return false;

            confounder = new byte[8];
            Buffer.BlockCopy(dec, 0, confounder, 0, 8);
            payload = new byte[PayloadSize];
            Buffer.BlockCopy(dec, 8, payload, 0, PayloadSize);
            return true;
        }

        public byte[] ToBytes()
        {
            byte[] key = KeyFor(Kind);

            var buf = new byte[0x184];
            Buffer.BlockCopy(_confounder, 0, buf, 0, 8);
            Buffer.BlockCopy(_payload, 0, buf, 8, PayloadSize);

            byte[] full;
            using (var h = new HMACSHA1(key)) full = h.ComputeHash(buf);
            var storedHmac = new byte[0x10];
            Buffer.BlockCopy(full, 0, storedHmac, 0, 0x10);

            byte[] rc4Key;
            using (var h = new HMACSHA1(key)) rc4Key = h.ComputeHash(storedHmac);

            Rc4.Apply(rc4Key, 0x10, buf, 0, 0x184);

            var file = new byte[FileSize + _tail.Length];
            Buffer.BlockCopy(storedHmac, 0, file, 0, 0x10);
            Buffer.BlockCopy(buf, 0, file, 0x10, 0x184);
            Buffer.BlockCopy(_tail, 0, file, FileSize, _tail.Length);
            return file;
        }

        /// <summary>
        /// Null when <paramref name="tag"/> fits the account record, else what is wrong with it. The record holds 15
        /// UTF-16 characters plus a terminator; control characters are refused.
        /// </summary>
        public static string GamertagProblem(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Trim().Length == 0) return "Enter a gamertag.";
            if (tag.Length > 15) return "A gamertag is at most 15 characters.";
            foreach (char c in tag)
                if (char.IsControl(c)) return "A gamertag cannot contain control characters.";
            return null;
        }

        /// <summary>
        /// True when <paramref name="tag"/> follows Xbox LIVE's own rules: letters, digits and single spaces, starting
        /// with a letter and not ending in a space. Anything else still shows on a console offline.
        /// </summary>
        public static bool IsStandardGamertag(string tag)
        {
            if (GamertagProblem(tag) != null) return false;
            if (!((tag[0] >= 'A' && tag[0] <= 'Z') || (tag[0] >= 'a' && tag[0] <= 'z')) || tag[tag.Length - 1] == ' ') return false;
            for (int i = 0; i < tag.Length; i++)
            {
                char c = tag[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == ' ')) return false;
                if (c == ' ' && i > 0 && tag[i - 1] == ' ') return false;
            }
            return true;
        }

        /// <summary>A copy of the decrypted account record (for inspection / testing).</summary>
        public byte[] Payload => (byte[])_payload.Clone();

        // ------------------------------------------------------------------ fields (offsets within the payload)

        public uint ReservedFlags { get => BE.U32(_payload, 0x00); set => BE.W32(_payload, 0x00, value); }
        public uint LiveFlags { get => BE.U32(_payload, 0x04); set => BE.W32(_payload, 0x04, value); }

        public string Gamertag
        {
            get => BE.Utf16BE(_payload, 0x08, 0x20);
            set => BE.WriteUtf16BE(_payload, 0x08, 0x20, value);
        }

        public ulong Xuid { get => BE.U64(_payload, 0x28); set => BE.W64(_payload, 0x28, value); }
        public string XuidHex => Xuid.ToString("X16");

        public uint CachedUserFlags { get => BE.U32(_payload, 0x30); set => BE.W32(_payload, 0x30, value); }

        public string ServiceProvider
        {
            get { string s = Encoding.ASCII.GetString(_payload, 0x34, 4).TrimEnd('\0', ' '); return s.Length == 0 ? "" : s; }
        }

        public byte[] Passcode
        {
            get { var p = new byte[4]; Buffer.BlockCopy(_payload, 0x38, p, 0, 4); return p; }
        }

        private static readonly string[] PasscodeButtons = { "", "D-pad Up", "D-pad Down", "D-pad Left", "D-pad Right", "X", "Y", "Left Trigger", "Right Trigger", "Left Bumper", "Right Bumper" };

        /// <summary>The button a stored passcode byte stands for (1-10), or null for a value that is not a button.</summary>
        public static string PasscodeButton(byte code) => code >= 1 && code < PasscodeButtons.Length ? PasscodeButtons[code] : null;

        /// <summary>The sign-in passcode as its four button presses ("X, Y, Left Bumper, Right Bumper"), or "" when none is set.</summary>
        public string PasscodeText
        {
            get
            {
                if (!PasscodeEnabled) return "";
                var names = Passcode.Select(b => PasscodeButton(b) ?? "(unknown " + b + ")");
                return string.Join(", ", names);
            }
        }

        public string OnlineDomain => Encoding.ASCII.GetString(_payload, 0x3C, 20).TrimEnd('\0');
        public string KerberosRealm => Encoding.ASCII.GetString(_payload, 0x50, 24).TrimEnd('\0');

        public byte[] OnlineKey
        {
            get { var k = new byte[0x10]; Buffer.BlockCopy(_payload, 0x68, k, 0, 0x10); return k; }
        }

        public bool HasOnlineKey
        {
            get { foreach (byte b in OnlineKey) if (b != 0) return true; return false; }
        }

        // ------------------------------------------------------------------ decoded flags

        public bool PasscodeEnabled => (ReservedFlags & 0x10000000) != 0;

        /// <summary>The "set up for Xbox LIVE" flag. Clearing it makes the console treat the profile as an offline one.</summary>
        public bool LiveEnabled
        {
            get => (ReservedFlags & 0x20000000) != 0;
            set => ReservedFlags = value ? ReservedFlags | 0x20000000 : ReservedFlags & ~0x20000000u;
        }

        /// <summary>Removes the sign-in passcode: clears the flag and the four stored button presses.</summary>
        public void RemovePasscode()
        {
            ReservedFlags &= ~0x10000000u;
            Array.Clear(_payload, 0x38, 4);
        }
        public bool Recovering => (ReservedFlags & 0x40000000) != 0;

        public bool PaymentIsCreditCard => (CachedUserFlags & 0x1) != 0;
        public bool ParentalControls => (CachedUserFlags & 0x01000000) != 0;

        // Packed into the cached user flags. Checked against a real Xbox LIVE profile: 0x0230676C = English (1),
        // Silver (3), United States (0x67).
        public int CountryId => (int)((CachedUserFlags >> 8) & 0xFF);
        public int SubscriptionTierId => (int)((CachedUserFlags >> 20) & 0xF);
        public int LanguageId => (int)((CachedUserFlags >> 25) & 0x1F);

        public string Country => XboxLive.CountryName(CountryId);
        public string Language => XboxLive.LanguageName(LanguageId);
        public string SubscriptionTier => XboxLive.TierName(SubscriptionTierId);

        /// <summary>Offline XUIDs begin 0xE0..; online (LIVE) XUIDs begin 0x0009...</summary>
        public bool IsOnlineXuid => (Xuid >> 48) == 0x0009;
    }

    /// <summary>RC4 stream cipher (symmetric; the same call encrypts and decrypts in place).</summary>
    internal static class Rc4
    {
        public static void Apply(byte[] key, int keyLen, byte[] data, int offset, int length)
        {
            var s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % keyLen]) & 0xFF;
                byte t = s[i]; s[i] = s[j]; s[j] = t;
            }
            int a = 0, b = 0;
            for (int n = 0; n < length; n++)
            {
                a = (a + 1) & 0xFF;
                b = (b + s[a]) & 0xFF;
                byte t = s[a]; s[a] = s[b]; s[b] = t;
                data[offset + n] ^= s[(s[a] + s[b]) & 0xFF];
            }
        }
    }

    /// <summary>Xbox LIVE lookup tables for the account's packed country / language / subscription fields.</summary>
    internal static class XboxLive
    {
        public static string TierName(int t)
        {
            switch (t) { case 0: return "None"; case 3: return "Silver"; case 6: return "Gold"; case 9: return "Family Gold"; default: return "Tier " + t; }
        }

        public static string LanguageName(int l)
        {
            string[] names = { "None", "English", "Japanese", "German", "French", "Spanish", "Italian", "Korean",
                               "Traditional Chinese", "Portuguese", "Simplified Chinese", "Polish", "Russian" };
            if (l == 15) return "Norwegian";
            return l >= 0 && l < names.Length ? names[l] : "Language " + l;
        }

        public static string CountryName(int c)
        {
            switch (c)
            {
                case 0: return "Unknown";
                case 0x67: return "United States";
                case 0x23: return "United Kingdom";
                case 0x35: return "Japan";
                case 0x08: return "Australia";
                case 0x0C: return "Austria";
                case 0x0B: return "Belgium";
                case 0x12: return "Brazil";
                case 0x15: return "Canada";
                case 0x1E: return "Denmark";
                case 0x4D: return "Finland";
                case 0x22: return "France";
                case 0x24: return "Germany";
                case 0x36: return "Mexico";
                case 0x6D: return "Netherlands";
                case 0x4C: return "New Zealand";
                case 0x6F: return "Norway";
                case 0x4F: return "Poland";
                case 0x50: return "Portugal";
                case 0x5E: return "Spain";
                case 0x64: return "Sweden";
                case 0x65: return "Switzerland";
                case 0x11: return "Italy";
                case 0x19: return "Colombia";
                case 0x1A: return "Chile";
                case 0x6A: return "Ireland";
                case 0x58: return "Singapore";
                default: return "Country 0x" + c.ToString("X2");
            }
        }
    }
}
