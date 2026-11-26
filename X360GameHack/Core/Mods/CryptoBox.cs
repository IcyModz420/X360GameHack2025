using System;
using System.IO;
using System.Security.Cryptography;

namespace X360GameHack.Core.Mods
{
    /// <summary>
    /// Authenticated encryption for mod payloads: AES-256-CBC with an HMAC-SHA-256 over the ciphertext
    /// (encrypt-then-MAC). The keys never come from the open-source program or the file - they are delivered
    /// by the licence server after a token and this machine's id have been checked. Available on .NET
    /// Framework 4.8 (AES-GCM is not), and it is a standard, well-understood construction.
    /// </summary>
    public static class CryptoBox
    {
        public const int KeyBytes = 32, MacBytes = 32, IvBytes = 16;

        /// <summary>iv (16) + hmac(32) + ciphertext.</summary>
        public static byte[] Seal(byte[] keyAes, byte[] keyMac, byte[] plaintext)
        {
            if (keyAes == null || keyAes.Length != KeyBytes) throw new ArgumentException("bad AES key");
            if (keyMac == null || keyMac.Length != KeyBytes) throw new ArgumentException("bad MAC key");
            using (var aes = NewAes(keyAes))
            {
                aes.GenerateIV();
                byte[] iv = aes.IV;
                byte[] ct;
                using (var enc = aes.CreateEncryptor())
                    ct = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
                byte[] mac = Mac(keyMac, iv, ct);
                var outp = new byte[IvBytes + MacBytes + ct.Length];
                Buffer.BlockCopy(iv, 0, outp, 0, IvBytes);
                Buffer.BlockCopy(mac, 0, outp, IvBytes, MacBytes);
                Buffer.BlockCopy(ct, 0, outp, IvBytes + MacBytes, ct.Length);
                return outp;
            }
        }

        /// <summary>Checks the MAC first (constant time), then decrypts. Throws if the data or the key is wrong.</summary>
        public static byte[] Open(byte[] keyAes, byte[] keyMac, byte[] sealed_)
        {
            if (keyAes == null || keyAes.Length != KeyBytes || keyMac == null || keyMac.Length != KeyBytes) throw new ArgumentException("bad key");
            if (sealed_ == null || sealed_.Length < IvBytes + MacBytes) throw new InvalidDataException("mod data is too short");
            var iv = new byte[IvBytes]; Buffer.BlockCopy(sealed_, 0, iv, 0, IvBytes);
            var mac = new byte[MacBytes]; Buffer.BlockCopy(sealed_, IvBytes, mac, 0, MacBytes);
            int ctLen = sealed_.Length - IvBytes - MacBytes;
            var ct = new byte[ctLen]; Buffer.BlockCopy(sealed_, IvBytes + MacBytes, ct, 0, ctLen);
            if (!FixedTimeEquals(mac, Mac(keyMac, iv, ct))) throw new CryptographicException("This mod could not be unlocked with the key the server gave (wrong key, or the file has been tampered with).");
            using (var aes = NewAes(keyAes))
            {
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor())
                    return dec.TransformFinalBlock(ct, 0, ct.Length);
            }
        }

        private static Aes NewAes(byte[] key)
        {
            var aes = Aes.Create();
            aes.KeySize = 256; aes.BlockSize = 128; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            return aes;
        }

        private static byte[] Mac(byte[] keyMac, byte[] iv, byte[] ct)
        {
            using (var h = new HMACSHA256(keyMac))
            {
                h.TransformBlock(iv, 0, iv.Length, null, 0);
                h.TransformFinalBlock(ct, 0, ct.Length);
                return h.Hash;
            }
        }

        public static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>Overwrites a key/plaintext buffer so it does not linger in memory after use.</summary>
        public static void Wipe(byte[] b) { if (b != null) Array.Clear(b, 0, b.Length); }
    }
}
