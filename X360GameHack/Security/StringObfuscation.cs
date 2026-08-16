using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Security;

namespace X360GameHack
{
    [Confuser.Obfuscate]
    public class StringXORing
    {
        private readonly string originalValue; // Stores the original string for reference
        private string obfuscatedValue; // Stores the obfuscated string
        private int obfuscationKey; // Dynamic key for obfuscation
        private readonly byte[] expectedHash; // Precomputed hash for integrity


        public StringXORing(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            originalValue = value;
            expectedHash = ComputeHash(value); // Compute hash at initialization
            obfuscationKey = Environment.TickCount; // Initialize with dynamic key
            obfuscatedValue = XorString(value, obfuscationKey);
        }

        // Static factory method as an alternative (returns StringXORing)
        public static StringXORing Create(string value)
        {
            return new StringXORing(value);
        }

        // Public readonly property to access the string
        public string Value
        {
            get
            {
                string currentValue = XorString(obfuscatedValue, obfuscationKey);
                if (!VerifyHash(currentValue))
                {
                    throw new SecurityException("String tampering detected! If your here.. Shove that cheat engine up your ass!!");
                    // Alternative: Exit app, log incident, or return fallback value
                }
                return currentValue;
            }
        }

        // Implicit conversion to string for seamless use
        public static implicit operator string(StringXORing StringXORing)
        {
            return StringXORing.Value;
        }

        // Periodically re-obfuscate to make the memory value dynamic
        public void PeriodicUpdate()
        {
            string currentValue = XorString(obfuscatedValue, obfuscationKey);
            if (!VerifyHash(currentValue))
            {
                throw new SecurityException("String tampering detected! If your here.. Shove that cheat engine up your ass!!");
            }
            obfuscationKey = Environment.TickCount; // New key
            obfuscatedValue = XorString(currentValue, obfuscationKey);
        }

        // XOR the string with a key for obfuscation
        private string XorString(string input, int key)
        {
            char[] result = new char[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = (char)(input[i] ^ (key >> (i % 4)));
            }
            return new string(result);
        }

        // Compute SHA256 hash of the string
        private byte[] ComputeHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            }
        }

        // Verify the string's hash against the expected hash
        private bool VerifyHash(string input)
        {
            byte[] currentHash = ComputeHash(input);
            if (currentHash.Length != expectedHash.Length)
                return false;
            for (int i = 0; i < currentHash.Length; i++)
            {
                if (currentHash[i] != expectedHash[i])
                    return false;
            }
            return true;
        }
    }
}