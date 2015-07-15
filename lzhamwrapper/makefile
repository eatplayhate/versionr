CC=clang
CFLAGS=-fPIC -c -O3 -I../lzham_codec-master/include
LDFLAGS=-shared -lstdc++ -L../lzham_codec-master/ -llzhamlib -llzhamdecomp -llzhamcomp
SOURCES=wrapper.cpp
OBJECTS=$(SOURCES:.cpp=.o)
UNAME_S := $(shell uname -s)
ifeq ($(UNAME_S),Darwin)
	DLL=liblzhamwrapper.dylib
	ARCHOVERRIDE=-arch i386
else
	DLL=liblzhamwrapper.so
endif

all: libraries $(SOURCES) $(DLL)

libraries:
	$(MAKE) -C ../lzham_codec-master/lzhamlib all
	$(MAKE) -C ../lzham_codec-master/lzhamdecomp all
	$(MAKE) -C ../lzham_codec-master/lzhamcomp all

$(DLL): $(OBJECTS)
	$(CC) $(ARCHOVERRIDE) $(OBJECTS) $(LDFLAGS) -o $@

.cpp.o:
	$(CC) $(ARCHOVERRIDE) $(CFLAGS) $< -o $@

clean:
	rm -f *.o $(DLL)
	$(MAKE) -C ../lzham_codec-master/lzhamlib clean
	$(MAKE) -C ../lzham_codec-master/lzhamdecomp clean
	$(MAKE) -C ../lzham_codec-master/lzhamcomp clean
