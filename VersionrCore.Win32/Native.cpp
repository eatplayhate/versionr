#include <Windows.h>
#include <stdio.h>

void EnumerateFSC(int& count, const char* folder, int container, int& index)
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
				{
					if ((fndA.cFileName[1] == 0 || fndA.cFileName[1] == '.'))
						continue;
					if (strcmp(fndA.cFileName, ".versionr") == 0)
						continue;
				}
				count++;
				sprintf(filetemp, "%s%s/", folder, fndA.cFileName);
				EnumerateFSC(count, filetemp, ++index, index);
			}
			else
			{
				sprintf(filetemp, "%s%s", folder, fndA.cFileName);
				count++;
				unsigned long long fsize = fndA.nFileSizeLow | ((unsigned long long)fndA.nFileSizeHigh << 32);
			}
		} while (FindNextFileA(hnd, &fndA));
		FindClose(hnd);
	}
}