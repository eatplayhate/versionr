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

        public LZ4ReaderStream(long size, System.IO.Stream baseStream)
            : base(size, baseStream)
        {
            Reset();
        }
    }
}
