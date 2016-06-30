CC=clang
CFLAGS=-fPIC -c -O3 -I../libxdiff-0.23/xdiff
LDFLAGS=-shared -lstdc++
SOURCES=$(wildcard ../libxdiff-0.23/xdiff/*.c)
OBJECTS=$(SOURCES:.c=.o)
UNAME_S := $(shell uname -s)
ifeq ($(UNAME_S),Darwin)
	DLL=libXDiffEngine.dylib
	ARCHOVERRIDE=-arch i386
else
	DLL=libXDiffEngine.so
endif

all: $(SOURCES) $(DLL)

$(DLL): $(OBJECTS)
	$(CC) $(ARCHOVERRIDE) $(OBJECTS) $(LDFLAGS) -o $@

.c.o:
	$(CC) $(ARCHOVERRIDE) $(CFLAGS) $< -o $@

clean:
	rm -f *.o $(DLL)