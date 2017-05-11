// VersionrCore.Win32.h

#pragma once

#include <Windows.h>

using namespace System;

namespace Versionr
{
	namespace Win32
	{
		public ref class FileSystem abstract sealed
		{
		public:
			static System::Collections::Generic::List<Versionr::FlatFSEntry>^ EnumerateFileSystem(System::String^ fs);
			static int EnumerateFileSystemX(System::String^ fs);
		};
	}
}