using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public class LZHAMLegacyStream : System.IO.Stream
    {
        IntPtr m_Decompressor { get; set; }
        long OutputSize { get; set; }

        public override bool CanRead
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return false;
            }
        }

        public override long Length
        {
            get
            {
                return OutputSize;
            }
        }

        public override long Position
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "CreateDecompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern IntPtr CreateDecompressionStream(int windowBits);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DestroyDecompressionStream", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool DestroyDecompressionStream(IntPtr stream);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DecompressData", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int DecompressData(IntPtr stream, byte[] output, int outLength, out bool finished);

        [System.Runtime.InteropServices.DllImport("lzhamwrapper", EntryPoint = "DecompressSetSource", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int DecompressSetSource(IntPtr stream, byte[] output, int outLength);
        

        System.IO.Stream BaseStream { get; set; }
        byte[] m_Buffer = new byte[4 * 1024 * 1024];
        byte[] m_OutputBuffer = new byte[16 * 1024 * 1024];
        int m_OutputPosition;
        int m_DecompressorEnd;
        bool m_End;

        public LZHAMLegacyStream(System.IO.Stream baseStream, bool readFirstID, long? outputSize = null)
        {
            m_Decompressor = CreateDecompressionStream(23);
            BaseStream = baseStream;
            BinaryReader sw = new BinaryReader(BaseStream);
            if (readFirstID)
            {
                if (sw.ReadInt32() != 0)
                    throw new Exception();
            }
            if (outputSize == null)
                OutputSize = sw.ReadInt64();
            else
                outputSize = OutputSize;
            m_OutputPosition = m_OutputBuffer.Length;
            m_DecompressorEnd = m_OutputBuffer.Length;
            m_End = false;

            var readAmount = BaseStream.Read(m_Buffer, 0, m_Buffer.Length);
            DecompressSetSource(m_Decompressor, m_Buffer, readAmount);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
            if (m_Decompressor != IntPtr.Zero)
                DestroyDecompressionStream(m_Decompressor);
            m_Decompressor = IntPtr.Zero;
            BaseStream.Dispose();
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int readamount = 0;
            while (count != readamount)
            {
                int remainder = count - readamount;
                int copyable = m_DecompressorEnd - m_OutputPosition;
                if (copyable == 0)
                {
                    if (m_End)
                        return readamount;
                    Decompress();
                }
                else
                {
                    int max = copyable > remainder ? remainder : copyable;
                    Array.Copy(m_OutputBuffer, m_OutputPosition, buffer, offset + readamount, max);
                    m_OutputPosition += max;
                    readamount += max;
                }
            }
            return readamount;
        }

        private void Decompress()
        {
            bool finished = false;
            var result = DecompressData(m_Decompressor, m_OutputBuffer, m_OutputBuffer.Length, out finished);
            if (result <= 0)
            {
                var readAmount = BaseStream.Read(m_Buffer, 0, m_Buffer.Length);
                if (readAmount == 0)
                    m_End = true;
                else
                    DecompressSetSource(m_Decompressor, m_Buffer, readAmount);
                result = -result;
            }
            m_DecompressorEnd = result;
            m_OutputPosition = 0;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}