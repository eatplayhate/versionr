CC=clang
CFLAGS=-fPIC -c -O3 -DSQLITE_TEMP_STORE=2 -DSQLITE_DEFAULT_MEMSTATUS=0 -DSQLITE_DEFAULT_WAL_SYNCHRONOUS=1 -DSQLITE_MAX_EXPR_DEPTH=0 -DSQLITE_OMIT_DECLTYPE -DSQLITE_OMIT_DEPRECATED -DSQLITE_OMIT_SHARED_CACHE -DSQLITE_ENABLE_FTS5 -DSQLITE_ENABLE_STAT4
LDFLAGS=-shared -ldl -lpthread
SOURCES=shell.c sqlite3.c
OBJECTS=$(SOURCES:.c=.o)
ifeq ($(OS),Windows_NT)
	DLL=sqlite3.dll
else
    UNAME_S := $(shell uname -s)
    ifeq ($(UNAME_S),Darwin)
		DLL=libsqlite3.0.dylib
		ARCHOVERRIDE=-arch i386
    else
		DLL=libsqlite3.so.0
    endif
endif

all: $(SOURCES) $(DLL)

$(DLL): $(OBJECTS)
	$(CC) $(ARCHOVERRIDE) $(LDFLAGS) $(OBJECTS) -o $@

.c.o:
	$(CC) $(ARCHOVERRIDE) $(CFLAGS) $< -o $@

clean:
	rm *.o $(DLL)