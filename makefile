OUTPUT_DIRECTORY=bin

all:
	$(MAKE) -C lzhamwrapper all
	$(MAKE) -C lzhl-master all
	$(MAKE) -C sqlite3 all
	$(shell test -d $(OUTPUT_DIRECTORY) || mkdir -p $(OUTPUT_DIRECTORY))
	cp lzhl-master/*.so bin
	cp lzhamwrapper/*.so bin
	cp sqlite3/*.so.0 bin
	cp References/*.dll ./bin
	cd Versionr; \
		xbuild /p:Configuration=Release
	echo "Completed"

clean:
	$(MAKE) -C lzhamwrapper clean
	$(MAKE) -C lzhl-master clean
	$(MAKE) -C sqlite3 clean
	rm -rf ./bin
