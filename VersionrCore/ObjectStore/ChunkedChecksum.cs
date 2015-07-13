using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public class ChunkedChecksum
    {
        class Chunk
        {
            public int Index;
            public long Offset;
            public uint Adler32;
            public int Length;
            public byte[] SHA1;
        };
        int ChunkSize { get; set; }
        uint ChunkCount { get; set; }
        Chunk[] Chunks { get; set; }

        class CircularBuffer
        {
            int Head;
            byte[] Buffer;

            public CircularBuffer(int size)
            {
                Head = 0;
                Buffer = new byte[size];
            }
            public byte[] ToArray(int size)
            {
                byte[] data = new byte[Buffer.Length];
                int start = Head - size;
                int firstBlock = 0;
                if (start < 0)
                {
                    start += Buffer.Length;
                    if (start < 0)
                        throw new Exception();

                    firstBlock = Buffer.Length - start;
                    Array.Copy(Buffer, start, data, 0, firstBlock);
                    start = 0;
                }
                int secondBlock = Head - start;
                Array.Copy(Buffer, 0, data, firstBlock, Head - start);
                return data;
            }
            public byte AddByte(byte b)
            {
                byte returnValue = Buffer[Head];
                Buffer[Head] = b;
                if (++Head == Buffer.Length)
                    Head = 0;
                return returnValue;
            }
        }

        class BufferedView
        {
            System.IO.Stream DataStream { get; set; }
            byte[] m_Buffer;
            int m_RemainderInBlock;
            int m_BufferPos;
            public BufferedView(System.IO.Stream stream, int bufferSize)
            {
                m_Buffer = new byte[bufferSize];
                DataStream = stream;
                m_RemainderInBlock = 0;
                m_BufferPos = 0;
                FillBuffer();
            }

            private void FillBuffer()
            {
                if (m_BufferPos == m_RemainderInBlock)
                {
                    var read = DataStream.Read(m_Buffer, 0, m_Buffer.Length);
                    m_RemainderInBlock = read;
                    m_BufferPos = 0;
                }
            }

            public byte Next()
            {
                FillBuffer();
                return m_Buffer[m_BufferPos++];
            }
        }

        public class FileBlock
        {
            public bool Base;
            public long Offset;
            public long Length;

            public long End
            {
                get
                {
                    return Offset + Length;
                }
            }
        }
        
        public static List<FileBlock> ComputeDelta(System.IO.Stream input, long inputSize, ChunkedChecksum chunks, out long deltaSize)
        {
            List<FileBlock> deltas = new List<FileBlock>();
            if (inputSize < chunks.ChunkSize)
            {
                deltas.Add(new FileBlock() { Base = false, Offset = 0, Length = inputSize });
                deltaSize = 1 + 8 + inputSize;
                return deltas;
            }
            List<Chunk> sortedChunks = chunks.Chunks.OrderBy(x => x.Adler32).ToList();
            Dictionary<uint, int> adlerToIndex = new Dictionary<uint, int>();
            for (int i = 0; i < sortedChunks.Count; i++)
                adlerToIndex[sortedChunks[i].Adler32] = i;
            
            BufferedView view = new BufferedView(input, 4 * 1024 * 1024);
            CircularBuffer circularBuffer = new CircularBuffer(chunks.ChunkSize);
            long readHead = 0;
            long processHead = 0;
            uint checksum = 0;
            FileBlock lastMatch = null;
            var sha1 = System.Security.Cryptography.SHA1.Create();
            while (processHead != inputSize)
            {
                // first run through
                if (readHead == processHead)
                {
                    for (int i = 0; i < chunks.ChunkSize; i++)
                        circularBuffer.AddByte(view.Next());
                    readHead = chunks.ChunkSize;
                    checksum = FastHash(circularBuffer.ToArray(chunks.ChunkSize), chunks.ChunkSize);
                }

                int remainder = (int)(readHead - processHead);
                int possibleChunkLookup;
                Chunk match = null;
                if (adlerToIndex.TryGetValue(checksum, out possibleChunkLookup))
                {
                    byte[] realHash = sha1.ComputeHash(circularBuffer.ToArray(chunks.ChunkSize > remainder ? remainder : chunks.ChunkSize), 0, remainder);
                    while (true)
                    {
                        Chunk inspectedChunk = sortedChunks[possibleChunkLookup];
                        if (inspectedChunk.Adler32 != checksum)
                            break;
                        if (inspectedChunk.Length == remainder &&
                            System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(realHash, inspectedChunk.SHA1))
                        {
                            match = inspectedChunk;
                            break;
                        }
                        possibleChunkLookup--;
                    }
                }
                if (match != null)
                {
                    if (lastMatch == null || (lastMatch.Base == false || lastMatch.End != match.Offset))
                    {
                        lastMatch = new FileBlock() { Base = true, Length = 0, Offset = match.Offset };
                        deltas.Add(lastMatch);
                    }
                    lastMatch.Length += match.Length;
                }
                else
                {
                    if (lastMatch == null || lastMatch.Base == true)
                    {
                        lastMatch = new FileBlock() { Base = false, Length = 0, Offset = processHead };
                        deltas.Add(lastMatch);
                    }
                    lastMatch.Length++;
                }

                // eat next byte
                int bytesToEat = 1;
                if (match != null)
                    bytesToEat = match.Length;
                while (bytesToEat-- != 0)
                {
                    remainder = (int)(readHead - processHead);
                    if (readHead < inputSize)
                    {
                        byte add = view.Next();
                        byte remove = circularBuffer.AddByte(add);
                        checksum = RotateHash(checksum, add, remove, remainder);
                        readHead++;
                    }
                    else if (bytesToEat == 0)
                    {
                        checksum = FastHash(circularBuffer.ToArray(remainder - 1), remainder - 1);
                    }
                    processHead++;
                }
            }

            deltaSize = 0;
            foreach (var x in deltas)
            {
                deltaSize++;
                if (x.Base == false)
                {
                    deltaSize += 8;
                    deltaSize += x.Length;
                }
                else
                {
                    deltaSize += 8;
                    deltaSize += 8;
                }
            }

            return deltas;
        }

        private static uint RotateHash(uint checksum, byte add, byte remove, int chunkSize)
        {
            ushort b = (ushort)(checksum >> 16 & 0xffff);
            ushort a = (ushort)(checksum & 0xffff);

            a = (ushort)((a - remove + add));
            b = (ushort)((b - (chunkSize * remove) + a - 1));

            return (uint)((b << 16) | a);
        }

        public static ChunkedChecksum Load(long filesize, System.IO.Stream stream)
        {
            ChunkedChecksum result = new ChunkedChecksum();

            byte[] temp8 = new byte[8];
            stream.Read(temp8, 0, 8);
            result.ChunkSize = BitConverter.ToInt32(temp8, 0);
            result.ChunkCount = BitConverter.ToUInt32(temp8, 4);
            result.Chunks = new Chunk[result.ChunkCount];

            uint remaining = result.ChunkCount;
            long offset = 0;
            int index = 0;
            while (remaining > 0)
            {
                stream.Read(temp8, 0, 4);
                Chunk c = new Chunk() { Adler32 = BitConverter.ToUInt32(temp8, 0), SHA1 = new byte[20], Offset = offset };
                offset += result.ChunkSize;
                if (remaining == 1)
                {
                    stream.Read(temp8, 0, 4);
                    c.Length = BitConverter.ToInt32(temp8, 0);
                }
                else
                    c.Length = result.ChunkSize;
                c.Index = index;
                result.Chunks[index++] = c;
                remaining--;
            }

            return result;
        }

        public static ChunkedChecksum Compute(int size, System.IO.Stream stream)
        {
            ChunkedChecksum result = new ChunkedChecksum();
            result.ChunkSize = size;
            byte[] block = new byte[size];
            List<Chunk> chunks = new List<Chunk>();

            var sha1 = System.Security.Cryptography.SHA1.Create();
            long offset = 0;
            int index = 0;
            while (true)
            {
                int count = stream.Read(block, 0, size);
                if (count == 0)
                    break;
                if (count < size)
                {
                    int lols = 1;
                }
                Chunk chunk = new Chunk()
                {
                    Index = index++,
                    Offset = offset,
                    Adler32 = FastHash(block, count),
                    SHA1 = sha1.ComputeHash(block, 0, count),
                    Length = count
                };
                offset += size;
                chunks.Add(chunk);
                if (count < size)
                    break;
            }
            result.Chunks = chunks.ToArray();
            result.ChunkCount = (uint)chunks.Count;
            return result;
        }

        private static uint FastHash(byte[] block, int size)
        {
            ushort a = 1;
            ushort b = 0;
            for (int i = 0; i < size; i++)
            {
                a = (ushort)(block[i] + a);
                b = (ushort)(b + a);
            }
            return (uint)((b << 16) | a);
        }
    }
}
