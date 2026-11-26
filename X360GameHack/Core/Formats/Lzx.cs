using System;
using System.IO;

namespace X360GameHack.Core.Formats
{
    /// <summary>
    /// LZX decompression as used by compressed XEX files (the same LZX as Microsoft cabinets): 32 KB frames, a sliding
    /// window of 32 KB to 2 MB, verbatim / aligned-offset / uncompressed blocks, three repeated offsets. Written from
    /// the format; proven by comparing every decoded base file with xextool's (see the test harness).
    /// Every malformed input ends in an <see cref="InvalidDataException"/> - nothing is guessed.
    /// </summary>
    internal sealed class LzxDecoder
    {
        private const int FrameSize = 0x8000, NumChars = 256, PretreeSymbols = 20, AlignedSymbols = 8, LengthSymbols = 249, NumPrimaryLengths = 7, MinMatch = 2;
        private const int Verbatim = 1, Aligned = 2, Uncompressed = 3;

        private static readonly int[] ExtraBits = new int[51];
        private static readonly uint[] PositionBase = new uint[51];

        static LzxDecoder()
        {
            for (int i = 0, bits = 0; i < 51; i += 2)
            {
                ExtraBits[i] = bits;
                if (i + 1 < 51) ExtraBits[i + 1] = bits;
                if (i != 0 && bits < 17) bits++;
            }
            uint position = 0;
            for (int i = 0; i < 51; i++) { PositionBase[i] = position; position += 1u << ExtraBits[i]; }
        }

        /// <summary>A canonical Huffman code: codes are handed out by rising length, then by rising symbol.</summary>
        private sealed class Tree
        {
            public readonly byte[] Lengths;
            private ushort[] _table = new ushort[0];
            private int _bits;
            public bool Empty { get; private set; } = true;

            public Tree(int symbols) { Lengths = new byte[symbols]; }

            public void Build()
            {
                int max = 0;
                foreach (byte l in Lengths) if (l > max) max = l;
                Empty = max == 0;
                _bits = max;
                if (Empty) return;
                if (max > 16) throw new InvalidDataException("LZX: a code is longer than 16 bits.");
                int size = 1 << max;
                if (_table.Length != size) _table = new ushort[size];
                int next = 0;
                for (int length = 1; length <= max; length++)
                {
                    int span = 1 << (max - length);
                    for (int symbol = 0; symbol < Lengths.Length; symbol++)
                    {
                        if (Lengths[symbol] != length) continue;
                        if (next + span > size) throw new InvalidDataException("LZX: a code table is over-subscribed.");
                        for (int i = 0; i < span; i++) _table[next + i] = (ushort)symbol;
                        next += span;
                    }
                }
                for (int i = next; i < size; i++) _table[i] = 0xFFFF;     // codes that do not exist
            }

            public int Decode(LzxDecoder d)
            {
                if (Empty) throw new InvalidDataException("LZX: a symbol was read from an empty code table.");
                d.Ensure(16);
                int symbol = _table[d._buffer >> (32 - _bits)];
                if (symbol == 0xFFFF) throw new InvalidDataException("LZX: a code that is not in its table was read.");
                d.Remove(Lengths[symbol]);
                return symbol;
            }
        }

        private readonly byte[] _in;
        private int _inPos;
        private uint _buffer;
        private int _bitsLeft;

        private readonly byte[] _window;
        private readonly int _windowSize, _positionSlots;
        private uint _r0 = 1, _r1 = 1, _r2 = 1;
        private readonly Tree _pretree = new Tree(PretreeSymbols), _aligned = new Tree(AlignedSymbols), _length = new Tree(LengthSymbols);
        private readonly Tree _main;

        private LzxDecoder(byte[] input, int windowBits)
        {
            if (windowBits < 15 || windowBits > 21) throw new InvalidDataException("LZX: the window size is not one this format allows.");
            _in = input;
            _windowSize = 1 << windowBits;
            _window = new byte[_windowSize];
            _positionSlots = windowBits == 21 ? 50 : windowBits == 20 ? 42 : windowBits * 2;
            _main = new Tree(NumChars + _positionSlots * 8);
        }

        // ---- bits: the stream is 16-bit little-endian words, read from the top bit down

        private void Ensure(int count)
        {
            while (_bitsLeft < count)
            {
                // past the end the stream reads as zeros (an encoder may stop mid-word); real overruns fail on the size check
                uint lo = _inPos < _in.Length ? _in[_inPos] : 0u, hi = _inPos + 1 < _in.Length ? _in[_inPos + 1] : 0u;
                if (_inPos > _in.Length + 8) throw new InvalidDataException("LZX: the compressed data ends too early.");
                _inPos += 2;
                _buffer |= (hi << 8 | lo) << (16 - _bitsLeft);
                _bitsLeft += 16;
            }
        }

        private void Remove(int count) { _buffer <<= count; _bitsLeft -= count; }

        private uint Read(int count)
        {
            if (count == 0) return 0;
            Ensure(count);
            uint value = _buffer >> (32 - count);
            Remove(count);
            return value;
        }

        private void ReadLengths(Tree tree, int first, int last)
        {
            for (int i = 0; i < PretreeSymbols; i++) _pretree.Lengths[i] = (byte)Read(4);
            _pretree.Build();
            for (int x = first; x < last;)
            {
                int z = _pretree.Decode(this);
                if (z == 17)
                {
                    int run = (int)Read(4) + 4;
                    if (x + run > last) throw new InvalidDataException("LZX: a run of code lengths passes the end of its table.");
                    while (run-- > 0) tree.Lengths[x++] = 0;
                }
                else if (z == 18)
                {
                    int run = (int)Read(5) + 20;
                    if (x + run > last) throw new InvalidDataException("LZX: a run of code lengths passes the end of its table.");
                    while (run-- > 0) tree.Lengths[x++] = 0;
                }
                else if (z == 19)
                {
                    int run = (int)Read(1) + 4;
                    if (x + run > last) throw new InvalidDataException("LZX: a run of code lengths passes the end of its table.");
                    z = _pretree.Decode(this);
                    if (z > 16) throw new InvalidDataException("LZX: a run of code lengths has no length.");
                    z = tree.Lengths[x] - z; if (z < 0) z += 17;
                    while (run-- > 0) tree.Lengths[x++] = (byte)z;
                }
                else
                {
                    z = tree.Lengths[x] - z; if (z < 0) z += 17;
                    tree.Lengths[x++] = (byte)z;
                }
            }
        }

        /// <summary>Decompresses <paramref name="input"/> into exactly <paramref name="outputLength"/> bytes.</summary>
        public static byte[] Decompress(byte[] input, int outputLength, int windowBits)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (outputLength < 0) throw new ArgumentOutOfRangeException(nameof(outputLength));
            return new LzxDecoder(input, windowBits).Run(outputLength);
        }

        private byte[] Run(int total)
        {
            var output = new byte[total];
            int produced = 0, windowPos = 0, blockType = 0, blockLength = 0, blockRemaining = 0;

            // The stream may ask for the "E8" step: before compressing, the encoder rewrote the 4 bytes after every
            // 0xE8 byte (an x86 call) from a relative into an absolute value; each output frame is turned back here.
            // It applies from the first block whose code table uses the byte 0xE8 (or the first raw block) onward.
            int translateSize = 0, frameIndex = 0;
            bool translate = false;
            if (total > 0 && Read(1) != 0)
            {
                uint size = Read(16) << 16; size |= Read(16);
                translateSize = (int)size;
            }

            while (produced < total)
            {
                int frame = Math.Min(FrameSize, total - produced), todo = frame, frameStart = windowPos;
                while (todo > 0)
                {
                    if (blockRemaining == 0)
                    {
                        if (blockType == Uncompressed && (blockLength & 1) != 0) _inPos++;       // odd-sized raw blocks are padded to a word
                        blockType = (int)Read(3);
                        blockLength = blockRemaining = (int)(Read(16) << 8 | Read(8));
                        if (blockLength == 0) throw new InvalidDataException("LZX: a block of zero length.");
                        switch (blockType)
                        {
                            case Aligned:
                                for (int i = 0; i < AlignedSymbols; i++) _aligned.Lengths[i] = (byte)Read(3);
                                _aligned.Build();
                                goto case Verbatim;
                            case Verbatim:
                                ReadLengths(_main, 0, NumChars);
                                ReadLengths(_main, NumChars, NumChars + _positionSlots * 8);
                                _main.Build();
                                if (_main.Lengths[0xE8] != 0) translate = true;
                                ReadLengths(_length, 0, LengthSymbols);
                                _length.Build();
                                break;
                            case Uncompressed:
                                Ensure(16);                              // 1 to 16 padding bits, then the three offsets as bytes
                                if (_bitsLeft > 16) _inPos -= 2;
                                _bitsLeft = 0; _buffer = 0;
                                _r0 = RawU32(); _r1 = RawU32(); _r2 = RawU32();
                                translate = true;
                                break;
                            default:
                                throw new InvalidDataException("LZX: unknown block type " + blockType + ".");
                        }
                    }

                    int run = Math.Min(blockRemaining, todo);
                    todo -= run;
                    blockRemaining -= run;

                    if (blockType == Uncompressed)
                    {
                        if (_inPos + run > _in.Length) throw new InvalidDataException("LZX: a raw block runs past the end of the data.");
                        if (windowPos + run > _windowSize) throw new InvalidDataException("LZX: a raw block runs past the window.");
                        Buffer.BlockCopy(_in, _inPos, _window, windowPos, run);
                        _inPos += run;
                        windowPos += run;
                        continue;
                    }

                    while (run > 0)
                    {
                        int symbol = _main.Decode(this);
                        if (symbol < NumChars)
                        {
                            if (windowPos >= _windowSize) throw new InvalidDataException("LZX: output runs past the window.");
                            _window[windowPos++] = (byte)symbol;
                            run--;
                            continue;
                        }
                        symbol -= NumChars;
                        int matchLength = symbol & NumPrimaryLengths;
                        if (matchLength == NumPrimaryLengths) matchLength += _length.Decode(this);
                        matchLength += MinMatch;

                        uint offset = (uint)(symbol >> 3);
                        if (offset > 2)
                        {
                            int extra = ExtraBits[offset];
                            if (blockType == Verbatim)
                            {
                                offset = offset == 3 ? 1 : PositionBase[offset] - 2 + Read(extra);
                            }
                            else
                            {
                                offset = PositionBase[offset] - 2;
                                if (extra > 3) { offset += Read(extra - 3) << 3; offset += (uint)_aligned.Decode(this); }
                                else if (extra == 3) offset += (uint)_aligned.Decode(this);
                                else if (extra > 0) offset += Read(extra);
                                else offset = 1;
                            }
                            _r2 = _r1; _r1 = _r0; _r0 = offset;
                        }
                        else if (offset == 0) offset = _r0;
                        else if (offset == 1) { offset = _r1; _r1 = _r0; _r0 = offset; }
                        else { offset = _r2; _r2 = _r0; _r0 = offset; }

                        if (offset == 0 || offset > (uint)_windowSize) throw new InvalidDataException("LZX: a match reaches outside the window.");
                        if (windowPos + matchLength > _windowSize) throw new InvalidDataException("LZX: a match runs past the window.");
                        int from = windowPos - (int)offset;
                        if (from < 0) from += _windowSize;
                        for (int i = 0; i < matchLength; i++)
                        {
                            _window[windowPos++] = _window[from++];
                            if (from == _windowSize) from = 0;
                        }
                        run -= matchLength;
                    }
                    if (run < 0)
                    {
                        // the last match went past this run: it belongs to the same block and the same frame
                        if (-run > blockRemaining) throw new InvalidDataException("LZX: a match runs past its block.");
                        blockRemaining += run;
                        todo += run;
                    }
                }

                if (windowPos - frameStart != frame) throw new InvalidDataException("LZX: a frame decoded to the wrong size.");
                Buffer.BlockCopy(_window, frameStart, output, produced, frame);
                if (translate && translateSize != 0 && frameIndex <= 32768 && frame > 10)
                {
                    int position = produced;
                    for (int p = produced, end = produced + frame - 10; p < end;)
                    {
                        if (output[p++] != 0xE8) { position++; continue; }
                        int absolute = output[p] | output[p + 1] << 8 | output[p + 2] << 16 | output[p + 3] << 24;
                        if (absolute >= -position && absolute < translateSize)
                        {
                            int relative = absolute >= 0 ? absolute - position : absolute + translateSize;
                            output[p] = (byte)relative; output[p + 1] = (byte)(relative >> 8); output[p + 2] = (byte)(relative >> 16); output[p + 3] = (byte)(relative >> 24);
                        }
                        p += 4;
                        position += 5;
                    }
                }
                frameIndex++;
                produced += frame;
                if (windowPos == _windowSize) windowPos = 0;

                // every frame starts on a 16-bit boundary
                if (_bitsLeft > 0) Ensure(16);
                if ((_bitsLeft & 15) != 0) Remove(_bitsLeft & 15);
            }
            return output;
        }

        private uint RawU32()
        {
            if (_inPos + 4 > _in.Length) throw new InvalidDataException("LZX: a raw block header runs past the end of the data.");
            uint v = (uint)(_in[_inPos] | _in[_inPos + 1] << 8 | _in[_inPos + 2] << 16 | _in[_inPos + 3] << 24);
            _inPos += 4;
            return v;
        }
    }
}
