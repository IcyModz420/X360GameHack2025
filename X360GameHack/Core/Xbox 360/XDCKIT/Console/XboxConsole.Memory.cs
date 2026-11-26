// =============================================================================
// XDCKIT.XboxConsole.Memory.cs - Typed memory R/W partial
// =============================================================================
// Provides the canonical XDevkit-style raw memory API:
//
//   GetMemory(addr, length)                     -> byte[]
//   GetMemory(addr, length, buf, out bytesRead)
//   SetMemory(addr, data)                       -> ()
//   SetMemory(addr, len, data, out bytesWritten)
//
// Plus typed convenience helpers (PowerPC big-endian aware):
//
//   ReadByte / ReadSByte / ReadBool / ReadInt16 / ReadUInt16 / ReadInt32
//   ReadUInt32 / ReadInt64 / ReadUInt64 / ReadFloat / ReadDouble / ReadString
//   ...same set of WriteXxx
//   ...plus array variants (e.g. SetUInt32(addr, uint[]), GetFloat(addr, count)).
//
// And arithmetic in-place ops (XOR/AND/OR) for atomic-ish flag tweaks.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

    public partial class XboxConsole
    {
        #region Raw GetMemory / SetMemory  (xbdm: getmemex / setmem)

        /// <summary>
        /// Read <paramref name="length"/> bytes from <paramref name="address"/>
        /// using the binary <c>getmemex</c> command.  Returns the raw bytes.
        /// </summary>
        public byte[] GetMemory(uint address, uint length)
        {
            var buf = new byte[length];
            GetMemory(address, length, buf, out uint got);
            if (got < length)
                throw new InvalidOperationException("Memory at 0x" + (address + got).ToString("X8") + " can't be read (nothing is mapped there).");
            return buf;
        }

        /// <summary>
        /// Read a range that may have gaps. Bytes that can't be read come back as zero and each gap is added to
        /// <paramref name="holes"/> (start, length) when a list is given. Gaps are skipped a 4 KB page at a time.
        /// </summary>
        public byte[] GetMemoryWithGaps(uint address, uint length, List<KeyValuePair<uint, uint>> holes = null)
        {
            var buf = new byte[length];
            var part = new byte[Math.Min(length, 0x10000u)];
            uint done = 0;
            while (done < length)
            {
                uint want = Math.Min((uint)part.Length, length - done);
                GetMemory(address + done, want, part, out uint got);
                Buffer.BlockCopy(part, 0, buf, (int)done, (int)got);
                done += got;
                if (got == want) continue;
                ulong nextPage = (((ulong)address + done) & ~0xFFFul) + 0x1000;
                uint skip = (uint)Math.Min(nextPage - ((ulong)address + done), length - done);
                holes?.Add(new KeyValuePair<uint, uint>(address + done, skip));
                done += skip;
            }
            return buf;
        }

        /// <summary>
        /// XDevkit-style overload: read into a caller-allocated buffer and
        /// report how many bytes actually came back.
        /// </summary>
        public void GetMemory(uint address, uint bytesToRead, byte[] data, out uint bytesRead)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < bytesToRead) throw new ArgumentException("Buffer too small.", nameof(data));
            if (!Connected) throw new InvalidOperationException("Not connected.");
            if (bytesToRead == 0) { bytesRead = 0; return; }

            string cmd = $"getmemex addr=0x{address:X8} length=0x{bytesToRead:X8}";
            var resp = Client.SendTextCommand(cmd);

            if ((int)resp.Status != 203)
                throw new InvalidOperationException($"GETMEMEX expected 203, got {(int)resp.Status}: {resp.StatusMessage}");

            // Measured on a console (xbdm 2.0.21076.11): the body is a run of packets, each led by a 16-bit
            // little-endian word. Its low 15 bits are how many data bytes follow (at most 0x400) and its top bit
            // marks the last packet. Unreadable memory ends the run early: an unmapped address answers with the
            // single word 0x8000 and no data, so the count has to be taken from the words, not assumed.
            int dest = 0;
            var head = new byte[2];
            var packet = new byte[0x8000];
            while (true)
            {
                Client.ReadExact(head, 0, 2);
                int word = head[0] | head[1] << 8;
                int count = word & 0x7FFF;
                bool last = (word & 0x8000) != 0;
                if (count > 0)
                {
                    Client.ReadExact(packet, 0, count);
                    int take = (int)Math.Min(count, bytesToRead - (uint)dest);
                    Buffer.BlockCopy(packet, 0, data, dest, take);
                    dest += take;
                }
                if (last || (count == 0)) break;
                if ((uint)dest >= bytesToRead) break;
            }
            if ((uint)dest < bytesToRead) Array.Clear(data, dest, (int)bytesToRead - dest);
            bytesRead = (uint)dest;
        }

        /// <summary>
        /// Write <paramref name="data"/> to <paramref name="address"/> using
        /// the text-mode <c>setmem</c> command (hex-encoded).
        /// </summary>
        public void SetMemory(uint address, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            SetMemory(address, (uint)data.Length, data, out _);
        }

        /// <summary>XDevkit-style overload: writes <paramref name="bytesToWrite"/> bytes from <paramref name="data"/>.</summary>
        public void SetMemory(uint address, uint bytesToWrite, byte[] data, out uint bytesWritten)
        {
            if (!Connected) throw new InvalidOperationException("Not connected.");
            if (bytesToWrite == 0) { bytesWritten = 0; return; }
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < bytesToWrite) throw new ArgumentException("Buffer too small.", nameof(data));

            // One command line holds 512 characters on the console's xbdm: a longer setmem is refused (446, seen on a real
            // console with a 336-byte write). Writes go out in pieces of at most SetMemoryChunk bytes, in order.
            bytesWritten = 0;
            while (bytesWritten < bytesToWrite)
            {
                uint count = Math.Min(SetMemoryChunk, bytesToWrite - bytesWritten);
                var sb = new StringBuilder(48 + (int)count * 2);
                sb.Append("setmem addr=0x").Append((address + bytesWritten).ToString("X8")).Append(" data=");
                for (uint i = 0; i < count; i++) sb.Append(data[bytesWritten + i].ToString("X2"));

                var resp = Client.SendTextCommand(sb.ToString());
                if (!resp.IsSuccess)
                    throw new InvalidOperationException($"SETMEM failed at 0x{address + bytesWritten:X8}: {(int)resp.Status} {resp.StatusMessage}");

                // The xbdm reply for setmem is "200- N bytes set"; fewer than asked = it stopped there.
                uint set = count;
                if (!string.IsNullOrEmpty(resp.StatusMessage))
                {
                    int spaceIdx = resp.StatusMessage.IndexOf(' ');
                    if (spaceIdx > 0 &&
                        uint.TryParse(resp.StatusMessage.Substring(0, spaceIdx),
                                      NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        set = Math.Min(n, count);
                }
                bytesWritten += set;
                if (set < count) return;
            }
        }

        /// <summary>The most bytes one setmem command carries (hex makes it twice as long; the console's command line is 512 characters).</summary>
        public const uint SetMemoryChunk = 128;

        /// <summary>Set <paramref name="size"/> bytes at <paramref name="address"/> to all 0x00.</summary>
        public void NullAddress(uint address, uint size)
        {
            if (size == 0) return;
            SetMemory(address, new byte[size]);
        }

        /// <summary>Hint to xbdm that an executable region was just modified (force icache flush).</summary>
        public void InvalidateMemoryCache(bool executablePages, uint address, uint size)
        {
            try { Client.SendTextCommand($"invalmem addr=0x{address:X8} length=0x{size:X8}"); }
            catch { /* ignore - host has no support */ }
        }

        #endregion

        #region Memory dump

        /// <summary>Dump <paramref name="length"/> bytes starting at <paramref name="address"/> directly to a file.</summary>
        public void DumpMemory(uint address, uint length, string fileName)
        {
            if (!Connected) throw new InvalidOperationException("Not connected.");
            if (length == 0) return;

            using (var fs = new System.IO.FileStream(fileName,
                       System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
            {
                uint chunkSize = (uint)Math.Min(XboxClient.MemoryDumpChunkSize, (long)length);
                var buf = new byte[chunkSize];
                uint remaining = length;
                uint cur = address;
                while (remaining > 0)
                {
                    uint chunk = Math.Min((uint)buf.Length, remaining);
                    fs.Write(GetMemoryWithGaps(cur, chunk), 0, (int)chunk);
                    cur += chunk;
                    remaining -= chunk;
                }
            }
        }

        #endregion

        #region Big-endian read/write primitives (single helper for every scalar)

        /// <summary>Read <paramref name="size"/> bytes at <paramref name="address"/> and reverse them in place (PPC BE -> host LE).</summary>
        private byte[] ReadBigEndian(uint address, uint size)
        {
            var b = GetMemory(address, size);
            Array.Reverse(b);
            return b;
        }

        /// <summary>Write a value already converted to <c>byte[]</c> as big-endian (reverses in place before sending).</summary>
        private void WriteBigEndian(uint address, byte[] hostBytes)
        {
            Array.Reverse(hostBytes);
            SetMemory(address, hostBytes);
        }

        /// <summary>Read an array of <c>count</c> scalars by issuing one <c>getmemex</c> and byte-swapping in-place.</summary>
        private byte[] ReadBigEndianBlock(uint address, uint count, int elementSize)
        {
            var b = GetMemory(address, count * (uint)elementSize);
            XboxExtensions.ReverseBytes(b, elementSize);
            return b;
        }

        /// <summary>Write an array of scalars by packing each into <paramref name="elementSize"/> bytes then byte-swapping the block.</summary>
        private void WriteBigEndianBlock<T>(uint address, T[] values, int elementSize, Action<T, byte[], int> packer)
        {
            if (values == null) return;
            var b = new byte[values.Length * elementSize];
            for (int i = 0; i < values.Length; i++) packer(values[i], b, i * elementSize);
            XboxExtensions.ReverseBytes(b, elementSize);
            SetMemory(address, b);
        }

        #endregion

        #region Bytes / SBytes / Bool (1-byte scalars; no swap needed)

        public byte ReadByte(uint address) => GetMemory(address, 1)[0];
        public void WriteByte(uint address, byte value) => SetMemory(address, new[] { value });

        public sbyte ReadSByte(uint address) => (sbyte)GetMemory(address, 1)[0];
        public void WriteSByte(uint address, sbyte value) => SetMemory(address, new[] { (byte)value });

        public void WriteSBytes(uint address, sbyte[] values)
        {
            if (values == null) return;
            var bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++) bytes[i] = (byte)values[i];
            SetMemory(address, bytes);
        }

        public bool ReadBool(uint address) => GetMemory(address, 1)[0] != 0;
        public void WriteBool(uint address, bool value) => SetMemory(address, new byte[] { (byte)(value ? 1 : 0) });

        public void WriteBools(uint address, bool[] values)
        {
            if (values == null) return;
            var bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++) bytes[i] = (byte)(values[i] ? 1 : 0);
            SetMemory(address, bytes);
        }

        #endregion

        #region Int16 / UInt16 / Int32 / UInt32 / Int64 / UInt64 (BE)

        public short ReadInt16(uint address) => BitConverter.ToInt16(ReadBigEndian(address, 2), 0);
        public void WriteInt16(uint address, short value) => WriteBigEndian(address, BitConverter.GetBytes(value));

        public short[] GetInt16(uint address, uint count)
        {
            var b = ReadBigEndianBlock(address, count, 2);
            var arr = new short[count];
            for (int i = 0; i < count; i++) arr[i] = BitConverter.ToInt16(b, i * 2);
            return arr;
        }
        public void SetInt16(uint address, short[] values)
            => WriteBigEndianBlock(address, values, 2, (v, b, off) => BitConverter.GetBytes(v).CopyTo(b, off));

        public ushort ReadUInt16(uint address) => BitConverter.ToUInt16(ReadBigEndian(address, 2), 0);
        public void WriteUInt16(uint address, ushort value) => WriteBigEndian(address, BitConverter.GetBytes(value));

        public ushort[] GetUInt16(uint address, uint count)
        {
            var b = ReadBigEndianBlock(address, count, 2);
            var arr = new ushort[count];
            for (int i = 0; i < count; i++) arr[i] = BitConverter.ToUInt16(b, i * 2);
            return arr;
        }
        public void SetUInt16(uint address, ushort[] values)
            => WriteBigEndianBlock(address, values, 2, (v, b, off) => BitConverter.GetBytes(v).CopyTo(b, off));

        public int ReadInt32(uint address) => BitConverter.ToInt32(ReadBigEndian(address, 4), 0);
        public void WriteInt32(uint address, int value) => WriteBigEndian(address, BitConverter.GetBytes(value));

        public int[] GetInt32(uint address, uint count)
        {
            var b = ReadBigEndianBlock(address, count, 4);
            var arr = new int[count];
            for (int i = 0; i < count; i++) arr[i] = BitConverter.ToInt32(b, i * 4);
            return arr;
        }
        public void SetInt32(uint address, int[] values)
            => WriteBigEndianBlock(address, values, 4, (v, b, off) => BitConverter.GetBytes(v).CopyTo(b, off));

        public uint ReadUInt32(uint address) => BitConverter.ToUInt32(ReadBigEndian(address, 4), 0);
        public void WriteUInt32(uint address, uint value) => WriteBigEndian(address, BitConverter.GetBytes(value));

        public uint[] GetUInt32(uint address, uint count)
        {
            var b = ReadBigEndianBlock(address, count, 4);
            var arr = new uint[count];
            for (int i = 0; i < count; i++) arr[i] = BitConverter.ToUInt32(b, i * 4);
            return arr;
        }
        public void SetUInt32(uint address, uint[] values)
            => WriteBigEndianBlock(address, values, 4, (v, b, off) => BitConverter.GetBytes(v).CopyTo(b, off));

        public long ReadInt64(uint address) => BitConverter.ToInt64(ReadBigEndian(address, 8), 0);
        public void WriteInt64(uint address, long value) => WriteBigEndian(address, BitConverter.GetBytes(value));

        public long[] GetInt64(uint address, uint count)
        {
            var b = ReadBigEndianBlock(address, count, 8);
            var arr = new long[count];
            for (int i = 0; i < count; i++) arr[i] = BitConverter.ToInt64(b, i * 8);
            return arr;
        }
        public void SetInt64(uint address, long[] values)
            => WriteBigEndianBlock(address, values, 8, (v, b, off) => BitConverter.GetBytes(v).CopyTo(b, off));

        public ulong ReadUInt64(uint address) => BitConverter.ToUInt64(ReadBigEndian(address, 8), 0);
        public void WriteUInt64(uint address, ulong value) => WriteBigEndian(address, BitConverter.GetBytes(value));

        public ulong[] GetUInt64(uint address, uint count)
        {
            var b = ReadBigEndianBlock(address, count, 8);
            var arr = new ulong[count];
            for (int i = 0; i < count; i++) arr[i] = BitConverter.ToUInt64(b, i * 8);
            return arr;
        }
        public void SetUInt64(uint address, ulong[] values)
            => WriteBigEndianBlock(address, values, 8, (v, b, off) => BitConverter.GetBytes(v).CopyTo(b, off));

        #endregion

        #region Float / Double (BE)

        public float ReadFloat(uint address) => BitConverter.ToSingle(ReadBigEndian(address, 4), 0);
        public void WriteFloat(uint address, float value) => WriteBigEndian(address, BitConverter.GetBytes(value));

        public float[] GetFloat(uint address, uint count)
        {
            var b = ReadBigEndianBlock(address, count, 4);
            var arr = new float[count];
            for (int i = 0; i < count; i++) arr[i] = BitConverter.ToSingle(b, i * 4);
            return arr;
        }
        public void SetFloat(uint address, float[] values)
            => WriteBigEndianBlock(address, values, 4, (v, b, off) => BitConverter.GetBytes(v).CopyTo(b, off));

        public double ReadDouble(uint address) => BitConverter.ToDouble(ReadBigEndian(address, 8), 0);
        public void WriteDouble(uint address, double value) => WriteBigEndian(address, BitConverter.GetBytes(value));

        #endregion

        #region String

        /// <summary>Read a NUL-terminated UTF-8 string up to <paramref name="maxBytes"/> bytes.</summary>
        public string ReadString(uint address, int maxBytes = 0x100)
        {
            var raw = GetMemory(address, (uint)maxBytes);
            int n = Array.IndexOf(raw, (byte)0);
            return Encoding.UTF8.GetString(raw, 0, n < 0 ? raw.Length : n);
        }

        /// <summary>Write an ASCII string + NUL terminator.</summary>
        public void WriteString(uint address, string value)
        {
            value = value ?? string.Empty;
            var bytes = new byte[value.Length + 1];
            for (int i = 0; i < value.Length; i++) bytes[i] = (byte)value[i];
            bytes[value.Length] = 0;
            SetMemory(address, bytes);
        }

        /// <summary>Write a NUL-terminated wide string (UTF-16 LE-style, what Xam expects).</summary>
        public void WriteWString(uint address, string value) => SetMemory(address, XboxExtensions.WCHAR(value));

        #endregion

        #region Vector helpers

        public void WriteVector1(uint address, Vector v) => WriteFloat(address, v.X);

        public void WriteVector2(uint address, Vector v)
            => SetFloat(address, new[] { v.X, v.Y });

        public void WriteVector3(uint address, Vector v)
            => SetFloat(address, new[] { v.X, v.Y, v.Z });

        #endregion

        #region Hook helper (PowerPC absolute branch)

        /// <summary>
        /// Writes a 4-instruction (16 byte) absolute branch hook at
        /// <paramref name="address"/> that jumps to <paramref name="destination"/>.
        /// Set <paramref name="linked"/> to use <c>bctrl</c> (preserves LR).
        /// </summary>
        public void WriteHook(uint address, uint destination, bool linked)
        {
            uint hi = (destination & 0x8000) != 0
                ? ((destination >> 16) & 0xFFFF) + 1
                : ((destination >> 16) & 0xFFFF);
            var instr = new uint[]
            {
                0x3D600000u + hi,                  // lis     r11, hi
                0x396B0000u + (destination & 0xFFFF), // addi    r11, r11, lo
                0x7D6903A6u,                       // mtctr   r11
                linked ? 0x4E800421u : 0x4E800420u // bctr[l]
            };
            SetUInt32(address, instr);
        }

        #endregion

        #region Atomic-style XOR / AND / OR helpers

        public void XOR_UInt16(uint addr, ushort v) => WriteUInt16(addr, (ushort)(ReadUInt16(addr) ^ v));
        public void XOR_UInt32(uint addr, uint v) => WriteUInt32(addr, ReadUInt32(addr) ^ v);
        public void XOR_UInt64(uint addr, ulong v) => WriteUInt64(addr, ReadUInt64(addr) ^ v);
        public void XOR_Int16(uint addr, short v) => WriteInt16(addr, (short)(ReadInt16(addr) ^ v));
        public void XOR_Int32(uint addr, int v) => WriteInt32(addr, ReadInt32(addr) ^ v);
        public void XOR_Int64(uint addr, long v) => WriteInt64(addr, ReadInt64(addr) ^ v);

        public void AND_UInt16(uint addr, ushort v) => WriteUInt16(addr, (ushort)(ReadUInt16(addr) & v));
        public void AND_UInt32(uint addr, uint v) => WriteUInt32(addr, ReadUInt32(addr) & v);
        public void AND_UInt64(uint addr, ulong v) => WriteUInt64(addr, ReadUInt64(addr) & v);
        public void AND_Int16(uint addr, short v) => WriteInt16(addr, (short)(ReadInt16(addr) & v));
        public void AND_Int32(uint addr, int v) => WriteInt32(addr, ReadInt32(addr) & v);
        public void AND_Int64(uint addr, long v) => WriteInt64(addr, ReadInt64(addr) & v);

        public void OR_UInt16(uint addr, ushort v) => WriteUInt16(addr, (ushort)(ReadUInt16(addr) | v));
        public void OR_UInt32(uint addr, uint v) => WriteUInt32(addr, ReadUInt32(addr) | v);
        public void OR_UInt64(uint addr, ulong v) => WriteUInt64(addr, ReadUInt64(addr) | v);
        public void OR_Int16(uint addr, short v) => WriteInt16(addr, (short)(ReadInt16(addr) | v));
        public void OR_Int32(uint addr, int v) => WriteInt32(addr, ReadInt32(addr) | v);
        public void OR_Int64(uint addr, long v) => WriteInt64(addr, ReadInt64(addr) | v);

        #endregion


        #region Wait helpers (proxy to XboxClient)

        public void Wait(int targetLength) => Client.Wait(targetLength);
        public void Wait(WaitType type) => Client.Wait(type);
        public bool Ping(int waitTimeMs = 250) => Client.Ping(waitTimeMs);

        /// <summary>Sleeps for <paramref name="ms"/> milliseconds.</summary>
        public static bool Delay(int ms) { Thread.Sleep(ms); return true; }

        #endregion
    }
