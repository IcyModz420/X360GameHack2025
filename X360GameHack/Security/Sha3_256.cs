using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Digests;
using System.IO;

namespace X360GameHack
{
    public class Sha3_256
    {
        public static string ComputeSha3_256(string filePath)
        {
            AntiDebug AntiDebug = new AntiDebug();
            AntiDebug.DoAntiDebugFunc();
            if (File.Exists(filePath))
            {
                var digest = new Sha3Digest(256); // 256-bit SHA-3
                byte[] buffer = new byte[8192];

                using (var stream = File.OpenRead(filePath))
                {
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        digest.BlockUpdate(buffer, 0, bytesRead);
                    }
                }

                byte[] result = new byte[digest.GetDigestSize()];
                digest.DoFinal(result, 0);
                return BitConverter.ToString(result).Replace("-", "").ToLower();
            }
            else
            {
                Environment.Exit(0);
                return "No Race Condition here.";
            }
        }
    }
}
