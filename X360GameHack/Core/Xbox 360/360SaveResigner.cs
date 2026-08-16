//using Isolib.Algorithms;
//using Isolib.Encryptions;
//using Isolib.Functions;
//using Isolib.IOPackage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace X360GameHack.Core.Xbox_360
{
    public class _360SaveResigner
    {
        /*private void WriteHeader(RsaParam param)
        {
            try
            {
                RWStream stream = new RWStream();
                _headerData.ContentSize = _mainIo.Length - _structure.BaseBlock;
                _headerData.Write(ref stream);
                stream.Position = 832L;
                stream.WriteInt32((_structure.Type == StructureType.Type0) ? 44302 : 38682);
                stream.Position = stream.Length;
                stream.WriteBytes(new byte[2278 + (_structure.BaseBlock - 40960)]);
                stream.Position = 889L;
                WriteDescriptor(ref stream);
                WriteHash(read: (_structure.BlockCount <= 170) ? GenerateBaseOffset(0u, TreeLevel.L0) : ((_structure.BlockCount > 28906) ? GenerateBaseOffset(0u, TreeLevel.L2) : GenerateBaseOffset(0u, TreeLevel.L1)), input: ref _mainIo, write: 897L, size: 4096, output: ref stream);
                int size = ((_structure.BaseBlock == 40960) ? 40124 : 44220);
                WriteHash(836L, 812L, size, ref stream);
                stream.Position = 556L;
                byte[] array = Sha1.Compute(stream.ReadBytes(280));
                stream.Position = 4L;
                if (_headerData.SignatureHeaderType == SignatureType.Con)
                {
                    stream.WriteBytes(param.Certificate);
                    RSACryptoServiceProvider rSACryptoServiceProvider = new RSACryptoServiceProvider();
                    RSAPKCS1SignatureFormatter rSAPKCS1SignatureFormatter = new RSAPKCS1SignatureFormatter();
                    rSACryptoServiceProvider.ImportParameters(param.ParametersRsaKeys);
                    rSAPKCS1SignatureFormatter.SetHashAlgorithm("SHA1");
                    rSAPKCS1SignatureFormatter.SetKey(rSACryptoServiceProvider);
                    rSAPKCS1SignatureFormatter.CreateSignature(array);
                    stream.WriteBytes(Isolib.Functions.Functions.SimpleScramble(Rsa.GeneratePks1Signature(param.ParametersRsaKeys, array), reverse: true));
                }
                else
                {
                    stream.WriteBytes(Isolib.Functions.Functions.ReverseByteArray(Rsa.GeneratePks1Signature(param.ParametersRsaKeys, array)));
                    stream.WriteBytes(new byte[296]);
                }

                stream.IsBigEndian = true;
                stream.Flush();
                _mainIo.Position = 0L;
                _mainIo.WriteBytes(stream.ReadStream());
                _mainIo.Flush();
                stream.Close();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        private void WriteTables()
        {
            try
            {
                for (uint num = 0u; num < _structure.BlockCount; num++)
                {
                    WriteHash(GenerateDataOffset(num), GenerateHashOffset(num, TreeLevel.L0), 4096, ref _mainIo);
                }

                if (_structure.BlockCount > 170)
                {
                    uint num2 = (_structure.BlockCount - 1) / 170 + 1;
                    for (uint num = 0u; num < num2; num++)
                    {
                        WriteHash(GenerateBaseOffset(num * 170, TreeLevel.L0), GenerateHashOffset(num * 170, TreeLevel.L1), 4096, ref _mainIo);
                    }

                    if (_structure.BlockCount > 28906)
                    {
                        num2 = (_structure.BlockCount - 1) / 28906 + 1;
                        for (uint num = 0u; num < num2; num++)
                        {
                            WriteHash(GenerateHashOffset(num * 28906, TreeLevel.L1), GenerateHashOffset(num * 28906, TreeLevel.L2), 4096, ref _mainIo);
                        }
                    }
                }

                _mainIo.Flush();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public void Finish()
        {
            try
            {
                RsaParam param = new RsaParam();
                WriteTables();
                WriteHeader(param);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }*/
    }
}
