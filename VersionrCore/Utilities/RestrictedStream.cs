using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    class RestrictedStream : System.IO.Stream
    {
        Stream UnderlyingStream { get; set; }
        long WindowSize { get; set; }
        long InternalPosition { get; set; }

        public RestrictedStream(Stream baseStream, long size)
        {
            UnderlyingStream = baseStream;
            WindowSize = size;
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
                return WindowSize;
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
            int bytesRead = count;
            long max = Length - Position;
            if (max < bytesRead)
                bytesRead = (int)max;

            UnderlyingStream.Read(buffer, offset, bytesRead);
            InternalPosition += bytesRead;

            return bytesRead;
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
