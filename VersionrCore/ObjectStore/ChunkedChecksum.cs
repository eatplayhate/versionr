using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public class ChunkedChecksum
    {
        enum HashMode
        {
            SHA1 = 0,
            Murmur3 = 1,
        }
        class Chunk
        {
            public int Index;
            public long Offset;
            public uint Adler32;
            public int Length;
            public byte[] Hash;
        };
        int ChunkSize { get; set; }
        uint ChunkCount { get; set; }
        Chunk[] Chunks { get; set; }
        HashMode HashType { get; set; }

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
        
        public static List<FileBlock> ComputeDelta(System.IO.Stream input, long inputSize, ChunkedChecksum chunks, out long deltaSize, Action<long, long> progress = null)
        {
            List<FileBlock> deltas = new List<FileBlock>();
            if (inputSize < chunks.ChunkSize)
            {
                deltas.Add(new FileBlock() { Base = false, Offset = 0, Length = inputSize });
                deltaSize = 1 + 8 + inputSize;
                return deltas;
            }
            List<Chunk> sortedChunks = chunks.Chunks.OrderBy(x => new Tuple<uint, long>(x.Adler32, x.Offset)).ToList();
            Dictionary<uint, int> adlerToIndex = new Dictionary<uint, int>();
            for (int i = 0; i < sortedChunks.Count; i++)
                adlerToIndex[sortedChunks[i].Adler32] = i;
            
            BufferedView view = new BufferedView(input, 4 * 1024 * 1024);
            CircularBuffer circularBuffer = new CircularBuffer(chunks.ChunkSize);
            long readHead = 0;
            long processHead = 0;
            uint checksum = 0;
            FileBlock lastMatch = null;
            System.Security.Cryptography.SHA1 sha1 = null;
            if (chunks.HashType == HashMode.SHA1)
                sha1 = System.Security.Cryptography.SHA1.Create();
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
                if (progress != null)
                    progress(inputSize, processHead);
                int possibleChunkLookup;
                Chunk match = null;
                if (adlerToIndex.TryGetValue(checksum, out possibleChunkLookup))
                {
                    byte[] data = circularBuffer.ToArray(chunks.ChunkSize > remainder ? remainder : chunks.ChunkSize);
                    byte[] realHash = null;
                    if (sha1 != null)
                        realHash = sha1.ComputeHash(data, 0, data.Length);
                    else
                        realHash = (new Utilities.Murmur3()).ComputeHash(data);
                    while (true)
                    {
                        Chunk inspectedChunk = sortedChunks[possibleChunkLookup];
                        if (inspectedChunk.Adler32 != checksum)
                            break;
                        if (inspectedChunk.Length == remainder &&
                            System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(realHash, inspectedChunk.Hash))
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
        
        public static void Skip(System.IO.Stream stream)
        {
            byte[] temp = new byte[16];
            stream.Read(temp, 0, 16);
            int version = 0;
            if (temp[0] == 'h' && temp[1] == 'a' && temp[2] == 's' && temp[3] == 'h')
                version = 1;
            else if (temp[0] == 'h' && temp[1] == 's' && temp[2] == 'h' && temp[3] == '2')
                version = 2;
            if (version == 0)
                throw new Exception();
            int chunkSize = BitConverter.ToInt32(temp, 8);
            uint chunkCount = BitConverter.ToUInt32(temp, 12);
            int offset = 0;
            int perchunk = 4;
            if (version == 1)
                perchunk += 20;
            else
            {
                offset += 4;
                perchunk += 16;
            }
            stream.Seek(offset + chunkCount * perchunk, SeekOrigin.Current);
        }

        public static ChunkedChecksum Load(long filesize, System.IO.Stream stream)
        {
            ChunkedChecksum result = new ChunkedChecksum();

            byte[] temp = new byte[16];
            stream.Read(temp, 0, 16);
            int version = 0;
            if (temp[0] == 'h' && temp[1] == 'a' && temp[2] == 's' && temp[3] == 'h')
                version = 1;
            else if (temp[0] == 'h' && temp[1] == 's' && temp[2] == 'h' && temp[3] == '2')
                version = 2;
            if (version == 0)
                throw new Exception();
            result.ChunkSize = BitConverter.ToInt32(temp, 8);
            result.ChunkCount = BitConverter.ToUInt32(temp, 12);
            result.Chunks = new Chunk[result.ChunkCount];
            if (version == 1)
                result.HashType = HashMode.SHA1;
            else if (version == 2)
            {
                stream.Read(temp, 0, 4);
                result.HashType = (HashMode)BitConverter.ToInt32(temp, 0);
            }

            uint remaining = result.ChunkCount;
            long offset = 0;
            int index = 0;
            while (remaining > 0)
            {
                stream.Read(temp, 0, 4);
                Chunk c = new Chunk() { Adler32 = BitConverter.ToUInt32(temp, 0), Offset = offset };
                offset += result.ChunkSize;
                if (remaining == 1)
                    c.Length = (int)(filesize - offset);
                else
                    c.Length = result.ChunkSize;
                if (result.HashType == HashMode.SHA1)
                {
                    c.Hash = new byte[20];
                    stream.Read(c.Hash, 0, 20);
                }
                else if (result.HashType == HashMode.Murmur3)
                {
                    c.Hash = new byte[16];
                    stream.Read(c.Hash, 0, 16);
                }
                c.Index = index;
                result.Chunks[index++] = c;
                remaining--;
            }

            return result;
        }

        public static ChunkedChecksum Compute(int size, System.IO.Stream stream, Action<long, long> progress = null)
        {
            ChunkedChecksum result = new ChunkedChecksum();
            result.ChunkSize = size;
            result.HashType = HashMode.Murmur3;
            byte[] block = new byte[size];
            List<Chunk> chunks = new List<Chunk>();
            
            long offset = 0;
            int index = 0;
            System.Security.Cryptography.SHA1 sha1 = null;
            if (result.HashType == HashMode.SHA1)
                sha1 = System.Security.Cryptography.SHA1.Create();
            while (true)
            {
                if (progress != null)
                    progress(size, offset);
                int count = stream.Read(block, 0, size);
                if (count == 0)
                    break;
                Chunk chunk = new Chunk()
                {
                    Index = index++,
                    Offset = offset,
                    Adler32 = FastHash(block, count),
                    //Hash = sha1.ComputeHash(block, 0, count),
                    Hash = new Utilities.Murmur3().ComputeHash(block, count),
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

        public static uint FastHash(byte[] block, int size)
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

        internal static void Write(System.IO.Stream stream, ChunkedChecksum result)
        {
            if (result.HashType == HashMode.SHA1)
                stream.Write(new byte[] { (byte)'h', (byte)'a', (byte)'s', (byte)'h' }, 0, 4);
            else
                stream.Write(new byte[] { (byte)'h', (byte)'s', (byte)'h', (byte)'2' }, 0, 4);
            stream.Write(BitConverter.GetBytes(result.Chunks.Length), 0, 4);
            stream.Write(BitConverter.GetBytes(result.ChunkSize), 0, 4);
            stream.Write(BitConverter.GetBytes(result.ChunkCount), 0, 4);
            stream.Write(BitConverter.GetBytes((int)result.HashType), 0, 4);
            foreach (var x in result.Chunks)
            {
                stream.Write(BitConverter.GetBytes(x.Adler32), 0, 4);
                stream.Write(x.Hash, 0, x.Hash.Length);
            }
        }
        internal static void ApplyDelta(System.IO.Stream baseFile, System.IO.Stream deltaFile, System.IO.Stream outputFile)
        {
            byte[] blobs = new byte[9];
            byte[] runningBuffer = new byte[4 * 1024 * 1024];
            deltaFile.Read(blobs, 0, 4);
            if (blobs[0] != 'c' || blobs[1] != 'h' || blobs[2] != 'n' || blobs[3] != 'k')
                throw new Exception();
            while (true)
            {
                deltaFile.Read(blobs, 0, 1);
                int blockCount = blobs[0];
                deltaFile.Read(blobs, 0, 8);
                long length = BitConverter.ToInt64(blobs, 0);
                if (blockCount > 0)
                {
                    deltaFile.Read(blobs, 0, 8);
                    long offset = BitConverter.ToInt64(blobs, 0);
                    baseFile.Position = offset;
                    if (length < runningBuffer.Length)
                    {
                        int remainder = (int)length;
                        baseFile.Read(runningBuffer, 0, remainder);
                        for (int i = 0; i < blockCount; i++)
                            outputFile.Write(runningBuffer, 0, remainder);
                    }
                    else
                    {
                        for (int i = 0; i < blockCount; i++)
                        {
                            while (length > 0)
                            {
                                int remainder = runningBuffer.Length;
                                if (remainder > length)
                                    remainder = (int)length;
                                baseFile.Read(runningBuffer, 0, remainder);
                                outputFile.Write(runningBuffer, 0, remainder);
                                length -= remainder;
                            }
                        }
                    }
                }
                else
                {
                    if (length == 0)
                        break;
                    while (length > 0)
                    {
                        int remainder = runningBuffer.Length;
                        if (remainder > length)
                            remainder = (int)length;
                        deltaFile.Read(runningBuffer, 0, remainder);
                        outputFile.Write(runningBuffer, 0, remainder);
                        length -= remainder;
                    }
                }
            }
        }

        internal static void WriteDelta(System.IO.Stream input, System.IO.Stream output, List<FileBlock> deltas)
        {
            output.Write(new byte[] { (byte)'c', (byte)'h', (byte)'n', (byte)'k' }, 0, 4);
            for (int i = 0; i < deltas.Count; i++)
            {
                var x = deltas[i];
                if (x.Base == true)
                {
                    int count = 1;
                    for (int j = i + 1; j < deltas.Count && count < 255; j++)
                    {
                        if (deltas[j].Base == true && deltas[j].Offset == x.Offset && deltas[j].Length == x.Length)
                            count++;
                        else
                            break;
                    }
                    i += count - 1;
                    output.Write(new byte[] { (byte)count }, 0, 1);
                    output.Write(BitConverter.GetBytes(x.Length), 0, 8);
                    output.Write(BitConverter.GetBytes(x.Offset), 0, 8);
                }
                else
                {
                    input.Position = x.Offset;
                    output.Write(new byte[] { 0 }, 0, 1);
                    output.Write(BitConverter.GetBytes(x.Length), 0, 8);
                    byte[] buffer = new byte[4 * 1024 * 1024];
                    long remainder = x.Length;
                    while (remainder > 0)
                    {
                        int size = buffer.Length;
                        if (size > remainder)
                            size = (int)remainder;
                        input.Read(buffer, 0, size);
                        output.Write(buffer, 0, size);
                        remainder -= size;
                    }
                }
            }
            output.Write(new byte[] { 0 }, 0, 1);
            output.Write(BitConverter.GetBytes((long)0), 0, 8);
        }
    }
}
