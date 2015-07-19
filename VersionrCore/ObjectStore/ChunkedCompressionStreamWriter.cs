using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.ObjectStore
{
    public abstract class ChunkedCompressionStreamWriter : IDisposable
    {
        protected void Run(long fileLength, int chunkSize, out long resultSize, Stream inputData, Stream outputData, Action<long, long, long> feedback = null)
        {
            if (!outputData.CanSeek)
                throw new Exception();
            long baseOutputPos = outputData.Position;

            resultSize = 0;
            byte[] chunkBuffer = new byte[chunkSize];
            byte[] outBuffer = new byte[chunkSize * 2];

            long remainder = fileLength;
            uint chunkCount = (uint)(fileLength / chunkSize);
            if (chunkCount > int.MaxValue)
                throw new Exception("File is too big!");
            if (fileLength != (long)chunkCount * chunkSize)
                chunkCount++;

            List<uint> sizes = new List<uint>();

            outputData.Seek(baseOutputPos + chunkCount * 4 + 4, SeekOrigin.Begin);

            resultSize = chunkCount * 4 + 4;

            while (remainder > 0)
            {
                if (feedback != null)
                    feedback(fileLength, fileLength - remainder, resultSize);
                int available = chunkSize;
                if (available > remainder)
                    available = (int)remainder;
                inputData.Read(chunkBuffer, 0, available);
                uint blockSize;
                remainder -= available;
                CompressData(chunkBuffer, outBuffer, available, out blockSize, remainder == 0);

                resultSize += blockSize;
                sizes.Add(blockSize);
                outputData.Write(outBuffer, 0, (int)blockSize);
            }

            outputData.Seek(baseOutputPos, SeekOrigin.Begin);
            outputData.Write(BitConverter.GetBytes(chunkSize), 0, 4);
            foreach (var x in sizes)
            {
                outputData.Write(BitConverter.GetBytes(x), 0, 4);
            }
        }
        protected ChunkedCompressionStreamWriter()
        {
        }

        protected abstract void CompressData(byte[] inputData, byte[] outputData, int available, out uint blockSize, bool end);

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
