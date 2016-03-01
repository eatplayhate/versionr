cd /d %~dp0
"C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\devenv.exe" Versionr.sln /Rebuild Release
mkdir Z:\Temp\LStrudwick\Versionr-Release
robocopy bin Z:\Temp\LStrudwick\Versionr-Release /MIR