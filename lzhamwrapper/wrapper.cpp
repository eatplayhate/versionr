#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <memory.h>

#define LZHAM_DEFINE_ZLIB_API
#include "lzham_static_lib.h"

#ifdef _MSC_VER
#define WRAPPER_API __declspec(dllexport)
#else
#define WRAPPER_API __attribute__((visibility("default")))
#endif

extern "C"
{
	int main()
	{
		return 0;
	}
	bool WRAPPER_API DestroyCompressionStream(z_stream* str)
	{
		if (deflateEnd(str) != Z_OK)
			return false;
		delete str;
		return true;
	}

	unsigned int WRAPPER_API StreamGetAdler32(z_stream* str)
	{
		return str->adler;
	}

	WRAPPER_API lzham_z_stream* CreateCompressionStream(int level, int dictionaryBits)
	{
		z_stream* stream = new z_stream();
		memset(stream, 0, sizeof(z_stream));
		stream->next_in = NULL;
		stream->avail_in = 0;
		stream->next_out = NULL;
		stream->avail_out = 0;

		if (deflateInit2(stream, level, LZHAM_Z_LZHAM, dictionaryBits, 9, LZHAM_Z_DEFAULT_STRATEGY) != Z_OK)
			return NULL;
		return stream;
	}

	int WRAPPER_API CompressData(z_stream* stream, unsigned char* dataIn, int inLength, unsigned char* dataOut, int outLength, bool flush, bool end)
	{
		stream->next_in = dataIn;
		stream->avail_in = inLength;
		stream->next_out = dataOut;
		stream->avail_out = outLength;
		int status = deflate(stream, end ? Z_FINISH : (flush ? Z_FULL_FLUSH : Z_NO_FLUSH));
		int length = outLength - stream->avail_out;
		if ((status == Z_STREAM_END) || (status == Z_OK))
			return length;
		return -1;
	}

	bool WRAPPER_API DestroyDecompressionStream(z_stream* str)
	{
		if (inflateEnd(str) != Z_OK)
			return false;
		delete str;
		return true;
	}

	WRAPPER_API lzham_z_stream* CreateDecompressionStream(int dictionaryBits)
	{
		z_stream* stream = new z_stream();
		memset(stream, 0, sizeof(z_stream));
		stream->next_in = NULL;
		stream->avail_in = 0;
		stream->next_out = NULL;
		stream->avail_out = 0;

		if (inflateInit2(stream, dictionaryBits) != Z_OK)
			return NULL;
		return stream;
	}

	void WRAPPER_API DecompressSetSource(z_stream* stream, unsigned char* dataIn, int inLength)
	{
		stream->next_in = dataIn;
		stream->avail_in = inLength;
	}

	int WRAPPER_API DecompressData(z_stream* stream, unsigned char* dataOut, int outLength, bool& finishedBlock)
	{
		stream->next_out = dataOut;
		stream->avail_out = outLength;
		int status = inflate(stream, Z_SYNC_FLUSH);
		int length = outLength - stream->avail_out;
		finishedBlock = stream->avail_in == 0;
		if (status == Z_STREAM_END)
			return -length;
		if (stream->avail_out == 0)
			return length;
		if (status == Z_OK)
			return length;
		return -1;
	}
}
