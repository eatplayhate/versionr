CentOS is the retarded Linux. As such, the default `mono-devel` package
failed (for me) to produce an executable that can work with a remote server
(e.g. any clone, pull, push will fail)

The solution that I found:
1. if the default `mono-devel` package is installed, uninstall it
   # sudo yum remove mono-devel
   # sudo yum clear all
(then go and wipe the yum cache too, the 'yum clear all' will tell you where)

2. install the latest distribution as instructed on the mon-project.com site
e.g. https://www.mono-project.com/download/stable/#download-lin-centos

3. instal the latest available llvm/clang package from the centos software 
collection:
See: https://www.softwarecollections.org/en/scls/rhscl/devtoolset-7/
** The specific solution for llvm/clang is:
https://stackoverflow.com/a/48103599

4. In the immortal words of lstrudwick you can now "Just make it".
The "PostBuildEvent" in the Versionr.csproj is Windows specific, but you'll 
know what to do.

To test the executable, the "mono vsr.exe --help" or "mono vsr.exe init" are
not sufficient, go nuts and clone a remote repository.

Cheers,
@acolomitchi

