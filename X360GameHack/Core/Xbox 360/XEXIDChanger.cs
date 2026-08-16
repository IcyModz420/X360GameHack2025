using System;
using System.Collections.Generic;
using System.IO;

public class XEXIDChanger
{
    /// <summary>
    /// Searches a file for all instances of an old Xbox 360 Title ID and overwrites them with a new one.
    /// </summary>
    public static void PatchXEXTitleID(string filePath, string oldTitleIdHex, string newTitleIdHex)
    {
        ReplaceHexInFile(filePath, oldTitleIdHex, newTitleIdHex, "Title ID");
    }

    /// <summary>
    /// Searches a file for all instances of an old Xbox 360 Media ID and overwrites them with a new one.
    /// </summary>
    public static void PatchXEXMediaID(string filePath, string oldMediaIdHex, string newMediaIdHex)
    {
        ReplaceHexInFile(filePath, oldMediaIdHex, newMediaIdHex, "Media ID");
    }

    // --- Core Patching Engine ---

    private static void ReplaceHexInFile(string filePath, string oldHex, string newHex, string idType)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("The specified file does not exist.", filePath);

        // Clean up the inputs
        oldHex = oldHex.Replace(" ", "").Replace("0x", "");
        newHex = newHex.Replace(" ", "").Replace("0x", "");

        if (oldHex.Length != 8 || newHex.Length != 8)
            throw new ArgumentException($"Xbox 360 {idType}s must be exactly 8 characters long (4 bytes).");

        byte[] oldBytes = StringToByteArray(oldHex);
        byte[] newBytes = StringToByteArray(newHex);

        byte[] fileData = File.ReadAllBytes(filePath);

        // Retrieve ALL matching offsets instead of just the first one
        List<int> offsets = FindAllBytePatterns(fileData, oldBytes);

        if (offsets.Count > 0)
        {
            // Loop through every found offset and overwrite the bytes
            foreach (int offset in offsets)
            {
                for (int i = 0; i < newBytes.Length; i++)
                {
                    fileData[offset + i] = newBytes[i];
                }
            }

            // Save the fully patched file
            File.WriteAllBytes(filePath, fileData);
        }
        else
        {
            throw new InvalidOperationException($"Could not find the original {idType} inside the file.");
        }
    }

    // --- Private Helper Methods ---

    private static byte[] StringToByteArray(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }

    /// <summary>
    /// Scans the entire byte array and returns a list of all starting index locations 
    /// where the specific pattern is found.
    /// </summary>
    private static List<int> FindAllBytePatterns(byte[] data, byte[] pattern)
    {
        List<int> foundOffsets = new List<int>();

        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                foundOffsets.Add(i);

                // Skip ahead by the length of the pattern to prevent overlapping matches
                // (e.g., finding the pattern inside the pattern we just found)
                i += pattern.Length - 1;
            }
        }

        return foundOffsets;
    }
}