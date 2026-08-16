using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack
{
    class XBEPatches
    {

        public bool PatchXBERam(string Filepath, string RamSpeed)
        {
            if (File.Exists(Filepath))
            {
                try
                {
                    if (RamSpeed == "64" || RamSpeed == "Stock")
                    {
                        FileStream fileStream = new FileStream(Filepath, FileMode.Open, FileAccess.ReadWrite)
                        {
                            Position = 292L
                        };
                        byte b = (byte)fileStream.ReadByte();
                        fileStream.Position--;
                        byte b2 = 4;
                        byte b3 = (byte)(b | b2);
                        fileStream.WriteByte(b3);
                        fileStream.Close();
                        if (b != b3)
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else if (RamSpeed == "128")
                    {
                        FileStream fileStream = new FileStream(Filepath, FileMode.Open, FileAccess.ReadWrite)
                        {
                            Position = 292L
                        };
                        byte b = (byte)fileStream.ReadByte();
                        fileStream.Position--;
                        byte b2 = 251;
                        byte b3 = (byte)(b & b2);
                        fileStream.WriteByte(b3);
                        fileStream.Close();
                        if (b != b3)
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (!RamSpeed.Contains("64") || !RamSpeed.Contains("Stock") || !RamSpeed.Contains("128"))
                        {
                            MessageBox.Show("Incorrect Ram Speed Detected!", "X360GameHack Info!");
                            return false;
                        }
                        else
                        {
                            MessageBox.Show("XBE Patches Class error!", "X360GameHack Info!");
                            return false;
                        }
                    }
                }

                catch (Exception ex)
                {
                    MessageBox.Show("An error happened patching the file!\n\n" + ex, "An exception happened!");
                    return false;
                }
            
            }
            else
            {

                MessageBox.Show("The file does not exist at: /n/n" + Filepath);
                return false;
            }
        }       

        //
        //
        //
        //
        //  CPU PATCHES BELOW
        //
        //
        //
        //



        public bool PatchXBECPUScale(string Filepath, int CPUScale)
        {
            CPUScale *= 1000000;
            byte[] bytes = BitConverter.GetBytes(CPUScale);
            byte[] bytes2 = BitConverter.GetBytes(733333333);
            FileStream fileStream2 = new FileStream(Filepath, FileMode.Open, FileAccess.ReadWrite);
            bool flag = false;
            int num2 = 0;
            do
            {
                num2 = fileStream2.ReadByte();
                if (num2 != -1)
                {
                    bool flag2 = true;
                    for (int j = 0; j < bytes2.Length; j++)
                    {
                        if ((byte)num2 != bytes2[j])
                        {
                            flag2 = false;
                            break;
                        }
                        num2 = fileStream2.ReadByte();
                        if (num2 == -1)
                        {
                            flag2 = false;
                            break;
                        }
                    }
                    if (flag2)
                    {
                        fileStream2.Position -= 5L;
                        for (int j = 0; j < bytes2.Length; j++)
                        {
                            fileStream2.WriteByte(bytes[j]);
                        }
                        flag = true;
                    }
                }
            }
            while (num2 != -1);
            fileStream2.Close();
            if (flag)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }

}
