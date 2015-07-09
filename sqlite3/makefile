CC=clang
CFLAGS=-c -O3
LDFLAGS=-lpthreads
SOURCES=shell.c sqlite3.c
OBJECTS=$(SOURCES:.c=.o)
ifeq ($(OS),Windows_NT)
	DLL=sqlite3.dll
else
    UNAME_S := $(shell uname -s)
    ifeq ($(UNAME_S),Darwin)
		DLL=sqlite3.dylib
    else
		DLL=sqlite3.so
    endif
endif

all: $(SOURCES) $(DLL)

$(DLL): $(OBJECTS)
	$(CC) $(LDFLAGS) $(OBJECTS) -o $@

.c.o:
	$(CC) $(CFLAGS) $< -o $@

clean:
	rm *.o $(DLL)