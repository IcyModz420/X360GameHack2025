using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using X360GameHack.Properties;

namespace X360GameHack
{
    internal class GOD2ISO
    {
        internal static byte[] XSFHeader
        {
            get
            {
                object obj = ResourceManager.GetObject("XSFHeader", resourceCulture);
                return ((byte[])(obj));
            }
        }
        public class MyState
        {
            public int file;
            public int files;
            public MyState(int file, int files)
            {
                this.file = file;
                this.files = files;
            }
        }

        public void FixSectorOffsets(FileStream iso, string godPath)
        {

            int sector, size, offset;
            byte[] buffer;
            Queue<DirEntry> directories = new Queue<DirEntry>();

            buffer = File.ReadAllBytes(godPath);
            // offset type?
            if ((buffer[0x391] & 0x40) != 0x40) return;
            // calculate the offset
            offset = BitConverter.ToInt32(buffer, 0x395);
            if (offset == 0) return;
            offset *= 2;
            offset -= 34;

            buffer = new byte[4];
            iso.Position = 0x10014;
            iso.Read(buffer, 0, 4);
            sector = BitConverter.ToInt32(buffer, 0);
            if (sector > 0)
            {
                sector -= offset;
                byte[] corrected = BitConverter.GetBytes(sector);
                iso.Position -= 4;
                iso.Write(corrected, 0, 4);
                iso.Read(buffer, 0, 4);
                size = BitConverter.ToInt32(buffer, 0);
                directories.Enqueue(new DirEntry(sector, size));
            }

            while (directories.Count > 0)
            {

                DirEntry dirEntry = directories.Dequeue();
                iso.Position = dirEntry.StartPos();

                while ((iso.Position + 4) < dirEntry.EndPos())
                {
                    // crossed a sector boundary?
                    if ((iso.Position + 4) / 2048L > iso.Position / 2048L)
                    {
                        iso.Position += 2048L - (iso.Position % 2048L);
                    }
                    // read subtrees
                    iso.Read(buffer, 0, 4);
                    if (buffer[0] == 0xff && buffer[1] == 0xff && buffer[2] == 0xff && buffer[3] == 0xff)
                    {
                        // another sector to process?
                        if (dirEntry.EndPos() - iso.Position > 2048)
                        {
                            iso.Position += 2048L - (iso.Position % 2048L);
                            continue;
                        }
                        break;
                    }

                    // read sector
                    iso.Read(buffer, 0, 4);
                    sector = BitConverter.ToInt32(buffer, 0);
                    if (sector > 0)
                    {
                        sector -= offset;
                        byte[] corrected = BitConverter.GetBytes(sector);
                        iso.Position -= 4;
                        iso.Write(corrected, 0, 4);
                    }

                    // get size
                    iso.Read(buffer, 0, 4);
                    size = BitConverter.ToInt32(buffer, 0);

                    // get attributes
                    iso.Read(buffer, 0, 1);

                    // if directory add to list of tables to process
                    if ((buffer[0] & 0x10) == 0x10) directories.Enqueue(new DirEntry(sector, size));

                    // get filename length
                    iso.Read(buffer, 0, 1);
                    // skip it
                    iso.Position += buffer[0];

                    // skip padding
                    if ((14 + buffer[0]) % 4 > 0) iso.Position += 4 - ((14 + buffer[0]) % 4);
                }
            }
        }

        public void FixCreateIsoGoodHeader(FileStream iso)
        {
            byte[] bytes = new byte[8];
            iso.Position = 8;
            iso.Read(bytes, 0, 8);
            if (BitConverter.ToInt64(bytes, 0) == 2587648L)
            {
                iso.Position = 0;
                iso.Write(XSFHeader, 0, XSFHeader.Length);
                FixXFSHeader(iso);
            }
        }

        public void FixXFSHeader(FileStream iso)
        {
            byte[] bytes;

            iso.Position = 8;
            bytes = BitConverter.GetBytes(iso.Length - 0x400);
            iso.Write(bytes, 0, bytes.Length);

            iso.Position = 0x8050;
            bytes = BitConverter.GetBytes((uint)(iso.Length / 2048));
            iso.Write(bytes, 0, bytes.Length);
            for (int i = bytes.Length - 1; i >= 0; i--)
            {
                iso.WriteByte(bytes[i]);
            }

            iso.Position = 0x7a69;
            bytes = Encoding.ASCII.GetBytes(GetAppName());
            iso.Write(bytes, 0, bytes.Length);
        }

        public bool HasXSFHeader(string file)
        {
            byte[] buff = new byte[3];

            FileStream data = new FileStream(file, FileMode.Open, FileAccess.Read);
            data.Position = 0x2000;
            data.Read(buff, 0, buff.Length);
            data.Close();

            if (buff[0] != 0x58) return false;
            if (buff[1] != 0x53) return false;
            if (buff[2] != 0x46) return false;

            return true;
        }

        public string FormatSize(ulong byteCount)
        {
            string size;
            if (byteCount >= 1073741824)
            {
                size = String.Format("{0:##.##}", byteCount / 1073741824.0) + " gb";
            }
            else if (byteCount >= 1048576)
            {
                size = String.Format("{0:##.##}", byteCount / 1048576.0) + " mb";
            }
            else if (byteCount >= 1024)
            {
                size = String.Format("{0:##.##}", byteCount / 1024.0) + " kb";
            }
            else
            {
                size = byteCount + " b";
            }
            return size;
        }

        public string GetAppName()
        {
            string s = "God2Iso v" + Assembly.GetExecutingAssembly().GetName().Version.ToString();
            return s.Substring(0, s.Length - 2);
        }

        public static global::System.Globalization.CultureInfo Culture
        {
            get
            {
                return resourceCulture;
            }
            set
            {
                resourceCulture = value;
            }
        }

        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager
        {
            get
            {
                if (object.ReferenceEquals(resourceMan, null))
                {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("God2Iso.Properties.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }

        public static global::System.Resources.ResourceManager resourceMan;

        public static global::System.Globalization.CultureInfo resourceCulture;
    }
    public class DirEntry
    {
        public int sector;
        public int length;
        public DirEntry(int sector, int length)
        {
            this.sector = sector;
            this.length = length;
        }
        public long StartPos()
        {
            return sector * 2048L;
        }
        public long EndPos()
        {
            return (sector * 2048L) + length;
        }
    }
}

