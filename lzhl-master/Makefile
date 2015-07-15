CC=clang
CFLAGS=-fPIC -c -O3
LDFLAGS=-shared -lstdc++
SOURCES=$(wildcard *.cpp)
OBJECTS=$(SOURCES:.cpp=.o)
UNAME_S := $(shell uname -s)
ifeq ($(UNAME_S),Darwin)
	DLL=liblzhl.dylib
	ARCHOVERRIDE=-arch i386
else
	DLL=liblzhl.so
endif

all: $(SOURCES) $(DLL)

$(DLL): $(OBJECTS)
	$(CC) $(ARCHOVERRIDE) $(OBJECTS) $(LDFLAGS) -o $@

.cpp.o:
	$(CC) $(ARCHOVERRIDE) $(CFLAGS) $< -o $@

clean:
	rm -f *.o $(DLL)
