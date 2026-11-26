using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>Thrown when a 16 KB keyvault is still encrypted and no (or the wrong) CPU key was supplied.</summary>
    public sealed class KeyVaultEncryptedException : IOException
    {
        public KeyVaultEncryptedException(string message) : base(message) { }
    }

    /// <summary>
    /// The part of an Xbox 360 keyvault (KV.bin) that signs CON packages: the console certificate and the console's
    /// 1024-bit RSA key. Accepted as a decrypted 16 KB KV.bin, a decrypted KV without its 16-byte header (0x3FF0 bytes),
    /// or an encrypted KV plus the console's CPU key. A load only succeeds when the key is mathematically proven to
    /// belong to the certificate (p*q = certificate modulus, same exponent, test signature verifies).
    /// The key stays in this process's memory only: it is never written to disk, logged or sent anywhere.
    /// </summary>
    public sealed class KeyVault
    {
        public const int FullSize = 0x4000;
        public const int CertificateSize = 0x1A8;
        private const int KeyOffset = 0x298;   // XECRYPT_RSAPRV_1024 ConsolePrivateKey
        private const int CertOffset = 0x9C8;  // XE_CONSOLE_CERTIFICATE

        public string ConsoleId { get; }        // 10 hex digits
        public string PartNumber { get; }
        public uint ConsoleType { get; }
        public string ManufactureDate { get; }
        public bool WasEncrypted { get; }
        public string TypeName => ConsoleType == 2 ? "retail" : ConsoleType == 1 ? "development kit" : "type " + ConsoleType;
        public string Summary => "console " + ConsoleId + (PartNumber.Length > 0 ? " · " + PartNumber : "") + " · " + TypeName +
                                 (ManufactureDate.Length > 0 ? " · made " + ManufactureDate : "");

        private readonly byte[] _cert;
        private readonly BigInteger _n, _d;
        private readonly int _e;

        private KeyVault(byte[] cert, BigInteger n, int e, BigInteger d, bool encrypted)
        {
            _cert = cert; _n = n; _e = e; _d = d; WasEncrypted = encrypted;
            ConsoleId = BE.Hex(cert, 0x02, 5);
            PartNumber = Ascii(cert, 0x07, 0x0B);
            ConsoleType = BE.U32(cert, 0x18);
            ManufactureDate = Ascii(cert, 0x1C, 8);
        }

        internal byte[] Certificate => (byte[])_cert.Clone();

        /// <summary>Loads a keyvault file. <paramref name="cpuKeyHex"/> is only needed for an encrypted KV.</summary>
        public static KeyVault Load(string path, string cpuKeyHex = null)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("Keyvault file not found.", path);
            if (info.Length != FullSize && info.Length != FullSize - 0x10)
                throw new InvalidDataException("Not a keyvault. A KV.bin is exactly 16 KB (16,384 bytes; 16,368 without its 16-byte header).");
            byte[] data = File.ReadAllBytes(path);
            byte[] cpu = ParseCpuKey(cpuKeyHex);
            try { return FromBytes(data, cpu); }
            finally
            {
                Array.Clear(data, 0, data.Length);
                if (cpu != null) Array.Clear(cpu, 0, cpu.Length);
            }
        }

        /// <summary>32 hex digits (spaces and dashes ignored) -> 16 bytes; null/empty -> null.</summary>
        public static byte[] ParseCpuKey(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            string h = new string(hex.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
            if (h.Length != 32 || !h.All(Uri.IsHexDigit)) throw new FormatException("A CPU key is exactly 32 hex digits.");
            return BE.ParseHexBytes(h);
        }

        public static KeyVault FromBytes(byte[] data, byte[] cpuKey = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == FullSize - 0x10)
                return Parse(data, -0x10, false)
                       ?? throw new InvalidDataException("This 16,368-byte file is not a decrypted keyvault (and without its 16-byte header it cannot be decrypted).");
            if (data.Length != FullSize) throw new InvalidDataException("Not a keyvault (wrong size).");
            var kv = Parse(data, 0, false);
            if (kv != null) return kv;
            if (cpuKey == null)
                throw new KeyVaultEncryptedException("This keyvault is encrypted. Enter the CPU key of the console it came from to decrypt it.");
            // RC4 key = HMAC-SHA1(CPU key, first 16 bytes). The result is only accepted if the decrypted key proves itself.
            foreach (int keyLength in new[] { 0x10, 0x14 })
            {
                byte[] plain = Decrypt(data, cpuKey, keyLength);
                try { kv = Parse(plain, 0, true); }
                finally { Array.Clear(plain, 0, plain.Length); }
                if (kv != null) return kv;
            }
            throw new KeyVaultEncryptedException("That CPU key does not decrypt this keyvault (wrong CPU key, or the file is not a keyvault).");
        }

        private static byte[] Decrypt(byte[] kv, byte[] cpuKey, int keyLength)
        {
            byte[] digest;
            using (var h = new HMACSHA1(cpuKey)) digest = h.ComputeHash(kv, 0, 0x10);
            var plain = (byte[])kv.Clone();
            Rc4(digest.Take(keyLength).ToArray(), plain, 0x10, plain.Length - 0x10);
            Array.Clear(digest, 0, digest.Length);
            return plain;
        }

        private static void Rc4(byte[] key, byte[] data, int offset, int count)
        {
            var s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 0xFF;
                byte t = s[i]; s[i] = s[j]; s[j] = t;
            }
            for (int n = 0, i = 0, j = 0; n < count; n++)
            {
                i = (i + 1) & 0xFF;
                j = (j + s[i]) & 0xFF;
                byte t = s[i]; s[i] = s[j]; s[j] = t;
                data[offset + n] ^= s[(s[i] + s[j]) & 0xFF];
            }
            Array.Clear(s, 0, s.Length);
            Array.Clear(key, 0, key.Length);
        }

        private static KeyVault Parse(byte[] d, int shift, bool encrypted)
        {
            int k = KeyOffset + shift, c = CertOffset + shift;
            if (k < 0 || c + CertificateSize > d.Length) return null;
            if (BE.U16(d, c) != CertificateSize) return null;           // certificate size field
            if (BE.U32(d, k) != 0x10) return null;                       // cqw: 16 qwords = 1024-bit key
            uint e = BE.U32(d, k + 4);
            if (e < 3 || e > 0x7FFFFFFF || (e & 1) == 0 || BE.U32(d, c + 0x24) != e) return null;
            BigInteger n = Num(d, k + 0x10, 0x80), p = Num(d, k + 0x90, 0x40), q = Num(d, k + 0xD0, 0x40);
            if (p <= BigInteger.One || q <= BigInteger.One || p * q != n || Num(d, c + 0x28, 0x80) != n) return null;
            BigInteger dd = ModInverse(e, (p - 1) * (q - 1));
            if (dd.IsZero) return null;
            var cert = new byte[CertificateSize];
            Buffer.BlockCopy(d, c, cert, 0, CertificateSize);
            var kv = new KeyVault(cert, n, (int)e, dd, encrypted);
            // Proof: a test signature must verify with the certificate's own public key.
            byte[] probe;
            using (var sha = SHA1.Create()) probe = sha.ComputeHash(Encoding.ASCII.GetBytes("X360GameHack keyvault self-test"));
            return ConSignature.VerifyRaw(cert, kv.SignDigest(probe), probe) ? kv : null;
        }

        /// <summary>PKCS#1 v1.5 / SHA-1 signature of <paramref name="sha1"/>, in the byte order CON headers store it.</summary>
        internal byte[] SignDigest(byte[] sha1)
        {
            BigInteger m = FromBigEndian(ConSignature.EncodePkcs1Sha1(sha1));
            BigInteger s = BigInteger.ModPow(m, _d, _n);
            if (BigInteger.ModPow(s, _e, _n) != m) throw new CryptographicException("Signature self-check failed.");
            return ToLittleEndian(s, 0x80);
        }

        // ------------------------------------------------------------------ big-number helpers

        /// <summary>XeCrypt "BnQw" number: big-endian qwords, least significant qword first.</summary>
        internal static BigInteger Num(byte[] b, int offset, int length)
        {
            var le = new byte[length + 1];
            for (int i = 0; i < length / 8; i++)
                for (int j = 0; j < 8; j++) le[i * 8 + j] = b[offset + i * 8 + 7 - j];
            return new BigInteger(le);
        }

        internal static BigInteger FromBigEndian(byte[] be)
        {
            var le = new byte[be.Length + 1];
            for (int i = 0; i < be.Length; i++) le[i] = be[be.Length - 1 - i];
            return new BigInteger(le);
        }

        internal static BigInteger FromLittleEndian(byte[] b, int offset, int length)
        {
            var le = new byte[length + 1];
            Buffer.BlockCopy(b, offset, le, 0, length);
            return new BigInteger(le);
        }

        internal static byte[] ToLittleEndian(BigInteger v, int length)
        {
            var raw = v.ToByteArray();
            var r = new byte[length];
            Buffer.BlockCopy(raw, 0, r, 0, Math.Min(length, raw.Length));
            return r;
        }

        internal static byte[] ToBigEndian(BigInteger v, int length)
        {
            var r = ToLittleEndian(v, length);
            Array.Reverse(r);
            return r;
        }

        private static BigInteger ModInverse(BigInteger a, BigInteger m)
        {
            BigInteger g = m, x = 0, x1 = 1, a1 = a % m;
            while (a1 != 0)
            {
                BigInteger q = g / a1;
                (g, a1) = (a1, g - q * a1);
                (x, x1) = (x1, x - q * x1);
            }
            if (g != 1) return BigInteger.Zero;
            return (x % m + m) % m;
        }

        private static string Ascii(byte[] b, int o, int n)
        {
            var s = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                char ch = (char)b[o + i];
                if (ch == 0) break;
                s.Append(ch >= 0x20 && ch < 0x7F ? ch : '?');
            }
            return s.ToString().Trim();
        }
    }

    /// <summary>
    /// CON package signatures. The console certificate sits at 0x004 (0x1A8 bytes) and the package signature at
    /// 0x1AC (0x80 bytes, stored little-endian): RSA-1024 PKCS#1 v1.5 over SHA1(header[0x22C..0x344]) - the license
    /// table, the header hash and the header size. Verified against real console-signed saves.
    /// </summary>
    public static class ConSignature
    {
        public const int CertOffset = 0x004, SignatureOffset = 0x1AC, SignedStart = 0x22C, SignedEnd = 0x344;
        public const int ConsoleIdOffset = 0x36C, ProfileIdOffset = 0x371, DeviceIdOffset = 0x3FD;
        public const int ConsoleIdLength = 5, ProfileIdLength = 8, DeviceIdLength = 0x14;

        private static readonly byte[] Sha1DigestInfo = { 0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2B, 0x0E, 0x03, 0x02, 0x1A, 0x05, 0x00, 0x04, 0x14 };

        public enum State { Valid, BadSignature, BadHeaderHash, Microsoft, NotPackage }

        public sealed class Result
        {
            public State State;
            public string Magic = "";
            public string ConsoleId = "";   // console whose certificate signed it (CON only)
            public string PartNumber = "";
            public uint ConsoleType;        // that certificate's console type: 2 = retail, 1 = development kit
            public bool HashesChecked;      // set by Inspect: every data block and the hash tree were verified
            public bool HashesOk = true;    // valid in the table copies a console reads first
            public bool BackupOk = true;    // valid when the backup copy of a table may vouch instead
            public int BadBlocks;
            public string HashProblem = "";
            public bool IsCon => Magic == "CON";
            public bool IsDevkitSigned => (ConsoleType & 3) == 1;
            /// <summary>False when no console will load it (out-of-date hashes in both table copies, or the header hash).</summary>
            public bool Loads => State != State.NotPackage && State != State.BadHeaderHash && (HashesOk || BackupOk);

            public string Describe()
            {
                if (State == State.NotPackage) return "Not an Xbox 360 STFS package.";
                if (!HashesOk && BackupOk)
                    return "Hash tables need repair - the copy a console reads first is out of date (its backup copy is fine). Click Rehash & resign.";
                if (!HashesOk)
                    return "Hashes are out of date" + (BadBlocks > 0 ? " (" + BadBlocks + " block" + (BadBlocks == 1 ? "" : "s") + ")" : "") +
                           " - consoles will reject it. Click Rehash & resign.";
                switch (State)
                {
                    case State.Valid:
                        return IsDevkitSigned
                            ? "Signed with a development kit keyvault (console " + ConsoleId + ") - devkits and RGH / JTAG load it; stock retail consoles don't."
                            : "Valid on any console (stock and modded) - signed by console " + ConsoleId + ".";
                    case State.BadSignature: return "Valid on RGH / JTAG consoles. A stock console also needs a keyvault signature - load a keyvault and press Rehash & resign.";
                    case State.BadHeaderHash: return "Header hash is out of date - consoles will reject it. Click Rehash & resign.";
                    default: return Magic + " package (Microsoft's format) - stock consoles only load it exactly as Microsoft published it; RGH / JTAG also load it after changes.";
                }
            }
        }

        internal static byte[] EncodePkcs1Sha1(byte[] sha1)
        {
            if (sha1 == null || sha1.Length != 20) throw new ArgumentException("SHA-1 digest expected.");
            var em = new byte[0x80];
            em[1] = 0x01;
            int t = em.Length - Sha1DigestInfo.Length - 20;
            for (int i = 2; i < t - 1; i++) em[i] = 0xFF;
            em[t - 1] = 0x00;
            Buffer.BlockCopy(Sha1DigestInfo, 0, em, t, Sha1DigestInfo.Length);
            Buffer.BlockCopy(sha1, 0, em, t + Sha1DigestInfo.Length, 20);
            return em;
        }

        public static byte[] SignedDigest(byte[] head)
        {
            using (var sha = SHA1.Create()) return sha.ComputeHash(head, SignedStart, SignedEnd - SignedStart);
        }

        /// <summary>SHA1(header[0x344 .. header size rounded up to 4 KB]) - stored at 0x32C.</summary>
        public static byte[] HeaderHash(byte[] head)
        {
            long end = ((long)BE.U32(head, 0x340) + 0xFFF) & ~0xFFFL;
            if (end <= SignedEnd || end > head.Length) throw new InvalidDataException("Malformed package header (bad header size).");
            using (var sha = SHA1.Create()) return sha.ComputeHash(head, SignedEnd, (int)end - SignedEnd);
        }

        /// <summary>Checks a CON signature against the certificate embedded at <paramref name="cert"/>.</summary>
        internal static bool VerifyRaw(byte[] cert, byte[] storedSignature, byte[] sha1)
        {
            if (cert.Length < KeyVault.CertificateSize || BE.U16(cert, 0) != KeyVault.CertificateSize) return false;
            uint e = BE.U32(cert, 0x24);
            if (e < 3 || (e & 1) == 0) return false;
            BigInteger n = KeyVault.Num(cert, 0x28, 0x80);
            BigInteger s = KeyVault.FromLittleEndian(storedSignature, 0, 0x80);
            if (n.IsZero || s >= n) return false;
            byte[] em = KeyVault.ToBigEndian(BigInteger.ModPow(s, e, n), 0x80);
            return em.SequenceEqual(EncodePkcs1Sha1(sha1));
        }

        public static Result Check(byte[] head)
        {
            var r = new Result();
            if (head == null || head.Length < 0x1000) return r;
            r.Magic = Encoding.ASCII.GetString(head, 0, 4).Trim();
            if (r.Magic != "CON" && r.Magic != "LIVE" && r.Magic != "PIRS") { r.State = State.NotPackage; return r; }
            byte[] hh;
            try { hh = HeaderHash(head); }
            catch (InvalidDataException) { r.State = State.NotPackage; return r; }
            if (r.Magic != "CON") { r.State = hh.SequenceEqual(head.Skip(0x32C).Take(20)) ? State.Microsoft : State.BadHeaderHash; return r; }
            var cert = head.Skip(CertOffset).Take(KeyVault.CertificateSize).ToArray();
            r.ConsoleId = BE.Hex(cert, 0x02, 5);
            r.PartNumber = Encoding.ASCII.GetString(cert, 0x07, 0x0B).TrimEnd('\0', ' ');
            r.ConsoleType = BE.U32(cert, 0x18);
            if (!hh.SequenceEqual(head.Skip(0x32C).Take(20))) { r.State = State.BadHeaderHash; return r; }
            var sig = head.Skip(SignatureOffset).Take(0x80).ToArray();
            r.State = VerifyRaw(cert, sig, SignedDigest(head)) ? State.Valid : State.BadSignature;
            return r;
        }

        public static Result Check(string path)
        {
            long size = new FileInfo(path).Length;
            byte[] head = BE.ReadHead(path, 0x10000);
            if (head.Length < 0x1000 || size < 0x1000) return new Result { State = State.NotPackage };
            long end = ((long)BE.U32(head, 0x340) + 0xFFF) & ~0xFFFL;
            if (end > head.Length && end <= 0x100000) head = BE.ReadHead(path, (int)end);
            return Check(head);
        }

        /// <summary>Header fields to replace (null = keep).</summary>
        public sealed class Ids
        {
            public byte[] ProfileId;   // 8 bytes  (0x371) - the profile the content belongs to
            public byte[] DeviceId;    // 20 bytes (0x3FD) - the storage device it was saved on
            public byte[] ConsoleId;   // 5 bytes  (0x36C) - the console that created it (informational)
        }

        /// <summary><see cref="Check(string)"/> plus every data block and the whole hash tree - what a console verifies.</summary>
        public static Result Inspect(string path)
        {
            var r = Check(path);
            if (r.State == State.NotPackage) return r;
            try
            {
                using (var v = StfsVolume.Open(path))
                {
                    if (v.TotalBlocks > 0x70E4) return r;   // too large to verify here: HashesChecked stays false
                    var (_, bad) = v.VerifyAll();
                    string why = null;
                    r.HashesOk = bad == 0 && v.VerifyTree(out why);
                    if (!r.HashesOk)
                    {
                        r.HashProblem = bad > 0 ? bad + " data block(s) do not match their hashes" : why;
                        var (_, backupBad) = v.VerifyAll(strict: false);
                        r.BackupOk = backupBad == 0 && v.VerifyTree(out _, strict: false);
                        r.BadBlocks = backupBad;
                    }
                }
            }
            catch (InvalidDataException ex) { r.HashesOk = r.BackupOk = false; r.HashProblem = ex.Message; }
            r.HashesChecked = true;
            return r;
        }

        /// <summary>
        /// True when Rehash &amp; resign would change nothing: every hash verifies, the IDs already match and the signature
        /// is already as good as <paramref name="kv"/> would make it. Rehashing such a package anyway could only break a
        /// signature that is still valid.
        /// </summary>
        public static bool IsUpToDate(string path, Ids ids, KeyVault kv)
        {
            var r = Inspect(path);
            if (!r.HashesChecked || !r.HashesOk || !r.Loads) return false;
            byte[] head = BE.ReadHead(path, 0x1000);
            if (!Same(ids?.ProfileId, head, ProfileIdOffset) || !Same(ids?.DeviceId, head, DeviceIdOffset) || !Same(ids?.ConsoleId, head, ConsoleIdOffset))
                return false;
            return kv == null || !r.IsCon || (r.State == State.Valid && (r.ConsoleType & 3) == (kv.ConsoleType & 3));
        }

        private static bool Same(byte[] want, byte[] head, int offset) => want == null || head.Skip(offset).Take(want.Length).SequenceEqual(want);

        /// <summary>
        /// Rehash &amp; resign: writes the given IDs, rebuilds every hash (hash tables, top hash, header hash) and - when
        /// <paramref name="kv"/> is given - installs that keyvault's console certificate and RSA-signs the package.
        /// Everything happens on a temporary copy that must verify completely (every data block, the hash tree and, with a
        /// keyvault, the signature) before it replaces the original, so a failure leaves the file untouched.
        /// </summary>
        public static Result Apply(string path, Ids ids, KeyVault kv)
        {
            Validate(ids?.ProfileId, ProfileIdLength, "profile ID");
            Validate(ids?.DeviceId, DeviceIdLength, "device ID");
            Validate(ids?.ConsoleId, ConsoleIdLength, "console ID");
            string magic = Encoding.ASCII.GetString(BE.ReadHead(path, 4)).Trim();
            if (magic != "CON" && magic != "LIVE" && magic != "PIRS") throw new InvalidDataException("Not an Xbox 360 STFS package.");
            if (kv != null && magic != "CON")
                throw new InvalidOperationException(magic + " packages are signed by Microsoft. Only CON packages (saves, profiles, gamer content) can be signed with a console keyvault.");

            string tmp = path + ".resigning";
            File.Copy(path, tmp, true);
            try
            {
                if (ids != null && (ids.ProfileId != null || ids.DeviceId != null || ids.ConsoleId != null))
                    using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        if (ids.ProfileId != null) { fs.Position = ProfileIdOffset; fs.Write(ids.ProfileId, 0, ProfileIdLength); }
                        if (ids.DeviceId != null) { fs.Position = DeviceIdOffset; fs.Write(ids.DeviceId, 0, DeviceIdLength); }
                        if (ids.ConsoleId != null) { fs.Position = ConsoleIdOffset; fs.Write(ids.ConsoleId, 0, ConsoleIdLength); }
                    }
                StfsEditor.RehashInPlace(tmp);
                if (kv != null)
                {
                    byte[] head;
                    using (var v = StfsVolume.Open(tmp)) head = v.ReadHead();
                    Buffer.BlockCopy(kv.Certificate, 0, head, CertOffset, KeyVault.CertificateSize);
                    Buffer.BlockCopy(kv.SignDigest(SignedDigest(head)), 0, head, SignatureOffset, 0x80);
                    if (Check(head).State != State.Valid) throw new CryptographicException("The new signature did not verify.");
                    WriteHead(tmp, head);
                }
                var r = Inspect(tmp);
                if (!r.HashesChecked || !r.HashesOk || !r.Loads)
                    throw new InvalidDataException("The rehashed package did not verify (" + (r.HashProblem.Length > 0 ? r.HashProblem : r.Describe()) + ").");
                if (kv != null && r.State != State.Valid) throw new InvalidDataException("The signature did not verify after writing.");
                StfsEditor.Commit(tmp, path);
                return r;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>What happened to one file of a batch.</summary>
        public enum BatchOutcome { Resigned, AlreadyGood, NotPackage, Failed }

        /// <summary>
        /// Rehash &amp; resign for many files: each one keeps its own IDs, gets every hash rebuilt and - CON packages, when
        /// a keyvault is given - a new signature. A file that is already good is left untouched; one that fails does not
        /// stop the rest. <paramref name="beforeChange"/> runs just before a file is rewritten (for a backup).
        /// </summary>
        public static Dictionary<string, BatchOutcome> ApplyMany(IEnumerable<string> paths, KeyVault kv, Action<string> beforeChange = null,
                                                                 Action<string, BatchOutcome, string> report = null)
        {
            var results = new Dictionary<string, BatchOutcome>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths ?? Enumerable.Empty<string>())
            {
                if (results.ContainsKey(path)) continue;
                BatchOutcome outcome; string detail = "";
                try
                {
                    var now = Check(path);
                    if (now.State == State.NotPackage) outcome = BatchOutcome.NotPackage;
                    else
                    {
                        var use = now.IsCon ? kv : null;                      // LIVE / PIRS: hashes only
                        if (IsUpToDate(path, null, use)) outcome = BatchOutcome.AlreadyGood;
                        else
                        {
                            beforeChange?.Invoke(path);
                            detail = Apply(path, null, use).Describe();
                            outcome = BatchOutcome.Resigned;
                        }
                    }
                }
                catch (Exception ex) { outcome = BatchOutcome.Failed; detail = ex.Message; }
                results[path] = outcome;
                report?.Invoke(path, outcome, detail);
            }
            return results;
        }

        private static void Validate(byte[] b, int length, string what)
        {
            if (b != null && b.Length != length) throw new ArgumentException("The " + what + " must be " + length + " bytes (" + length * 2 + " hex digits).");
        }

        private static void WriteHead(string path, byte[] head)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                fs.Position = 0;
                fs.Write(head, 0, head.Length);
                fs.Flush(true);
            }
        }

        /// <summary>
        /// Profile ID and device ID of another package (a save from the target profile, or the profile itself).
        /// Falls back to the file name for profiles (their file is named after the profile ID).
        /// </summary>
        public static (byte[] ProfileId, byte[] DeviceId, string Note) ReadIds(string path)
        {
            byte[] head = BE.ReadHead(path, 0x1000);
            string magic = head.Length >= 4 ? Encoding.ASCII.GetString(head, 0, 4) : "";
            if (head.Length < 0x1000 || (magic != "CON " && magic != "LIVE" && magic != "PIRS"))
                throw new InvalidDataException("Not an Xbox 360 package (expected a save or a profile).");
            byte[] profile = head.Skip(ProfileIdOffset).Take(ProfileIdLength).ToArray();
            byte[] device = head.Skip(DeviceIdOffset).Take(DeviceIdLength).ToArray();
            uint type = BE.U32(head, 0x344);
            string note = type == 0x10000 ? "profile" : type == 1 ? "save" : "package";
            if (profile.All(b => b == 0))
            {
                string name = Path.GetFileName(path);
                if (name.Length == 16 && name.All(Uri.IsHexDigit)) { profile = BE.ParseHexBytes(name); note += " (profile ID from its file name)"; }
            }
            return (profile.All(b => b == 0) ? null : profile, device.All(b => b == 0) ? null : device, note);
        }
    }
}
