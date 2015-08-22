OUTPUT_DIRECTORY=bin

all:
	$(MAKE) -C lzhamwrapper all
	$(MAKE) -C lzhl-master all
	$(MAKE) -C sqlite3 all
	$(shell test -d $(OUTPUT_DIRECTORY) || mkdir -p $(OUTPUT_DIRECTORY))
	$(shell cp lzhl-master/*.so lzhl-master/*.dylib bin)
	$(shell cp lzhamwrapper/*.so lzhamwrapper/*.dylib bin)
	$(shell cp sqlite3/*.so.0 sqlite3/*.dylib bin)
	cp Dependencies/*.dll ./bin
	cp References/*.dll ./bin

clean:
	$(MAKE) -C lzhamwrapper clean
	$(MAKE) -C lzhl-master clean
	$(MAKE) -C sqlite3 clean
	rm -rf ./bin
