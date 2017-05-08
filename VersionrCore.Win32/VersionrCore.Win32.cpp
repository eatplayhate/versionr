// This is the main DLL file.

#include "stdafx.h"

#include "VersionrCore.Win32.h"
#include <stdio.h>
#include <msclr/marshal.h>

void EnumerateFS(System::Collections::Generic::List<Versionr::FlatFSEntry>^ files, const char* folder, int container, int& index)
{
	char foldertemp[2048];
	sprintf(foldertemp, "%s*", folder);
	WIN32_FIND_DATAA fndA;
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
					continue;
				sprintf(filetemp, "%s%s/", folder, fndA.cFileName);
				Versionr::FlatFSEntry e;
				e.Attributes = fndA.dwFileAttributes;
				e.ContainerID = container;
				e.DirectoryID = ++index;
				e.FileTime = time;
				e.Length = -1;
				e.FullName = gcnew System::String(filetemp);
				files->Add(e);
				EnumerateFS(files, filetemp, e.DirectoryID, index);
			}
			else
			{
				sprintf(filetemp, "%s%s", folder, fndA.cFileName);
				unsigned long long fsize = fndA.nFileSizeLow | ((unsigned long long)fndA.nFileSizeHigh << 32);
				Versionr::FlatFSEntry e;
				e.Attributes = fndA.dwFileAttributes;
				e.ContainerID = container;
				e.DirectoryID = -1;
				e.FileTime = time;
				e.Length = fsize;
				e.FullName = gcnew System::String(filetemp);
				files->Add(e);
			}
		} while (FindNextFileA(hnd, &fndA));
		FindClose(hnd);
	}
}

namespace Versionr
{
	namespace Win32
	{
		System::Collections::Generic::List<Versionr::FlatFSEntry>^ FileSystem::EnumerateFileSystem(System::String^ fs)
		{
			msclr::interop::marshal_context context;
			int index = 0;
			System::Collections::Generic::List<Versionr::FlatFSEntry>^ lf = gcnew System::Collections::Generic::List<Versionr::FlatFSEntry>();
			EnumerateFS(lf, context.marshal_as<const char*>(fs), 0, index);
			return lf;
		}
	}
}