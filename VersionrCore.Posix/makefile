CC=clang
CFLAGS=-fPIC -c -O3 -std=c++14
LDFLAGS=-shared -lstdc++
SOURCES=FileSystem.cpp
OBJECTS=$(SOURCES:.cpp=.o)
UNAME_S := $(shell uname -s)
ifeq ($(UNAME_S),Darwin)
	DLL=libVersionrCore.Posix.dylib
	ARCHOVERRIDE=-arch i386
else
	DLL=libVersionrCore.Posix.so
endif

all: $(SOURCES) $(DLL)

$(DLL): $(OBJECTS)
	$(CC) $(ARCHOVERRIDE) $(OBJECTS) $(LDFLAGS) -o $@

.cpp.o:
	$(CC) $(ARCHOVERRIDE) $(CFLAGS) $< -o $@

clean:
	rm -f *.o $(DLL)
