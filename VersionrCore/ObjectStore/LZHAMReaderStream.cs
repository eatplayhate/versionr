using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public class LZHAMReaderStream : ChunkedDecompressionStream
    {
        static System.Collections.Concurrent.ConcurrentBag<IntPtr> s_Decompressors = new System.Collections.Concurrent.ConcurrentBag<IntPtr>();
        IntPtr m_Decompressor { get; set; }

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "CreateDecompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern IntPtr CreateDecompressionStream(int windowBits);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "ResetDecompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern IntPtr ResetDecompressionStream(IntPtr stream);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DestroyDecompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool DestroyDecompressionStream(IntPtr stream);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DecompressData", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int DecompressData(IntPtr stream, IntPtr output, int outLength, out bool finished);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DecompressSetSource", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int DecompressSetSource(IntPtr stream, IntPtr output, int outLength);

        byte[] m_DecompressionBuffer = null;
        protected override void RefillBuffer(byte[] data, byte[] output, int decompressedSize, bool end)
        {
			unsafe
			{
				fixed (byte* input = data)
				fixed (byte* outptr = output)
				{
					m_DecompressionBuffer = data;
					DecompressSetSource(m_Decompressor, (IntPtr)input, data.Length);
					bool finished;
					while (DecompressData(m_Decompressor, (IntPtr)outptr, decompressedSize, out finished) != decompressedSize)
					{
						DestroyDecompressionStream(m_Decompressor);
						m_Decompressor = CreateDecompressionStream(WindowBits);
						DecompressSetSource(m_Decompressor, (IntPtr)input, data.Length);
					}
					while (!finished)
						DecompressData(m_Decompressor, (IntPtr)outptr, 0, out finished);
				}
			}
        }

        protected const int WindowBits = 23;

        protected LZHAMReaderStream(long size, int chunkSize, System.IO.Stream baseStream)
            : base(size, chunkSize, baseStream)
        {
            IntPtr decompressor;
            if (!s_Decompressors.TryTake(out decompressor))
                decompressor = CreateDecompressionStream(WindowBits);
            m_Decompressor = decompressor;
            Reset();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
            IntPtr decompressor = m_Decompressor;
            decompressor = ResetDecompressionStream(decompressor);
            if (decompressor != null)
                s_Decompressors.Add(decompressor);
            m_Decompressor = IntPtr.Zero;
            base.Dispose(disposing);
        }

        public static System.IO.Stream OpenStream(long fileSize, System.IO.Stream baseStream)
        {
            int chunkSize;
            var stream = ChunkedDecompressionStream.OpenStreamFast(fileSize, baseStream, out chunkSize,
                (byte[] data, int offset, int length, int outsize) =>
                {
                    IntPtr decompressor;
                    if (!s_Decompressors.TryTake(out decompressor))
                        decompressor = CreateDecompressionStream(WindowBits);

                    byte[] output = new byte[outsize];
                    unsafe
                    {
                        fixed (byte* outptr = output)
                        fixed (byte* inptr = data)
                        {
                            DecompressSetSource(decompressor, (IntPtr)inptr, length);
                            bool finished;
                            DecompressData(decompressor, (IntPtr)outptr, outsize, out finished);
                            if (!finished)
                                DecompressData(decompressor, (IntPtr)outptr, 0, out finished);
                        }
                    }
                    decompressor = ResetDecompressionStream(decompressor);
                    if (decompressor != null)
                        s_Decompressors.Add(decompressor);
                    return output;
                });

            if (stream != null)
                return stream;
            return new LZHAMReaderStream(fileSize, chunkSize, baseStream);
        }
    }
}
