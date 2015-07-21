using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public class LZ4Writer : ChunkedCompressionStreamWriter
    {
        bool HighCompression { get; set; }
        protected LZ4Writer(bool hc) : base()
        {
            HighCompression = hc;
        }
        protected override void CompressData(byte[] inputData, byte[] outputData, int available, out uint blockSize, bool end)
        {
            if (HighCompression)
                blockSize = (uint)LZ4.LZ4Codec.EncodeHC(inputData, 0, available, outputData, 0, outputData.Length);
            else
                blockSize = (uint)LZ4.LZ4Codec.Encode(inputData, 0, available, outputData, 0, outputData.Length);
        }
        public static void CompressToStream(long fileLength, int chunkSize, out long resultSize, System.IO.Stream inputData, System.IO.Stream outputData, Action<long, long, long> feedback = null)
        {
            using (LZ4Writer writer = new LZ4Writer(false))
            {
                writer.Run(fileLength, chunkSize, out resultSize, inputData, outputData, feedback);
            }
        }
    }
    public class LZ4HCWriter : LZ4Writer
    {
        protected LZ4HCWriter() : base(true)
        {
        }
        public static new void CompressToStream(long fileLength, int chunkSize, out long resultSize, System.IO.Stream inputData, System.IO.Stream outputData, Action<long, long, long> feedback = null)
        {
            using (LZ4HCWriter writer = new LZ4HCWriter())
            {
                writer.Run(fileLength, chunkSize, out resultSize, inputData, outputData, feedback);
            }
        }
    }
}
