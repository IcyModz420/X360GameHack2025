using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace X360GameHack.Core.Mods
{
    /// <summary>
    /// Who made a mod file. Every .x360mod ends with its maker's signature: "X360SIG1" and an ECDSA P-256 signature
    /// (64 bytes, r || s) over the SHA-256 of everything before it. The program loads only mods signed with the
    /// X360GameHack owner's key - the public half is built in below, the private half never leaves the owner's PC
    /// (<see cref="OwnerKey"/>) - so nobody else can make a mod package this program will use, free or paid, and a
    /// changed mod file no longer loads. A reader that does not know about signatures ignores the trailing bytes.
    /// </summary>
    public static class ModSignature
    {
        public const string Magic = "X360SIG1";
        public const int Length = 8 + 64;

        /// <summary>The X360GameHack owner's public signing key: P-256, X || Y, base64.</summary>
        private static readonly string[] OwnerKeys = { OwnerPublicKey };

        /// <summary>The owner's key, made once on the owner's PC on 2026-11-25; a public key, safe to publish.</summary>
        public const string OwnerPublicKey = "0UYrmNbO2CodyCPxnml5cErlBMZm5IzjcG5L/N/hEcBNdje8tz2vWjtScuDnwi8mfxnOqAVs/Sf1t5Y7f18iQg==";

        /// <summary>For automated tests only: more public keys (X || Y) taken as the owner's. Empty in normal use.</summary>
        internal static readonly List<byte[]> TestKeys = new List<byte[]>();

        /// <summary>The signed part of a file and its signature, or (null, null) when it carries none.</summary>
        public static (byte[] Body, byte[] Signature) Split(byte[] file)
        {
            if (file == null || file.Length < Length + 11) return (null, null);
            int at = file.Length - Length;
            if (Encoding.ASCII.GetString(file, at, 8) != Magic) return (null, null);
            var body = new byte[at];
            Buffer.BlockCopy(file, 0, body, 0, at);
            var sig = new byte[64];
            Buffer.BlockCopy(file, at + 8, sig, 0, 64);
            return (body, sig);
        }

        /// <summary>True when the file ends with a valid signature by the owner's key.</summary>
        public static bool ByOwner(byte[] file)
        {
            var (body, sig) = Split(file);
            if (body == null) return false;
            foreach (byte[] key in OwnerKeys.Where(k => !string.IsNullOrEmpty(k)).Select(Convert.FromBase64String).Concat(TestKeys))
                if (Verify(key, body, sig)) return true;
            return false;
        }

        /// <summary>Checks a P-256 signature (r || s) over SHA-256 of <paramref name="data"/> with a public key (X || Y).</summary>
        public static bool Verify(byte[] publicXY, byte[] data, byte[] signature)
        {
            if (publicXY == null || publicXY.Length != 64 || signature == null || signature.Length != 64) return false;
            try
            {
                var q = new ECPoint { X = publicXY.Take(32).ToArray(), Y = publicXY.Skip(32).ToArray() };
                using (var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = q }))
                    return ec.VerifyData(data, signature, HashAlgorithmName.SHA256);
            }
            catch (CryptographicException) { return false; }
            catch (ArgumentException) { return false; }
        }

        /// <summary>The file signed with <paramref name="key"/> (a signature already on it is replaced).</summary>
        public static byte[] Sign(byte[] file, ECDsa key)
        {
            if (key == null) throw new InvalidOperationException("No signing key.");
            byte[] body = Split(file).Body ?? file;
            byte[] sig = key.SignData(body, HashAlgorithmName.SHA256);
            if (sig.Length != 64) throw new CryptographicException("Unexpected signature size " + sig.Length + ".");
            using (var ms = new MemoryStream(body.Length + Length))
            {
                ms.Write(body, 0, body.Length);
                ms.Write(Encoding.ASCII.GetBytes(Magic), 0, 8);
                ms.Write(sig, 0, 64);
                return ms.ToArray();
            }
        }

        /// <summary>The public half of a key, X || Y (64 bytes).</summary>
        public static byte[] PublicOf(ECDsa key)
        {
            var p = key.ExportParameters(false);
            return p.Q.X.Concat(p.Q.Y).ToArray();
        }
    }

    /// <summary>
    /// The X360GameHack owner's private signing key, which exists only on the owner's PC:
    /// Documents\X360GameHack-private\owner-signing\owner-signing-key.dpapi, protected with Windows DPAPI for that
    /// Windows user (a copy is useless on another PC or account). With it, the Mod Maker shows and every mod it makes is
    /// signed; without it, this program cannot make mod packages at all.
    /// </summary>
    public static class OwnerKey
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("X360GameHack owner mod-signing key v1");

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "X360GameHack-private", "owner-signing", "owner-signing-key.dpapi");

        /// <summary>For automated tests only: stands in for the owner's key. Null in normal use.</summary>
        internal static ECDsa TestKey;

        private static bool _looked;
        private static ECDsa _key;

        /// <summary>True when this PC holds the owner's key and it matches the public key built into the program.</summary>
        public static bool Available => Load() != null;

        /// <summary>The owner's key, or null when this PC does not hold it (or it is not the one the program trusts).</summary>
        public static ECDsa Load()
        {
            if (TestKey != null) return TestKey;
            if (_looked) return _key;
            _looked = true;
            _key = LoadFrom(FilePath);
            return _key;
        }

        /// <summary>The owner key stored DPAPI-protected at <paramref name="path"/> for this Windows user, or null when there is none or it is not the trusted one.</summary>
        internal static ECDsa LoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    if (raw.Length != 96) return null;
                    var p = new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        D = raw.Take(32).ToArray(),
                        Q = new ECPoint { X = raw.Skip(32).Take(32).ToArray(), Y = raw.Skip(64).Take(32).ToArray() }
                    };
                    var key = ECDsa.Create(p);
                    // only the key whose public half is built in counts as the owner's
                    byte[] probe = Encoding.ASCII.GetBytes("x360gh owner key check");
                    byte[] pub = ModSignature.PublicOf(key);
                    bool trusted = !string.IsNullOrEmpty(ModSignature.OwnerPublicKey) && Convert.FromBase64String(ModSignature.OwnerPublicKey).SequenceEqual(pub)
                                   && ModSignature.Verify(pub, probe, key.SignData(probe, HashAlgorithmName.SHA256));
                    if (!trusted) { key.Dispose(); return null; }
                    return key;
                }
                finally { Array.Clear(raw, 0, raw.Length); }
            }
            catch (CryptographicException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // ---------------------------------------------------------------- backup with a password
        // The DPAPI copy dies with the Windows profile. A backup file is the key sealed (CryptoBox: AES-256-CBC + HMAC-SHA256)
        // under keys derived from a password (PBKDF2-SHA256, 600,000 rounds, random salt):
        //   "X360OKB1" | rounds (u32 BE) | salt (16) | public X || Y (64, to tell which key it is) | sealed D || X || Y
        private const string BackupMagic = "X360OKB1";
        public const int BackupRounds = 600000, MinPasswordLength = 12;

        private static (byte[] Aes, byte[] Mac) BackupKeys(string password, byte[] salt, int rounds)
        {
            byte[] pw = Encoding.UTF8.GetBytes(password);
            try
            {
                using (var kdf = new Rfc2898DeriveBytes(pw, salt, rounds, HashAlgorithmName.SHA256))
                {
                    byte[] k = kdf.GetBytes(64);
                    var a = k.Take(32).ToArray(); var m = k.Skip(32).ToArray();
                    Array.Clear(k, 0, k.Length);
                    return (a, m);
                }
            }
            finally { Array.Clear(pw, 0, pw.Length); }
        }

        /// <summary>Writes a password-protected backup of the owner's key to <paramref name="path"/> (never overwrites).</summary>
        public static void ExportBackup(string path, string password)
        {
            var key = Load() ?? throw new InvalidOperationException("This PC does not hold the signing key.");
            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength) throw new ArgumentException("Use a password of at least " + MinPasswordLength + " characters.");
            if (File.Exists(path)) throw new IOException("A file with that name exists already - pick a new name.");
            var p = key.ExportParameters(true);
            byte[] raw = p.D.Concat(p.Q.X).Concat(p.Q.Y).ToArray();
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            var (ka, km) = BackupKeys(password, salt, BackupRounds);
            try
            {
                byte[] sealed_ = CryptoBox.Seal(ka, km, raw);
                using (var ms = new MemoryStream())
                {
                    ms.Write(Encoding.ASCII.GetBytes(BackupMagic), 0, 8);
                    int rounds = BackupRounds;
                    ms.Write(new[] { (byte)(rounds >> 24), (byte)(rounds >> 16), (byte)(rounds >> 8), (byte)rounds }, 0, 4);
                    ms.Write(salt, 0, 16);
                    ms.Write(p.Q.X, 0, 32); ms.Write(p.Q.Y, 0, 32);
                    ms.Write(sealed_, 0, sealed_.Length);
                    using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) ms.WriteTo(f);
                }
            }
            finally { CryptoBox.Wipe(raw); CryptoBox.Wipe(ka); CryptoBox.Wipe(km); Array.Clear(p.D, 0, p.D.Length); }
            // prove the file opens again with that password before saying it is done
            try { using (var back = ReadBackup(path, password)) if (!ModSignature.PublicOf(back).SequenceEqual(ModSignature.PublicOf(key))) throw new CryptographicException("mismatch"); }
            catch (Exception) { try { File.Delete(path); } catch (IOException) { } throw new IOException("The backup did not read back correctly - it was not kept."); }
        }

        /// <summary>The key inside a backup file (throws on a wrong password, a damaged file, or a key the program does not trust).</summary>
        public static ECDsa ReadBackup(string path, string password)
        {
            byte[] f = File.ReadAllBytes(path);
            if (f.Length < 8 + 4 + 16 + 64 + 48 || Encoding.ASCII.GetString(f, 0, 8) != BackupMagic) throw new InvalidDataException("That is not an X360GameHack signing-key backup.");
            int rounds = f[8] << 24 | f[9] << 16 | f[10] << 8 | f[11];
            if (rounds < 100000 || rounds > 10000000) throw new InvalidDataException("The backup file is damaged.");
            byte[] salt = f.Skip(12).Take(16).ToArray(), pub = f.Skip(28).Take(64).ToArray(), sealed_ = f.Skip(92).ToArray();
            if (string.IsNullOrEmpty(ModSignature.OwnerPublicKey) || !Convert.FromBase64String(ModSignature.OwnerPublicKey).SequenceEqual(pub))
                throw new InvalidDataException("That backup is not of the key this program trusts.");
            var (ka, km) = BackupKeys(password ?? "", salt, rounds);
            byte[] raw = null;
            try
            {
                try { raw = CryptoBox.Open(ka, km, sealed_); }
                catch (CryptographicException) { throw new CryptographicException("Wrong password (or the backup file was changed)."); }
                if (raw.Length != 96 || !raw.Skip(32).SequenceEqual(pub)) throw new InvalidDataException("The backup file is damaged.");
                var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = raw.Take(32).ToArray(), Q = new ECPoint { X = pub.Take(32).ToArray(), Y = pub.Skip(32).ToArray() } });
                byte[] probe = Encoding.ASCII.GetBytes("x360gh owner key check");
                if (!ModSignature.Verify(pub, probe, key.SignData(probe, HashAlgorithmName.SHA256))) { key.Dispose(); throw new InvalidDataException("The backup file is damaged."); }
                return key;
            }
            finally { CryptoBox.Wipe(raw); CryptoBox.Wipe(ka); CryptoBox.Wipe(km); }
        }

        /// <summary>
        /// Puts the key from a backup back on this PC for this Windows user (DPAPI, at <paramref name="target"/> or the
        /// usual place). Never replaces a key that is there already.
        /// </summary>
        public static void RestoreBackup(string backupPath, string password, string target = null)
        {
            target = target ?? FilePath;
            if (File.Exists(target)) throw new IOException("This PC already holds a signing key (" + target + ") - it is never replaced.");
            using (var key = ReadBackup(backupPath, password))
            {
                var p = key.ExportParameters(true);
                byte[] raw = p.D.Concat(p.Q.X).Concat(p.Q.Y).ToArray();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var f = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                    {
                        byte[] prot = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
                        f.Write(prot, 0, prot.Length);
                    }
                }
                finally { Array.Clear(raw, 0, raw.Length); Array.Clear(p.D, 0, p.D.Length); }
            }
            _looked = false; _key = null;                                       // look again next time
        }

        /// <summary>
        /// Makes a new owner key and stores it DPAPI-protected at <paramref name="path"/> (never overwrites). Returns the
        /// public half (X || Y) to build into the program. Run once, on the owner's PC, by the owner's own tool.
        /// </summary>
        public static byte[] Create(string path)
        {
            if (File.Exists(path)) throw new IOException("An owner key already exists at " + path + " - it is never replaced.");
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var p = key.ExportParameters(true);
                byte[] raw = p.D.Concat(p.Q.X).Concat(p.Q.Y).ToArray();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser));
                }
                finally { Array.Clear(raw, 0, raw.Length); Array.Clear(p.D, 0, p.D.Length); }
                return p.Q.X.Concat(p.Q.Y).ToArray();
            }
        }
    }
}
