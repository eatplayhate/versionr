using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public abstract class ChunkedDecompressionStream : System.IO.Stream
    {
        long m_Length;
        long[] m_ChunkOffsets;
        uint[] m_ChunkSizes;
        int m_ChunkSize;
        int m_ChunkIndex;
        int m_LastChunkSize;
        long m_UnderlyingStreamOffset;

        byte[] m_ChunkBuffer;
        long m_Position;
        long m_BasePosition;
        int m_CurrentChunkSize;
        System.IO.Stream m_UnderlyingStream;

        public ChunkedDecompressionStream(long size, Stream baseStream)
        {
            m_UnderlyingStream = baseStream;
            m_Length = size;
            m_Position = 0;
            m_BasePosition = 0;

            long currentPos = 0;
            long compressedPos = 0;
            List<long> offsets = new List<long>();
            List<uint> sizes = new List<uint>();
            byte[] temp = new byte[4];
            baseStream.Read(temp, 0, 4);
            m_ChunkSize = BitConverter.ToInt32(temp, 0);
            while (true)
            {
                baseStream.Read(temp, 0, 4);
                uint chunkSize = BitConverter.ToUInt32(temp, 0);
                sizes.Add(chunkSize);
                offsets.Add(compressedPos);
                if (currentPos + m_ChunkSize >= size)
                {
                    // last chunk
                    m_LastChunkSize = (int)(size - currentPos);
                    break;
                }
                else
                    currentPos += m_ChunkSize;
                compressedPos += chunkSize;
            }
            m_UnderlyingStreamOffset = m_UnderlyingStream.Position;

            m_ChunkBuffer = new byte[offsets.Count == 1 ? m_LastChunkSize : m_ChunkSize];
            m_ChunkOffsets = offsets.ToArray();
            m_ChunkSizes = sizes.ToArray();
            m_ChunkIndex = -1;
        }

        protected void Reset()
        {
            m_ChunkIndex = -1;
            m_BasePosition = 0;
            m_Position = 0;
            UpdateChunk(0);
        }

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
                return true;
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
                return m_Length;
            }
        }

        public override long Position
        {
            get
            {
                return m_Position;
            }

            set
            {
                if (value < 0 || value > Length)
                    throw new System.IO.IOException();
                if (value < m_BasePosition)
                {
                    int posChunk = (int)(value / m_ChunkSize);
                    m_Position = value;
                    UpdateChunk(posChunk);
                }
                else if (value - m_BasePosition > m_CurrentChunkSize)
                {
                    int posChunk = (int)(value / m_ChunkSize);
                    m_Position = value;
                    UpdateChunk(posChunk);
                }
                else
                {
                    m_Position = value;
                }
            }
        }

        private void UpdateChunk(int posChunk)
        {
            if (posChunk != m_ChunkIndex)
            {
                if (posChunk > m_ChunkOffsets.Length)
                    throw new Exception();
                else if (posChunk < 0)
                    throw new Exception();
                m_ChunkIndex = posChunk;
                m_BasePosition = m_ChunkIndex * m_ChunkSize;
                m_UnderlyingStream.Position = m_ChunkOffsets[m_ChunkIndex] + m_UnderlyingStreamOffset;
                byte[] compressedData = new byte[m_ChunkSizes[m_ChunkIndex]];
                m_UnderlyingStream.Read(compressedData, 0, compressedData.Length);
                bool lastChunk = m_ChunkIndex == m_ChunkOffsets.Length - 1;
                m_CurrentChunkSize = lastChunk ? m_LastChunkSize : m_ChunkSize;
                RefillBuffer(compressedData, m_ChunkBuffer, m_CurrentChunkSize, lastChunk);
            }
        }

        protected abstract void RefillBuffer(byte[] data, byte[] output, int decompressedSize, bool end);

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long endOfFile = m_Length - m_Position;
            int availableSize = count;
            if (availableSize > endOfFile)
                availableSize = (int)endOfFile;
            if (availableSize > 0)
            {
                int readCount = 0;
                while (readCount != availableSize)
                {
                    int endOfBlock = (int)(m_BasePosition - m_Position) + m_CurrentChunkSize;
                    int srcIndex = (int)(m_Position - m_BasePosition);
                    int readable = availableSize - readCount;
                    if (readable > endOfBlock)
                        readable = endOfBlock;

                    Array.Copy(m_ChunkBuffer, srcIndex, buffer, offset + readCount, readable);

                    m_Position += readable;
                    readCount += readable;
                    if (readCount != availableSize)
                        UpdateChunk(m_ChunkIndex + 1);
                }
            }
            return availableSize;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = System.Math.Max(0, System.Math.Min(offset, Length));
                    break;
                case SeekOrigin.End:
                    Position = System.Math.Max(0, System.Math.Min(Length + offset, Length));
                    break;
                case SeekOrigin.Current:
                    Position = System.Math.Max(0, System.Math.Min(Position + offset, Length));
                    break;
            }
            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
