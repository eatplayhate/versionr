cd /d %~dp0
"C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\devenv.exe" Versionr.sln /Rebuild Release
mkdir P:\Osiris\Versionr
robocopy bin P:\Osiris\Versionr /MIR




