using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public class LZHAMWriter : ChunkedCompressionStreamWriter
    {
        IntPtr m_Compressor { get; set; }

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "CreateCompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern IntPtr CreateCompressionStream(int level, int windowBits);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DestroyCompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool DestroyCompressionStream(IntPtr stream);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "CompressData", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int CompressData(IntPtr stream, byte[] input, int inLength, byte[] output, int outLength, bool flush, bool end);

        protected LZHAMWriter() : base()
        {
            m_Compressor = CreateCompressionStream(9, 23);
        }
        protected override void CompressData(byte[] inputData, byte[] outputData, int available, out uint blockSize, bool end)
        {
            var result = CompressData(m_Compressor, inputData, available, outputData, outputData.Length, true, false);
            if (result < 0)
                throw new Exception();
            blockSize = (uint)result;
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (m_Compressor != IntPtr.Zero)
                DestroyCompressionStream(m_Compressor);
            m_Compressor = IntPtr.Zero;
        }
        public static void CompressToStream(long fileLength, int chunkSize, out long resultSize, System.IO.Stream inputData, System.IO.Stream outputData, Action<long, long, long> feedback = null)
        {
            using (LZHAMWriter writer = new LZHAMWriter())
            {
                writer.Run(fileLength, chunkSize, out resultSize, inputData, outputData, feedback);
            }
        }
    }
}
