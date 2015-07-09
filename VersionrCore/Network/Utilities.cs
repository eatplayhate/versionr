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
        }
        public static void SendEncrypted<T>(System.Net.Sockets.NetworkStream stream, System.Security.Cryptography.ICryptoTransform encoder, T argument)
        {
            System.IO.MemoryStream memoryStream = new System.IO.MemoryStream();
            using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(memoryStream, encoder, System.Security.Cryptography.CryptoStreamMode.Write))
            {
                ProtoBuf.Serializer.Serialize<T>(cs, argument);
            }
            Packet packet = new Packet() { Data = memoryStream.ToArray() };
            ProtoBuf.Serializer.SerializeWithLengthPrefix<Packet>(stream, packet, ProtoBuf.PrefixStyle.Fixed32);
        }
        public static T ReceiveEncrypted<T>(System.Net.Sockets.NetworkStream stream, System.Security.Cryptography.ICryptoTransform decoder)
        {
            Packet packet = ProtoBuf.Serializer.DeserializeWithLengthPrefix<Packet>(stream, ProtoBuf.PrefixStyle.Fixed32);
            Printer.PrintDiagnostics("Received {0} byte encrypted packet.", packet.Data.Length);
            System.IO.MemoryStream memoryStream = new System.IO.MemoryStream(packet.Data);
            using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(memoryStream, decoder, System.Security.Cryptography.CryptoStreamMode.Read))
            {
                return ProtoBuf.Serializer.Deserialize<T>(cs);
            }
        }
    }
}
