#include "LZHLDecoderStat.hpp"
#include "LZHLCompressor.hpp"
#include "LZHLDecompressor.hpp"

#ifdef _MSC_VER
#define DLLAPI __declspec(dllexport)
#else
#define DLLAPI __attribute__((visibility("default")))
#endif

extern "C" {

  DLLAPI void* CreateCompressor(void) {
    return new LZHLCompressor();
  }

  DLLAPI void DestroyCompressor(void *comp) {
    delete (LZHLCompressor *)comp;
  }

  DLLAPI void ResetCompressor(void *comp) {
	  ((LZHLCompressor *)comp)->reset();
  }

  DLLAPI void ResetDecompressor(void *decomp) {
	  ((LZHLDecompressor *)decomp)->reset();
  }

  DLLAPI unsigned int Compress(void *comp, unsigned char *buf, unsigned int size, unsigned char *ret) {
    return ((LZHLCompressor *)comp)->compress(ret, buf, size);
  }

  DLLAPI void* CreateDecompressor(void) {
    return new LZHLDecompressor();
  }

  DLLAPI unsigned int Decompress(void *decomp, unsigned char *buf, unsigned int size, unsigned char *ret, unsigned int retsize) {
	  size_t stSize = size;
	  size_t stRetSize = retsize;
	  if (!((LZHLDecompressor *)decomp)->decompress(ret, &stRetSize, buf, &stSize))
		  return -1;
    return (unsigned int)retsize;
  }

  DLLAPI void DestroyDecompressor(void *decomp) {
    delete (LZHLDecompressor *)decomp;
  }

}
