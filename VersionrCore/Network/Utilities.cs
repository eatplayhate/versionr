using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Network
{
    class Utilities
    {
        [ProtoBuf.ProtoContract]
        public class Packet
        {
            [ProtoBuf.ProtoMember(1)]
            public byte[] Data { get; set; }
            [ProtoBuf.ProtoMember(2)]
            public int PayloadSize { get; set; }
            [ProtoBuf.ProtoMember(3)]
            public int? DecompressedSize { get; set; }
            [ProtoBuf.ProtoMember(4)]
            public uint Hash { get; set; }
        }
        public static uint ComputeAdler32(byte[] array)
        {
            uint checksum = 1;
            int n;
            uint s1 = checksum & 0xFFFF;
            uint s2 = checksum >> 16;

            int size = array.Length;
            int index = 0;
            while (size > 0)
            {
                n = (3800 > size) ? size : 3800;
                size -= n;

                while (--n >= 0)
                {
                    s1 = s1 + (uint)(array[index++] & 0xFF);
                    s2 = s2 + s1;
                }

                s1 %= 65521;
                s2 %= 65521;
            }

            checksum = (s2 << 16) | s1;

            return checksum;
        }
        internal static Dictionary<Type, bool> s_Compressible = new Dictionary<Type, bool>();
        internal static bool IsCompressible(Type t)
        {
            lock (s_Compressible)
            {
                bool result;
                if (!s_Compressible.TryGetValue(t, out result))
                {
                    result = t.GetCustomAttributes(typeof(CompressibleAttribute), true).Length > 0;
                    s_Compressible[t] = result;
                }
                return result;
            }
        }
        public static void SendEncrypted<T>(SharedNetwork.SharedNetworkInfo info, T argument)
        {
            byte[] result;
            using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream())
            {
                ProtoBuf.Serializer.Serialize<T>(memoryStream, argument);
                result = memoryStream.ToArray();
            }

            uint checksum = ComputeAdler32(result);
            int? decompressedSize = null;
            if (true)
            {
                Versionr.Utilities.LZHL.ResetCompressor(info.LZHLCompressor);
                byte[] compressedBuffer = new byte[result.Length * 2];
                uint resultSize = Versionr.Utilities.LZHL.Compress(info.LZHLCompressor, result, (uint)result.Length, compressedBuffer);
                if (resultSize < result.Length)
                {
                    decompressedSize = result.Length;
                    Array.Resize(ref compressedBuffer, (int)resultSize);
                    result = compressedBuffer;
                }
            }

            int payload = result.Length;
            using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream())
            {
                using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(memoryStream, info.Encryptor, System.Security.Cryptography.CryptoStreamMode.Write))
                {
                    cs.Write(result, 0, result.Length);
                }
                result = memoryStream.ToArray();
            }
            
            Packet packet = new Packet()
            {
                DecompressedSize = decompressedSize,
                Data = result,
                PayloadSize = payload,
                Hash = checksum,
            };

            ProtoBuf.Serializer.SerializeWithLengthPrefix<Packet>(info.Stream, packet, ProtoBuf.PrefixStyle.Fixed32);
        }
        public static T ReceiveEncrypted<T>(SharedNetwork.SharedNetworkInfo info)
        {
            Packet packet = ProtoBuf.Serializer.DeserializeWithLengthPrefix<Packet>(info.Stream, ProtoBuf.PrefixStyle.Fixed32);
            Printer.PrintDiagnostics("Received {0} byte encrypted packet.", packet.Data.Length);

            byte[] decryptedData = new byte[packet.PayloadSize];
            using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream(packet.Data))
            using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(memoryStream, info.Decryptor, System.Security.Cryptography.CryptoStreamMode.Read))
            {
                cs.Read(decryptedData, 0, decryptedData.Length);
            }
            
            if (packet.DecompressedSize.HasValue)
            {
                Versionr.Utilities.LZHL.ResetDecompressor(info.LZHLDecompressor);
                byte[] result = new byte[packet.DecompressedSize.Value];
                Versionr.Utilities.LZHL.Decompress(info.LZHLDecompressor, decryptedData, (uint)packet.PayloadSize, result, (uint)packet.DecompressedSize.Value);
                decryptedData = result;
                Printer.PrintDiagnostics(" - {0} bytes decompressed", packet.DecompressedSize.Value);
            }

            uint checksum = ComputeAdler32(decryptedData);
            if (checksum != packet.Hash)
                throw new Exception("Data did not survive the trip!");

            using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream(decryptedData))
            {
                return ProtoBuf.Serializer.Deserialize<T>(memoryStream);
            }
        }
    }
}
