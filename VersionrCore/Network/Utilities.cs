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
            public int? DecompressedSize { get; set; }
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
        public static void SendEncrypted<T>(System.Net.Sockets.NetworkStream stream, System.Security.Cryptography.ICryptoTransform encoder, T argument)
        {
            byte[] result;
            using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream())
            {
                using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(memoryStream, encoder, System.Security.Cryptography.CryptoStreamMode.Write))
                {
                    ProtoBuf.Serializer.Serialize<T>(cs, argument);
                }
                result = memoryStream.ToArray();
            }
            Packet packet = new Packet();
            if (result.Length > 10 * 1024 && IsCompressible(typeof(T)))
            {
                packet.DecompressedSize = result.Length;
                using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream())
                {
                    using (System.IO.Compression.GZipStream gzStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Compress))
                    {
                        gzStream.Write(result, 0, result.Length);
                    }
                    packet.Data = memoryStream.ToArray();
                }
            }
            else
                packet.Data = result;
            ProtoBuf.Serializer.SerializeWithLengthPrefix<Packet>(stream, packet, ProtoBuf.PrefixStyle.Fixed32);
        }
        public static T ReceiveEncrypted<T>(System.Net.Sockets.NetworkStream stream, System.Security.Cryptography.ICryptoTransform decoder)
        {
            Packet packet = ProtoBuf.Serializer.DeserializeWithLengthPrefix<Packet>(stream, ProtoBuf.PrefixStyle.Fixed32);
            Printer.PrintDiagnostics("Received {0} byte encrypted packet.", packet.Data.Length);
            if (packet.DecompressedSize.HasValue)
            {
                using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream(packet.Data))
                using (System.IO.Compression.GZipStream gzStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Decompress))
                {
                    packet.Data = new byte[packet.DecompressedSize.Value];
                    gzStream.Read(packet.Data, 0, packet.DecompressedSize.Value);
                }
                Printer.PrintDiagnostics(" - {0} bytes decompressed", packet.DecompressedSize.Value);
            }

            using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream(packet.Data))
            using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(memoryStream, decoder, System.Security.Cryptography.CryptoStreamMode.Read))
            {
                return ProtoBuf.Serializer.Deserialize<T>(cs);
            }
        }
    }
}
