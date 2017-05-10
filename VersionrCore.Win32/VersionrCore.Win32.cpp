// This is the main DLL file.

#include "stdafx.h"

#include "VersionrCore.Win32.h"
#include <stdio.h>
#include <msclr/marshal.h>

int EnumerateFS(System::Collections::Generic::List<Versionr::FlatFSEntry>^ files, const char* folder)
{
	char foldertemp[2048];
	sprintf(foldertemp, "%s*", folder);
	WIN32_FIND_DATAA fndA;
	int count = 0;
	HANDLE hnd = FindFirstFileExA(foldertemp, FindExInfoBasic, &fndA, FindExSearchNameMatch, NULL, 0);
	if (hnd != INVALID_HANDLE_VALUE)
	{
		char filetemp[2048];
		do
		{
			unsigned long long time = fndA.ftLastWriteTime.dwLowDateTime | ((unsigned long long)fndA.ftLastWriteTime.dwHighDateTime << 32);
			if ((fndA.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
			{
				if (fndA.cFileName[0] == '.')
				{
					if ((fndA.cFileName[1] == 0 || fndA.cFileName[1] == '.'))
						continue;
					if (strcmp(fndA.cFileName, ".versionr") == 0)
						continue;
				}
				sprintf(filetemp, "%s%s/", folder, fndA.cFileName);
				Versionr::FlatFSEntry e;
				e.Attributes = fndA.dwFileAttributes;
				e.FileTime = time;
				e.Length = -1;
				e.FullName = gcnew System::String(filetemp);
				int loc = files->Count;
				files->Add(e);
				int cc = EnumerateFS(files, filetemp);
				e.ChildCount = cc;
				files[loc] = e;
				count += 1 + cc;
			}
			else
			{
				sprintf(filetemp, "%s%s", folder, fndA.cFileName);
				unsigned long long fsize = fndA.nFileSizeLow | ((unsigned long long)fndA.nFileSizeHigh << 32);
				Versionr::FlatFSEntry e;
				e.Attributes = fndA.dwFileAttributes;
				e.ChildCount = 0;
				e.FileTime = time;
				e.Length = fsize;
				e.FullName = gcnew System::String(filetemp);
				files->Add(e);
				count++;
			}
		} while (FindNextFileA(hnd, &fndA));
		FindClose(hnd);
	}
	return count;
}

void EnumerateFSC(int& count, const char* folder, int container, int& index);

namespace Versionr
{
	namespace Win32
	{
		System::Collections::Generic::List<Versionr::FlatFSEntry>^ FileSystem::EnumerateFileSystem(System::String^ fs)
		{
			msclr::interop::marshal_context context;
			System::Collections::Generic::List<Versionr::FlatFSEntry>^ lf = gcnew System::Collections::Generic::List<Versionr::FlatFSEntry>();
			EnumerateFS(lf, context.marshal_as<const char*>(fs));
			return lf;
		}
		int FileSystem::EnumerateFileSystemX(System::String^ fs)
		{
			msclr::interop::marshal_context context;
			int index = 0;
			int count = 0;
			EnumerateFSC(count, context.marshal_as<const char*>(fs), 0, index);
			return count;
		}
	}
}