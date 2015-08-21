CC=clang
CFLAGS=-fPIC -c -O3
LDFLAGS=-shared -ldl -lpthread
SOURCES=shell.c sqlite3.c
OBJECTS=$(SOURCES:.c=.o)
ifeq ($(OS),Windows_NT)
	DLL=sqlite3.dll
else
    UNAME_S := $(shell uname -s)
    ifeq ($(UNAME_S),Darwin)
		DLL=libsqlite3.dylib
    else
		DLL=libsqlite3.so.0
    endif
endif

all: $(SOURCES) $(DLL)

$(DLL): $(OBJECTS)
	$(CC) $(LDFLAGS) $(OBJECTS) -o $@

.c.o:
	$(CC) $(CFLAGS) $< -o $@

clean:
	rm *.o $(DLL)