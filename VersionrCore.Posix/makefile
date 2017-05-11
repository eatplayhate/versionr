CC=clang
CFLAGS=-fPIC -c -O3
LDFLAGS=-shared -lstdc++
SOURCES=FileSystem.cpp
OBJECTS=$(SOURCES:.c=.o)
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

.c.o:
	$(CC) $(ARCHOVERRIDE) $(CFLAGS) $< -o $@

clean:
	rm -f *.o $(DLL)