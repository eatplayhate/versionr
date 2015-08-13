using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.Network
{
    class Utilities
    {
        public enum PacketCompressionCodec
        {
            None,
            LZ4,
            LZH,
        }
        public enum ChecksumCodec
        {
            None,
            XXHash,
            Adler32,
            MurMur3,
            Default = Adler32
        }
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
            [ProtoBuf.ProtoMember(5)]
            public uint Codec { get; set; }

            [ProtoBuf.ProtoIgnore]
            public ChecksumCodec Checksum
            {
                get
                {
                    return (ChecksumCodec)(Codec >> 16);
                }
                set
                {
                    Codec = ((uint)value << 16) | (Codec & 0xFFFF);
                }
            }

            [ProtoBuf.ProtoIgnore]
            public PacketCompressionCodec Compression
            {
                get
                {
                    return (PacketCompressionCodec)(Codec & 0xFFFF);
                }
                set
                {
                    Codec = ((uint)value) | (Codec & 0xFFFF0000);
                }
            }
        }

        public static uint ComputeChecksumAdler32(byte[] array)
        {
            return ObjectStore.ChunkedChecksum.FastHash(array, array.Length);
        }

        public static uint ComputeChecksumXXHash(byte[] array)
        {
            return xxHashSharp.xxHash.CalculateHash(array);
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
        public static void SendEncrypted<T>(SharedNetwork.SharedNetworkInfo info, T argument, System.IO.Stream target = null)
        {
            byte[] result;
            using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream())
            {
                ProtoBuf.Serializer.Serialize<T>(memoryStream, argument);
                result = memoryStream.ToArray();
            }

            ChecksumCodec ccode = info.ChecksumType;
            uint checksum = 0;
            if (ccode == ChecksumCodec.XXHash)
                checksum = ComputeChecksumXXHash(result);
            if (ccode == ChecksumCodec.Adler32)
                checksum = ComputeChecksumAdler32(result);
            if (ccode == ChecksumCodec.MurMur3)
            {
                var hasher = new Versionr.Utilities.Murmur3();
                var hash = hasher.ComputeHash(result);
                checksum = BitConverter.ToUInt32(hash, 0) ^ BitConverter.ToUInt32(hash, 4) ^ BitConverter.ToUInt32(hash, 8) ^ BitConverter.ToUInt32(hash, 12);
            }
            int? decompressedSize = null;
            byte[] compressedBuffer = null;
            PacketCompressionCodec codec = PacketCompressionCodec.None;
            if (result.Length > 512 * 1024)
            {
                compressedBuffer = LZ4.LZ4Codec.Encode(result, 0, result.Length);
                codec = PacketCompressionCodec.LZ4;
            }
            else
            {
                compressedBuffer = new byte[result.Length * 2];
                Versionr.Utilities.LZHL.ResetCompressor(info.LZHLCompressor);
                int compressedSize = (int)Versionr.Utilities.LZHL.Compress(info.LZHLCompressor, result, (uint)result.Length, compressedBuffer);
                Array.Resize(ref compressedBuffer, compressedSize);
                codec = PacketCompressionCodec.LZH;
            }
            if (compressedBuffer.Length < result.Length)
            {
                decompressedSize = result.Length;
                result = compressedBuffer;
            }

            int payload = result.Length;
            if (info.EncryptorFunction != null)
            {
                using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream())
                {
                    using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(memoryStream, info.Encryptor, System.Security.Cryptography.CryptoStreamMode.Write))
                    {
                        cs.Write(result, 0, result.Length);
                    }
                    result = memoryStream.ToArray();
                }
            }
            
            Packet packet = new Packet()
            {
                DecompressedSize = decompressedSize,
                Data = result,
                PayloadSize = payload,
                Hash = checksum,
                Compression = codec,
                Checksum = ccode
            };

            if (target == null)
                ProtoBuf.Serializer.SerializeWithLengthPrefix<Packet>(info.Stream, packet, ProtoBuf.PrefixStyle.Fixed32);
            else
                ProtoBuf.Serializer.SerializeWithLengthPrefix<Packet>(target, packet, ProtoBuf.PrefixStyle.Fixed32);
        }
        public static T ReceiveEncrypted<T>(SharedNetwork.SharedNetworkInfo info)
        {
            Packet packet = ProtoBuf.Serializer.DeserializeWithLengthPrefix<Packet>(info.Stream, ProtoBuf.PrefixStyle.Fixed32);
            Printer.PrintDiagnostics("Received {0} byte packet.", packet.Data.Length);

            byte[] decryptedData = packet.Data;
            if (info.DecryptorFunction != null)
            {
                decryptedData = new byte[packet.PayloadSize];
                using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream(packet.Data))
                using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(memoryStream, info.Decryptor, System.Security.Cryptography.CryptoStreamMode.Read))
                {
                    cs.Read(decryptedData, 0, decryptedData.Length);
                }
            }
            
            if (packet.DecompressedSize.HasValue)
            {
                switch (packet.Compression)
                {
                    case PacketCompressionCodec.None:
                        break;
                    case PacketCompressionCodec.LZ4:
                        decryptedData = LZ4.LZ4Codec.Decode(decryptedData, 0, decryptedData.Length, packet.DecompressedSize.Value);
                        break;
                    case PacketCompressionCodec.LZH:
                    {
                        Versionr.Utilities.LZHL.ResetDecompressor(info.LZHLDecompressor);
                        byte[] result = new byte[packet.DecompressedSize.Value];
                        Versionr.Utilities.LZHL.Decompress(info.LZHLDecompressor, decryptedData, (uint)decryptedData.Length, result, (uint)result.Length);
                        decryptedData = result;
                        break;
                    }
                }
                Printer.PrintDiagnostics(" - {0} bytes decompressed ({1})", packet.DecompressedSize.Value, packet.Compression);
            }

            if (packet.Checksum != ChecksumCodec.None)
            {
                uint checksum = 0;
                if (packet.Checksum == ChecksumCodec.XXHash)
                    checksum = ComputeChecksumXXHash(decryptedData);
                if (packet.Checksum == ChecksumCodec.Adler32)
                    checksum = ComputeChecksumAdler32(decryptedData);
                if (packet.Checksum == ChecksumCodec.MurMur3)
                {
                    var hasher = new Versionr.Utilities.Murmur3();
                    var hash = hasher.ComputeHash(decryptedData);
                    checksum = BitConverter.ToUInt32(hash, 0) ^ BitConverter.ToUInt32(hash, 4) ^ BitConverter.ToUInt32(hash, 8) ^ BitConverter.ToUInt32(hash, 12);
                }
                if (checksum != packet.Hash)
                    throw new Exception("Data did not survive the trip!");
            }

            using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream(decryptedData))
            {
                return ProtoBuf.Serializer.Deserialize<T>(memoryStream);
            }
        }

        internal static void SendEncryptedPrefixed<T>(NetCommand netCommand, SharedNetwork.SharedNetworkInfo info, T pack)
        {
            System.IO.MemoryStream ms = new System.IO.MemoryStream();
            ProtoBuf.Serializer.SerializeWithLengthPrefix<NetCommand>(ms, netCommand, ProtoBuf.PrefixStyle.Fixed32);
            SendEncrypted<T>(info, pack, ms);
            var data = ms.ToArray();
            info.Stream.Write(data, 0, data.Length);
        }
    }
}
