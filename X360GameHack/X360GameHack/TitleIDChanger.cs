using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using X360GameHack;

namespace X360GameHack
{
    class TitleIDChanger
    {
        private const int _sizeOffset = 23;
        private const int _tidOffset = 12;
        private const int _headerSize = 8;
        private const int _blockSize = 15;
        private string _xexFile;
        private int _size;
        private int _offset;
        private bool _isTid;
        public byte[] bytes;    
        private OpenFileDialog openFileDialog1;
        public string titleID = "";
        public string mediaID = "";
        Invoker invoker = new Invoker();

        public void GetTitleIDandMediaID(string path)
        {           
            titleID = "";
            mediaID = "";
            _isTid = false;
            using (BinaryReader binaryReader = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)))
            {
                bytes = binaryReader.ReadBytes(4);

                if (Encoding.ASCII.GetString(bytes) != "XEX2")
                {
                    MessageBox.Show("This only supports XEX2 files!");
                    return;
                }
                binaryReader.BaseStream.Seek(23L, SeekOrigin.Begin);
                _size = binaryReader.ReadByte();
                for (int i = 0; i < _size; i++)
                {
                    int num = 24 + 8 * i;
                    binaryReader.BaseStream.Seek(num, SeekOrigin.Begin);
                    byte[] array = binaryReader.ReadBytes(4);
                    if (array[0] == 0 && array[1] == 4 && array[2] == 0 && array[3] == 6)
                    {
                        _isTid = true;
                        _offset = Swap(BitConverter.ToUInt32(binaryReader.ReadBytes(4), 0));
                        binaryReader.BaseStream.Seek(_offset, SeekOrigin.Begin);
                        byte[] array2 = binaryReader.ReadBytes(4);
                        foreach (byte b in array2)
                        {
                            mediaID += $"{b:X2}";
                        }
                        binaryReader.BaseStream.Seek(8L, SeekOrigin.Current);
                        array2 = binaryReader.ReadBytes(4);
                        foreach (byte b2 in array2)
                        {
                            titleID += $"{b2:X2}";
                        }
                    }
                }
                if (!_isTid)
                {
                    MessageBox.Show("Can`t find TitleID!");
                    titleID = "00000000";
                    mediaID = "00000000";
                }
                _xexFile = path;
            }
        }
        public void addID_Click() //not used
        {
            if (!Regex.IsMatch(titleID, "\\A\\b[0-9a-fA-F]+\\b\\Z") || !Regex.IsMatch(mediaID, "\\A\\b[0-9a-fA-F]+\\b\\Z"))
            {
                MessageBox.Show("IDs must be in hex");
                return;
            }
            bool flag = true;
            int num;
            int num2;
            using (BinaryReader binaryReader = new BinaryReader(File.Open(_xexFile, FileMode.Open, FileAccess.Read, FileShare.None)))
            {
                num = 24 + _size * 8;
                binaryReader.BaseStream.Seek(num, SeekOrigin.Begin);
                if (BitConverter.ToUInt64(binaryReader.ReadBytes(8), 0) != 0L)
                {
                    flag = false;
                }
                binaryReader.BaseStream.Seek(28 + (_size - 1) * 8, SeekOrigin.Begin);
                num2 = Swap(BitConverter.ToUInt32(binaryReader.ReadBytes(4), 0)) + 15 + 1;
                binaryReader.BaseStream.Seek(num2, SeekOrigin.Begin);
                ulong num3 = BitConverter.ToUInt64(binaryReader.ReadBytes(8), 0);
                ulong num4 = BitConverter.ToUInt64(binaryReader.ReadBytes(8), 0);
                if (num3 != 0L || num4 != 0L)
                {
                    flag = false;
                }
            }
            if (!flag)
            {
                MessageBox.Show("Can`t add titleid");
                return;
            }
            using (BinaryWriter binaryWriter = new BinaryWriter(File.Open(_xexFile, FileMode.Open, FileAccess.Write, FileShare.None)))
            {
            binaryWriter.Seek(23, SeekOrigin.Begin);
            binaryWriter.Write(_size + 1);
            binaryWriter.Seek(num, SeekOrigin.Begin);
            byte[] buffer = new byte[4] { 0, 4, 0, 6 };
            binaryWriter.Write(buffer);
            binaryWriter.Write(Swap((uint)num2));
            _offset = num2;
            binaryWriter.Seek(_offset, SeekOrigin.Begin);
            binaryWriter.Write(StringToByteArray(mediaID));
            binaryWriter.Seek(_offset + 12, SeekOrigin.Begin);
            binaryWriter.Write(StringToByteArray(titleID));
            MessageBox.Show("Done!");
           }
        }

        public void ChangeTitleIDandMediaID(string path)
        {
            if (!Regex.IsMatch(titleID, "\\A\\b[0-9a-fA-F]+\\b\\Z") || !Regex.IsMatch(mediaID, "\\A\\b[0-9a-fA-F]+\\b\\Z"))
            {
                MessageBox.Show("IDs must be in hex");
                return;
            }
            using (BinaryWriter binaryWriter = new BinaryWriter(File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None)))
            {
                binaryWriter.Seek(_offset, SeekOrigin.Begin);
                binaryWriter.Write(StringToByteArray(mediaID));
                binaryWriter.Seek(_offset + 12, SeekOrigin.Begin);
                binaryWriter.Write(StringToByteArray(titleID));
                MessageBox.Show("Done!");
            }
        }

        public static int Swap(uint value)
        {
            return (int)(((value & 0xFF) << 24) | ((value & 0xFF00) << 8) | ((value & 0xFF0000) >> 8) | ((value & 0xFF000000u) >> 24));
        }

        public static byte[] StringToByteArray(string hex)
        {
            while (hex.Length < 8)
            {
                hex = "0" + hex;
            }
            return (from x in Enumerable.Range(0, hex.Length)
                    where x % 2 == 0
                    select Convert.ToByte(hex.Substring(x, 2), 16)).ToArray();
        }
    }
}
