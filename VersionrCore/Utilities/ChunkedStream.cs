using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    class ChunkedReceiverStream : System.IO.Stream
    {
        Func<Tuple<byte[], bool>> Receiver { get; set; }
        byte[] CurrentBlock { get; set; }
        long InternalPosition { get; set; }
        int LocalPosition { get; set; }
        bool End { get; set; }

        public ChunkedReceiverStream(Func<Tuple<byte[], bool>> receiver)
        {
            Receiver = receiver;
            CurrentBlock = new byte[0];
            InternalPosition = 0;
            LocalPosition = 0;
            End = false;
        }

        public bool EndOfStream
        {
            get
            {
                return LocalPosition == CurrentBlock.Length && End;
            }
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
                throw new NotImplementedException();
            }
        }

        public override long Position
        {
            get
            {
                return InternalPosition;
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;
            while (bytesRead < count)
            {
                if (LocalPosition == CurrentBlock.Length)
                {
                    if (!RefillBuffer())
                        return bytesRead;
                }
                int max = CurrentBlock.Length - LocalPosition;
                int requested = count - bytesRead;
                if (max < requested)
                    requested = max;
                Array.Copy(CurrentBlock, LocalPosition, buffer, offset + bytesRead, requested);
                InternalPosition += requested;
                bytesRead += requested;
                LocalPosition += requested;
            }
            return bytesRead;
        }

        private bool RefillBuffer()
        {
            if (End)
                return false;
            var result = Receiver();
            if (result.Item2)
                End = true;
            CurrentBlock = result.Item1;
            LocalPosition = 0;
            return true;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
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
