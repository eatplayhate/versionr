#include <unistd.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <dirent.h>
#include <stdio.h>
#include <map>

int scanrec(DIR* dir, const char* path, void (*handler)(char* name, long long size, long long timestamp, int attribs))
{
    char fn[2048];
    int count = 0;
    long long fsize = 0;
    struct dirent* in;
    struct stat stbuf;
    while ((in = readdir(dir)))
    {
        if (in->d_name[0] == '.')
        {
            if (in->d_name[1] == '.' || in->d_name[1] == 0)
                continue;
            if (strcmp(in->d_name, ".versionr") == 0)
                continue;
        }
        if (in->d_type == DT_DIR)
        {
            sprintf(fn, "%s/%s", path, in->d_name);
            lstat(fn, &stbuf);
            handler(fn, (long long)-1, (long long)stbuf.st_mtime, 0);
            DIR* subdir = opendir(fn);
            int ccount = scanrec(subdir, fn, handler);
            handler(fn, (long long)-2, (long long)stbuf.st_mtime, ccount);
            count += 1 + ccount;
            closedir(subdir);
        }
        else
        {
            sprintf(fn, "%s/%s", path, in->d_name);
            lstat(fn, &stbuf);
            handler(fn, (long long)stbuf.st_size, (long long)stbuf.st_mtime, 0);
            count++;
        }
    }
    return count;
}

__attribute__((visibility("default"))) extern "C" void scandirs(char* root, void (*handler)(char* name, long long size, long long timestamp, int attribs))
{
    DIR* dir = opendir(root);
    scanrec(dir, root, handler);
    closedir(dir);
}