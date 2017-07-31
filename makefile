OUTPUT_DIRECTORY=bin
UNAME_S := $(shell uname -s)
ifeq ($(UNAME_S),Darwin)
	LIBEXTS = *.dylib
else
	LIBEXTS = *.so
endif

all:
	$(MAKE) -C lzhamwrapper all
	$(MAKE) -C lzhl-master all
	$(MAKE) -C sqlite3 all
	$(MAKE) -C XDiffEngine all
	$(MAKE) -C VersionrCore.Posix all
	$(shell test -d $(OUTPUT_DIRECTORY) || mkdir -p $(OUTPUT_DIRECTORY))
	cp lzhl-master/$(LIBEXTS) bin
	cp lzhamwrapper/$(LIBEXTS) bin
	cp XDiffEngine/$(LIBEXTS) bin
	cp VersionrCore.Posix/$(LIBEXTS) bin
	cp sqlite3/$(LIBEXTS) bin

	cp References/*.dll ./bin
	cd Versionr; \
		xbuild /p:Configuration=Release
	echo "Completed"

clean:
	$(MAKE) -C lzhamwrapper clean
	$(MAKE) -C lzhl-master clean
	$(MAKE) -C sqlite3 clean
	$(MAKE) -C XDiffEngine clean
	$(MAKE) -C VersionrCore.Posix clean
	rm -rf ./bin
