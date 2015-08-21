all:
	$(MAKE) -C liblzhamwrapper all
	$(MAKE) -C lzhl-master all
	$(MAKE) -C sqlite3 all
	mkdir bin
	cp lzhl-master/*.so ./bin
	cp lzhl-master/*.dylib ./bin
	cp liblzhamwrapper/*.so ./bin
	cp liblzhamwrapper/*.dylib ./bin
	cp sqlite3/*.so ./bin
	cp sqlite3/*.dylib ./bin
	cp Dependencies/*.dll ./bin
	cp References/*.dll ./bin

clean:
	$(MAKE) -C liblzhamwrapper clean
	$(MAKE) -C lzhl-master clean
	$(MAKE) -C sqlite3 clean
	rm -rf ./bin
