// VersionrCore.Win32.h

#pragma once

#include <Windows.h>

using namespace System;
using namespace System::Runtime::InteropServices;

namespace Versionr
{
	namespace Win32
	{
		public ref class FileSystem abstract sealed
		{
		public:
			static System::Collections::Generic::List<Versionr::FlatFSEntry>^ EnumerateFileSystem(System::String^ fs);
			static int EnumerateFileSystemX(System::String^ fs);
			static System::String^ GetPathWithCorrectCase(System::String^ fs);

			static System::String^ GetFullPath(System::String^ path);
			static bool Exists(System::String^ path);
			static DWORD GetAttributes(System::String^ path);
			static void ReadData(System::String^ path,
				[Out]DateTime% created,
				[Out]DateTime% accessed,
				[Out]DateTime% written,
				[Out]UInt64% size);

			static void CreateDirectory(System::String^ path);
		};
	}
}