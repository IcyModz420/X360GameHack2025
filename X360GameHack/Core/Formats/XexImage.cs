using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>A resource section a XEX names in its header (for a game: its achievement / title data, named after the title ID).</summary>
    public sealed class XexResource
    {
        public string Name = "";
        public uint Address, Size;
    }

    /// <summary>
    /// The program inside a XEX2 file, decoded natively: the image key is unwrapped with the published XEX key
    /// (retail, or the all-zero development key), the data is AES-CBC decrypted and - depending on the file -
    /// expanded from "basic" (zero-run) or LZX compression. The result is the base file: the PE image as it sits in
    /// memory at <see cref="ImageBase"/>. Compressed files carry a SHA-1 per block, and each one is checked, so a
    /// wrong key or damaged file is reported instead of producing garbage.
    /// Read-only: this opens files the user supplies for viewing, disassembly and reading a game's own resources.
    /// </summary>
    public sealed class XexImage
    {
        private static readonly byte[] RetailKey = { 0x20, 0xB1, 0x85, 0xA5, 0x9D, 0x28, 0xFD, 0xC3, 0x40, 0x58, 0x3F, 0xBB, 0x08, 0x96, 0xBF, 0x91 };
        private static readonly byte[] DevkitKey = new byte[16];
        private const long MaxFileSize = 512L * 1024 * 1024, MaxImageSize = 512L * 1024 * 1024;

        public byte[] BaseFile { get; private set; }
        public uint ImageBase { get; private set; }
        public uint ImageSize { get; private set; }
        public uint EntryPoint { get; private set; }
        /// <summary>0 = not encrypted, 1 = encrypted.</summary>
        public int EncryptionType { get; private set; }
        /// <summary>1 = basic (zero runs), 2 = LZX, 3 = delta patch.</summary>
        public int CompressionType { get; private set; }
        /// <summary>"retail", "devkit" or "" (not encrypted).</summary>
        public string KeyUsed { get; private set; } = "";
        public int WindowSize { get; private set; }
        public List<XexResource> Resources { get; } = new List<XexResource>();

        public string EncryptionName => EncryptionType == 0 ? "Not encrypted" : "Encrypted (" + KeyUsed + " key)";
        public string CompressionName => CompressionType == 1 ? "Not compressed" : CompressionType == 2 ? "LZX compressed" : CompressionType == 3 ? "Delta patch" : "Unknown (" + CompressionType + ")";

        public static XexImage Load(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileSize) throw new InvalidDataException("This file is too large to be decoded here.");
            return Load(BE.ReadAllShared(path));
        }

        public static XexImage Load(byte[] file)
        {
            if (file == null || file.Length < 0x18 || Encoding.ASCII.GetString(file, 0, 4) != "XEX2") throw new InvalidDataException("Not an XEX2 file.");
            var x = new XexImage();
            uint dataOffset = BE.U32(file, 0x08), sec = BE.U32(file, 0x10), count = BE.U32(file, 0x14);
            if (dataOffset < 0x18 || dataOffset > file.Length || sec < 0x18 || (long)sec + 0x184 > dataOffset || count > 0x1000 || 0x18 + (long)count * 8 > dataOffset)
                throw new InvalidDataException("The XEX header is malformed.");

            uint formatAt = 0;
            for (uint i = 0; i < count; i++)
            {
                int e = (int)(0x18 + i * 8);
                uint key = BE.U32(file, e), value = BE.U32(file, e + 4);
                switch (key)
                {
                    case 0x000003FF: formatAt = value; break;
                    case 0x00010100: x.EntryPoint = value; break;
                    case 0x00010201: x.ImageBase = value; break;
                    case 0x000002FF: x.ReadResources(file, value, dataOffset); break;
                }
            }
            x.ImageSize = BE.U32(file, (int)sec + 4);
            if (x.ImageBase == 0) x.ImageBase = BE.U32(file, (int)sec + 0x110);
            if (x.ImageSize == 0 || x.ImageSize > MaxImageSize) throw new InvalidDataException("The XEX declares an image size that cannot be right.");
            if (formatAt == 0 || (long)formatAt + 8 > dataOffset) throw new InvalidDataException("The XEX has no file-format information.");
            uint formatSize = BE.U32(file, (int)formatAt);
            if (formatSize < 8 || (long)formatAt + formatSize > dataOffset) throw new InvalidDataException("The XEX file-format information is malformed.");
            x.EncryptionType = BE.U16(file, (int)formatAt + 4);
            x.CompressionType = BE.U16(file, (int)formatAt + 6);
            if (x.EncryptionType > 1) throw new InvalidDataException("Unknown XEX encryption type " + x.EncryptionType + ".");
            if (x.CompressionType == 3) throw new NotSupportedException("This is a delta patch (a title update's .xexp). It only makes sense applied to the XEX it patches.");
            if (x.CompressionType != 1 && x.CompressionType != 2 && x.CompressionType != 0) throw new InvalidDataException("Unknown XEX compression type " + x.CompressionType + ".");

            var data = new byte[file.Length - dataOffset];
            Buffer.BlockCopy(file, (int)dataOffset, data, 0, data.Length);
            byte[] wrapped = new byte[16];
            Buffer.BlockCopy(file, (int)sec + 0x150, wrapped, 0, 16);

            // The right key is the one whose result proves itself: the first compressed block's hash, or a PE header.
            var attempts = x.EncryptionType == 0 ? new[] { ((byte[])null, "") } : new[] { (RetailKey, "retail"), (DevkitKey, "devkit") };
            Exception last = null;
            foreach (var attempt in attempts)
            {
                try
                {
                    byte[] plain = attempt.Item1 == null ? data : Decrypt(data, Unwrap(wrapped, attempt.Item1));
                    x.BaseFile = x.CompressionType == 2 ? x.ExpandLzx(file, formatAt, formatSize, plain) : x.ExpandBasic(file, formatAt, formatSize, plain);
                    // LZX blocks prove the key by their hashes. An encrypted file without them is only accepted when
                    // the result is a program; an unencrypted file is taken as it is (some hold resources only).
                    bool program = x.BaseFile.Length >= 2 && x.BaseFile[0] == 'M' && x.BaseFile[1] == 'Z';
                    if (attempt.Item1 != null && x.CompressionType != 2 && !program) throw new InvalidDataException("the decoded image is not a program");
                    x.KeyUsed = attempt.Item2;
                    return x;
                }
                catch (InvalidDataException ex) { last = ex; }
                catch (CryptographicException ex) { last = ex; }
            }
            throw new InvalidDataException(x.EncryptionType == 0 ? "The XEX could not be decoded: " + last.Message
                : "The XEX could not be decoded with the retail or the development key (" + last.Message + ").");
        }

        private void ReadResources(byte[] file, uint at, uint limit)
        {
            if ((long)at + 4 > limit) return;
            uint size = BE.U32(file, (int)at);
            if (size < 4 || (long)at + size > limit) return;
            for (uint p = at + 4; p + 16 <= at + size; p += 16)
            {
                string name = Encoding.ASCII.GetString(file, (int)p, 8).TrimEnd('\0');
                Resources.Add(new XexResource { Name = name, Address = BE.U32(file, (int)p + 8), Size = BE.U32(file, (int)p + 12) });
            }
        }

        private static byte[] Unwrap(byte[] wrapped, byte[] key)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB; aes.Padding = PaddingMode.None; aes.Key = key;
                using (var d = aes.CreateDecryptor()) return d.TransformFinalBlock(wrapped, 0, 16);
            }
        }

        private static byte[] Decrypt(byte[] data, byte[] sessionKey)
        {
            int whole = data.Length & ~15;
            var plain = new byte[data.Length];
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.None; aes.Key = sessionKey; aes.IV = new byte[16];
                using (var d = aes.CreateDecryptor())
                {
                    byte[] p = d.TransformFinalBlock(data, 0, whole);
                    Buffer.BlockCopy(p, 0, plain, 0, whole);
                }
            }
            Buffer.BlockCopy(data, whole, plain, whole, data.Length - whole);      // a stray tail (never seen) is not encrypted
            return plain;
        }

        /// <summary>"Basic" compression: runs of stored bytes, each followed by a run of zeros that is not stored.</summary>
        private byte[] ExpandBasic(byte[] file, uint formatAt, uint formatSize, byte[] plain)
        {
            var image = new byte[ImageSize];
            if (CompressionType == 0 || formatSize == 8)
            {
                if (plain.Length > image.Length) throw new InvalidDataException("The XEX holds more data than its image size.");
                Buffer.BlockCopy(plain, 0, image, 0, plain.Length);
                return image;
            }
            long from = 0, to = 0;
            for (uint p = formatAt + 8; p + 8 <= formatAt + formatSize; p += 8)
            {
                uint dataSize = BE.U32(file, (int)p), zeroSize = BE.U32(file, (int)p + 4);
                if (from + dataSize > plain.Length || to + dataSize + zeroSize > image.Length) throw new InvalidDataException("The XEX block list does not fit its data.");
                Buffer.BlockCopy(plain, (int)from, image, (int)to, (int)dataSize);
                from += dataSize;
                to += dataSize + zeroSize;
            }
            return image;
        }

        /// <summary>
        /// LZX: the data is a chain of blocks; each starts with the size and SHA-1 of the next one, followed by chunks
        /// (16-bit size + bytes) of the compressed stream. Every block is checked against its hash before it is used.
        /// </summary>
        private byte[] ExpandLzx(byte[] file, uint formatAt, uint formatSize, byte[] plain)
        {
            if (formatSize < 8 + 4 + 24) throw new InvalidDataException("The XEX compression information is malformed.");
            uint window = BE.U32(file, (int)formatAt + 8);
            int windowBits = 0;
            while (windowBits < 32 && (1u << windowBits) < window) windowBits++;
            if ((1u << windowBits) != window) throw new InvalidDataException("The XEX compression window is not a power of two.");
            WindowSize = (int)window;
            uint blockSize = BE.U32(file, (int)formatAt + 12);
            byte[] expected = new byte[20];
            Buffer.BlockCopy(file, (int)formatAt + 16, expected, 0, 20);

            var stream = new MemoryStream(plain.Length);
            long at = 0;
            using (var sha = SHA1.Create())
                while (blockSize != 0)
                {
                    if (blockSize < 24 || at + blockSize > plain.Length) throw new InvalidDataException("A compressed block runs past the end of the file.");
                    byte[] hash = sha.ComputeHash(plain, (int)at, (int)blockSize);
                    if (!hash.SequenceEqual(expected)) throw new InvalidDataException("A compressed block does not match its hash (wrong key or damaged file).");
                    uint nextSize = BE.U32(plain, (int)at);
                    Buffer.BlockCopy(plain, (int)at + 4, expected, 0, 20);
                    long p = at + 24, end = at + blockSize;
                    while (p + 2 <= end)
                    {
                        int chunk = plain[p] << 8 | plain[p + 1];
                        p += 2;
                        if (chunk == 0) break;
                        if (p + chunk > end) throw new InvalidDataException("A compressed chunk runs past its block.");
                        stream.Write(plain, (int)p, chunk);
                        p += chunk;
                    }
                    at += blockSize;
                    blockSize = nextSize;
                }
            return LzxDecoder.Decompress(stream.ToArray(), (int)ImageSize, windowBits);
        }

        /// <summary>The bytes of a resource, or null when it lies outside the image.</summary>
        public byte[] ReadResource(XexResource r)
        {
            if (r == null || r.Address < ImageBase) return null;
            long offset = r.Address - ImageBase;
            if (offset + r.Size > BaseFile.Length) return null;
            var b = new byte[r.Size];
            Buffer.BlockCopy(BaseFile, (int)offset, b, 0, (int)r.Size);
            return b;
        }

        /// <summary>File offset in the base file of a memory address, or -1.</summary>
        public long OffsetOf(uint address) => address >= ImageBase && address - ImageBase < BaseFile.Length ? address - ImageBase : -1;
    }
}
