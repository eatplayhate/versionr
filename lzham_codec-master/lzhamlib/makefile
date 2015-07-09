CC=clang
CFLAGS=-fPIC -c -O3 -I../include -I../lzhamdecomp -I../lzhamcomp
SOURCES=lzham_lib.cpp
OBJECTS=$(SOURCES:.cpp=.o)
LIB=../liblzhamlib.a
UNAME_S := $(shell uname -s)
ifeq ($(UNAME_S),Darwin)
	ARCHOVERRIDE=-arch i386
endif

all: $(SOURCES) $(LIB)

$(LIB): $(OBJECTS)
	ar rcs $@ $(OBJECTS)

.cpp.o:
	$(CC) $(ARCHOVERRIDE) $(CFLAGS) $< -o $@

clean:
	rm -f *.o $(LIB)