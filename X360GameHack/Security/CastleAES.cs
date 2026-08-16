using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using System;
using System.Text;
using System.Windows.Forms;

namespace X360GameHack.Security
{
    internal class CastleAES
    {
        public static string EncryptString(string plainText)
        {
            // Generate a random 256-bit key
            byte[] key = new byte[32]; // 256 bits
            SecureRandom random = new SecureRandom();
            random.NextBytes(key);

            // Convert key to Base64 for display and storage
            string keyBase64 = Convert.ToBase64String(key);

            // Initialize AES engine
            AesEngine engine = new AesEngine();
            KeyParameter keyParam = new KeyParameter(key);
            engine.Init(true, keyParam); // true for encryption

            // Convert input string to bytes
            byte[] inputBytes = Encoding.UTF8.GetBytes(plainText);
            int blockSize = engine.GetBlockSize();

            // Pad input to multiple of block size
            int paddedLength = ((inputBytes.Length / blockSize) + 1) * blockSize;
            byte[] paddedInput = new byte[paddedLength];
            Array.Copy(inputBytes, paddedInput, inputBytes.Length);

            // Encrypt
            byte[] outputBytes = new byte[paddedLength];
            for (int i = 0; i < paddedLength; i += blockSize)
            {
                engine.ProcessBlock(paddedInput, i, outputBytes, i);
            }

            // Convert encrypted bytes to Base64
            string encryptedBase64 = Convert.ToBase64String(outputBytes);
            return encryptedBase64;
        }

        public string DecryptString(string encryptedBase64, string keyBase64)
        {
            try
            {
                // Convert inputs from Base64
                byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);
                byte[] key = Convert.FromBase64String(keyBase64);

                // Initialize AES engine
                AesEngine engine = new AesEngine();
                KeyParameter keyParam = new KeyParameter(key);
                engine.Init(false, keyParam); // false for decryption

                // Decrypt
                byte[] decryptedBytes = new byte[encryptedBytes.Length];
                int blockSize = engine.GetBlockSize();

                for (int i = 0; i < encryptedBytes.Length; i += blockSize)
                {
                    engine.ProcessBlock(encryptedBytes, i, decryptedBytes, i);
                }

                // Convert decrypted bytes to string, trimming padding
                string decryptedText = Encoding.UTF8.GetString(decryptedBytes).TrimEnd('\0');
               // MessageBox.Show(decryptedText);
                return decryptedText;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Decryption failed: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }
    }
}
