using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public class LZHAMReaderStream : ChunkedDecompressionStream
    {
        IntPtr m_Decompressor { get; set; }
        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "CreateDecompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern IntPtr CreateDecompressionStream(int windowBits);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DestroyDecompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool DestroyDecompressionStream(IntPtr stream);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DecompressData", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int DecompressData(IntPtr stream, byte[] output, int outLength);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DecompressSetSource", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int DecompressSetSource(IntPtr stream, byte[] output, int outLength);

        protected override void RefillBuffer(byte[] data, byte[] output, int decompressedSize, bool end)
        {
            DecompressSetSource(m_Decompressor, data, data.Length);
            while (DecompressData(m_Decompressor, output, decompressedSize) != decompressedSize)
            {
                DestroyDecompressionStream(m_Decompressor);
                m_Decompressor = CreateDecompressionStream(23);
                DecompressSetSource(m_Decompressor, data, data.Length);
            }
            // We call decompress again to consume the sync block
            if (DecompressData(m_Decompressor, output, 0) != 0)
                throw new Exception();
        }

        public LZHAMReaderStream(long size, System.IO.Stream baseStream)
            : base(size, baseStream)
        {
            m_Decompressor = CreateDecompressionStream(23);
            Reset();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
            if (m_Decompressor != IntPtr.Zero)
                DestroyDecompressionStream(m_Decompressor);
            m_Decompressor = IntPtr.Zero;
            base.Dispose(disposing);
        }
    }
}
