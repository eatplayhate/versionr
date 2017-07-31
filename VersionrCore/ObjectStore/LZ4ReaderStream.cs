using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public class LZ4ReaderStream : ChunkedDecompressionStream
    {
        protected override void RefillBuffer(byte[] data, byte[] output, int decompressedSize, bool end)
        {
            LZ4.LZ4Codec.Decode(data, 0, data.Length, output, 0, decompressedSize, true);
        }

        protected LZ4ReaderStream(long size, int chunkSize, System.IO.Stream baseStream)
            : base(size, chunkSize, baseStream)
        {
            Reset();
        }

        public static System.IO.Stream OpenStream(long fileSize, System.IO.Stream baseStream)
        {
            int chunkSize;
            var stream = ChunkedDecompressionStream.OpenStreamFast(fileSize, baseStream, out chunkSize,
                (byte[] data, int offset, int length, int outsize) =>
                {
                    byte[] output = new byte[outsize];
                    LZ4.LZ4Codec.Decode(data, offset, length, output, 0, outsize, true);
                    return output;
                });

            if (stream != null)
                return stream;
            return new LZ4ReaderStream(fileSize, chunkSize, baseStream);
        }
    }
}
