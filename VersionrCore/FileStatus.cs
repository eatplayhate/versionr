using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
namespace Versionr
{
    internal static class PosixFS
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void scandirdelegate(string name, long size, long timestamp, int attribs);

        [DllImport("VersionrCore.Posix")]
        public static extern void scandirs(string rootdir, scandirdelegate cback);

        static DateTime UnixTimeEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        public static List<FlatFSEntry> GetFlatEntries(string root)
        {
            List<FlatFSEntry> entries = new List<FlatFSEntry>();
            scandirs(root, (string name, long size, long timestamp, int attribs) =>
            {
                if (size == -1)
                {
                    entries.Add(new FlatFSEntry()
                    {
                        FullName = name + '/',
                        ChildCount = 0,
                        Attributes = 0,
                        FileTime = UnixTimeEpoch.Ticks + (timestamp * TimeSpan.TicksPerSecond),
                        Length = -1,
                    });
                }
                else if (size == -2)
                {
                    FlatFSEntry fs = entries[entries.Count - attribs - 1];
                    fs.ChildCount = attribs;
                    entries[entries.Count - attribs - 1] = fs;
                }
                else
                {
                    entries.Add(new FlatFSEntry()
                    {
                        FullName = name,
                        ChildCount = 0,
                        Attributes = 0,
                        FileTime = UnixTimeEpoch.Ticks + (timestamp * TimeSpan.TicksPerSecond),
                        Length = size,
                    });
                }
            });
            return entries;
        }
    }
    public struct FlatFSEntry
    {
        public string FullName;
        public int ChildCount;
        public int Attributes;
        public long FileTime;
        public long Length;
    };
    public class Entry
    {
        string m_Hash = null;
        public Area Area { get; set; }
        public string CanonicalName { get; set; }
        public string FullName { get; set; }
        public string LocalName { get; set; }
        public Entry Parent { get; set; }
        public FileInfo Info
        {
            get
            {
                return new FileInfo(FullName);
            }
        }
        public DirectoryInfo DirectoryInfo
        {
            get
            {
                return new DirectoryInfo(FullName);
            }
        }
        public string Hash
        {
            get
            {
                lock (this)
                {
                    if (m_Hash == null)
                        m_Hash = CheckHash(new FileInfo(FullName));
                    return m_Hash;
                }
            }
            set
            {
                m_Hash = value;
            }
        }
        public long Length { get; set; }
        public DateTime ModificationTime { get; set; }
        public Objects.Attributes Attributes { get; set; }
        public bool IsDirectory
        {
            get
            {
                return !IsSymlink && CanonicalName[CanonicalName.Length - 1] == '/';
            }
        }
		public bool IsSymlink
		{
			get
			{
                if ((Attributes & Objects.Attributes.Symlink) != 0)
                    return true;
                return false;
            }
		}
		public string SymlinkTarget
		{
			get
			{
                return Utilities.Symlink.GetTarget(FullName);
			}
		}
		public bool Ignored { get; private set; }

        public Entry(Area area, Entry parent, string cname, string fullname, string localName, long time, long size, bool ignored, FileAttributes attribs)
        {
            Area = area;
            Parent = parent;
            FullName = fullname;
            LocalName = localName;
            CanonicalName = localName == ".vrmeta" ? localName : cname;
            ModificationTime = DateTime.FromFileTimeUtc(time);
            Ignored = ignored;
            Length = size;

            if (IsDirectory)
            {
                m_Hash = string.Empty;
                if ((attribs & FileAttributes.ReparsePoint) != 0)
                {
                    Attributes = Attributes | Objects.Attributes.Symlink;
                    CanonicalName = CanonicalName.Substring(0, CanonicalName.Length - 1);
                }
            }
            else if (Utilities.Symlink.ExistsForFile(FullName, CanonicalName))
            {
                Attributes = Attributes | Objects.Attributes.Symlink;
                Length = 0;
            }
            if ((attribs & FileAttributes.Hidden) != 0)
                Attributes = Attributes | Objects.Attributes.Hidden;
            if ((attribs & FileAttributes.ReadOnly) != 0)
                Attributes = Attributes | Objects.Attributes.ReadOnly;
        }

        internal bool DataEquals(string fingerprint, long size)
        {
            return Length == size && Hash == fingerprint;
        }

        public string AbbreviatedHash
        {
            get
            {
                if (Hash == string.Empty)
                    return Hash;
                return Hash.Substring(0, 8) + "'" + Hash.Substring(Hash.Length - 8);
            }
        }

        public string Name
        {
            get
            {
                return LocalName;
            }
        }
        
        internal static string CheckHash(FileInfo info)
        {
#if SHOW_HASHES
            Printer.PrintDiagnostics("Hashing: {0}", info.FullName);
#endif
            using (var hasher = System.Security.Cryptography.SHA1.Create())
            using (var fs = info.OpenRead())
            {
                byte[] hashBytes = hasher.ComputeHash(fs);
                string hashValue = string.Empty;
                foreach (var h in hashBytes)
                    hashValue += string.Format("{0:X2}", h);
                return hashValue;
            }
        }
    }
    public static class FileSystemInfoExt
    {
        public static String GetFullNameWithCorrectCase(this FileSystemInfo fileOrFolder)
        {
            //Check whether null to simulate instance method behavior
            if (Object.ReferenceEquals(fileOrFolder, null)) throw new NullReferenceException();
            //Initialize common variables
            String myResult = GetCorrectCaseOfParentFolder(fileOrFolder.FullName);
            return myResult;
        }

        public static FileInfo GetCorrectCase(this FileInfo file)
        {
            if (Object.ReferenceEquals(file, null)) throw new NullReferenceException();
            //myParentFolder = GetLongPathName.Invoke(myFullName);
            String myFileName = Directory.GetFileSystemEntries(file.Directory.FullName, file.Name).FirstOrDefault();
            if (!Object.ReferenceEquals(myFileName, null))
            {
                return new FileInfo(Path.Combine(file.Directory.FullName, Path.GetFileName(myFileName)));
            }
            return file;
        }

        private static String GetCorrectCaseOfParentFolder(String fileOrFolder)
        {
            String myParentFolder = Path.GetDirectoryName(fileOrFolder);
            String myChildName = Path.GetFileName(fileOrFolder);
            if (Object.ReferenceEquals(myParentFolder, null)) return fileOrFolder.TrimEnd(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            if (Directory.Exists(myParentFolder))
            {
                //myParentFolder = GetLongPathName.Invoke(myFullName);
                String myFileOrFolder = Directory.GetFileSystemEntries(myParentFolder, myChildName).FirstOrDefault();
                if (!Object.ReferenceEquals(myFileOrFolder, null))
                {
                    myChildName = Path.GetFileName(myFileOrFolder);
                }
            }
            return GetCorrectCaseOfParentFolder(myParentFolder) + Path.DirectorySeparatorChar + myChildName;
        }

    }
    public class FileStatus
    {
        public List<Entry> Entries { get; set; }
        public FileStatus(Area root, DirectoryInfo rootFolder)
        {
            rootFolder = new DirectoryInfo(rootFolder.GetFullNameWithCorrectCase());
            Entries = GetEntryList(root, rootFolder, root.AdministrationFolder);
            int files = Entries.Where(x => !x.IsDirectory).Count();
            Printer.PrintDiagnostics("Current working directory has {0} file{1} in {2} director{3}.", files, files == 1 ? "" : "s", Entries.Count - files, (Entries.Count - files) == 1 ? "y" : "ies");
        }

        public static Func<string, List<FlatFSEntry>> GetFSFast = null;

        public static List<FlatFSEntry> GetFlatEntries(DirectoryInfo root)
        {
            List<FlatFSEntry> result = new List<FlatFSEntry>();
            GetEntriesRecursive(result, root.FullName.Replace('\\', '/') + "/", root);
            return result;
        }

        private static int GetEntriesRecursive(List<FlatFSEntry> result, string fn, DirectoryInfo root)
        {
            int count = 0;
            foreach (var x in root.GetFileSystemInfos())
            {
                if ((x.Attributes & FileAttributes.Directory) != 0)
                {
                    if (x.Name == "." || x.Name == ".." || x.Name == ".versionr")
                        continue;
                    FlatFSEntry fse = new FlatFSEntry()
                    {
                        Attributes = (int)x.Attributes,
                        FileTime = x.LastWriteTimeUtc.Ticks,
                        FullName = fn + x.Name + "/",
                        Length = -1,
                    };
                    int loc = result.Count;
                    result.Add(fse);
                    int cc = GetEntriesRecursive(result, fse.FullName, new DirectoryInfo(fn + x.Name));
                    fse.ChildCount = cc;
                    result[loc] = fse;
                    count += 1 + cc;
                }
                else
                {
                    FlatFSEntry fse = new FlatFSEntry()
                    {
                        Attributes = (int)x.Attributes,
                        FileTime = x.LastWriteTimeUtc.Ticks,
                        FullName = fn + x.Name,
                        Length = new FileInfo(fn + x.Name).Length,
                    };
                    result.Add(fse);
                    count++;
                }
            }
            return count;
        }

        struct FSScan
        {
            public System.Text.RegularExpressions.Regex[] FRIgnores;
            public System.Text.RegularExpressions.Regex[] FRIncludes;
            public string[] ExtIncludes;
            public string[] ExtIgnores;
        }

        public static List<Entry> GetEntryList(Area area, DirectoryInfo root, DirectoryInfo adminFolder)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Restart();
            if (!Utilities.MultiArchPInvoke.IsRunningOnMono)
            {
                if (GetFSFast == null)
                {
                    var asm = System.Reflection.Assembly.LoadFrom(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "/x64/VersionrCore.Win32.dll");
                    GetFSFast = asm.GetType("Versionr.Win32.FileSystem").GetMethod("EnumerateFileSystem").CreateDelegate(typeof(Func<string, List<FlatFSEntry>>)) as Func<string, List<FlatFSEntry>>;
                }
                if (GetFSFast != null)
                {
                    FSScan scan = new FSScan();
                    scan.FRIgnores = area?.Directives?.Ignore?.RegexFilePatterns;
                    scan.FRIncludes = area?.Directives?.Include?.RegexFilePatterns;
                    scan.ExtIgnores = area?.Directives?.Ignore?.Extensions;
                    scan.ExtIncludes = area?.Directives?.Include?.Extensions;
                    string fn = root.FullName.Replace('\\', '/');
                    if (fn[fn.Length - 1] != '/')
                        fn += '/';
                    sw.Restart();
                    var x = GetFSFast(fn);
                    sw.Restart();
                    List<Entry> e2 = new List<Entry>(x.Count);
                    System.Collections.Concurrent.ConcurrentBag<Entry> entries2 = new System.Collections.Concurrent.ConcurrentBag<Entry>();
                    System.Threading.CountdownEvent ce2 = new System.Threading.CountdownEvent(1);
                    ProcessListFast(scan, area, x, area.RootDirectory.FullName, ce2, entries2, 0, x.Count, null);
                    ce2.Signal();
                    ce2.Wait();
                    var ea = entries2.ToArray();
                    e2.Capacity = ea.Length;
                    e2.AddRange(ea);
                    return e2;
                }
            }
            else
            {
                try
                {
                    FSScan scan = new FSScan();
                    scan.FRIgnores = area?.Directives?.Ignore?.RegexFilePatterns;
                    scan.FRIncludes = area?.Directives?.Include?.RegexFilePatterns;
                    scan.ExtIgnores = area?.Directives?.Ignore?.Extensions;
                    scan.ExtIncludes = area?.Directives?.Include?.Extensions;
                    string fn = root.FullName.Replace('\\', '/');
                    if (fn[fn.Length - 1] != '/')
                        fn += '/';
                    sw.Restart();
                    var x = PosixFS.GetFlatEntries(fn);
                    sw.Restart();
                    List<Entry> e2 = new List<Entry>(x.Count);
                    System.Collections.Concurrent.ConcurrentBag<Entry> entries2 = new System.Collections.Concurrent.ConcurrentBag<Entry>();
                    System.Threading.CountdownEvent ce2 = new System.Threading.CountdownEvent(1);
                    ProcessListFast(scan, area, x, area.RootDirectory.FullName, ce2, entries2, 0, x.Count, null);
                    ce2.Signal();
                    ce2.Wait();
                    var ea = entries2.ToArray();
                    e2.Capacity = ea.Length;
                    e2.AddRange(ea);
                    return e2;
                }
                catch
                {
                    // eh
                }
            }
            sw.Restart();
            System.Collections.Concurrent.ConcurrentBag<Entry> entries = new System.Collections.Concurrent.ConcurrentBag<Entry>();
            System.Threading.CountdownEvent ce = new System.Threading.CountdownEvent(1);
            PopulateList(entries, ce, area, null, root, area.GetLocalPath(root.FullName), adminFolder, false);
            ce.Signal();
            ce.Wait();
            Entry[] entryArray = entries.ToArray();
            Array.Sort(entryArray, (Entry x, Entry y) => { return string.CompareOrdinal(x.CanonicalName, y.CanonicalName); });
            System.Console.WriteLine("3: {0}ms", sw.ElapsedMilliseconds);
            return entryArray.ToList();
        }

        private static void ProcessListFast(FSScan scan, Area area, List<FlatFSEntry> results, string rootFolder, System.Threading.CountdownEvent ce, System.Collections.Concurrent.ConcurrentBag<Entry> e2, int start, int end, Entry parentEntry = null, bool ignoreFiles = false)
        {
            for (int i = start; i < end; i++)
            {
                var r = results[i];
                if (r.Length == -1)
                {
                    bool ignoreDirectory = false;
                    bool ignoreContents = false;
                    string slashedSubdirectory = r.FullName.Substring(rootFolder.Length + 1);
                    if (area != null && area.Directives != null && area.Directives.Include != null)
                    {
                        ignoreDirectory = true;
                        foreach (var y in area.Directives.Include.Directories)
                        {
                            if (y.StartsWith(slashedSubdirectory, StringComparison.OrdinalIgnoreCase))
                            {
                                if (slashedSubdirectory.Length <= y.Length - 1)
                                    ignoreContents = true;
                                ignoreDirectory = false;
                                break;
                            }
                            else if (slashedSubdirectory.StartsWith(y))
                            {
                                ignoreDirectory = false;
                                break;
                            }
                        }
                        if (area.Directives.Include.RegexDirectoryPatterns != null)
                        {
                            foreach (var y in area.Directives.Include.RegexDirectoryPatterns)
                            {
                                if (y.IsMatch(slashedSubdirectory))
                                {
                                    ignoreDirectory = false;
                                    break;
                                }
                            }
                        }
                    }
                    if (area != null && area.Directives != null && area.Directives.Ignore != null && area.Directives.Ignore.RegexDirectoryPatterns != null)
                    {
                        string ssi = slashedSubdirectory.ToLowerInvariant();
                        foreach (var y in area.Directives.Ignore.Directories)
                        {
                            if (ssi.StartsWith(y, StringComparison.Ordinal))
                            {
                                ignoreDirectory = true;
                                break;
                            }
                        }
                        foreach (var y in area.Directives.Ignore.RegexDirectoryPatterns)
                        {
                            if (y.IsMatch(ssi))
                            {
                                ignoreDirectory = true;
                                break;
                            }
                        }
                    }

                    if (area != null && area.Directives != null && area.Directives.Externals != null)
                    {
                        foreach (var x in area.Directives.Externals)
                        {
                            string extdir = x.Value.Location.Replace('\\', '/');
                            if (extdir[extdir.Length - 1] != '/')
                                extdir += "/";
                            if (string.Equals(slashedSubdirectory, extdir, StringComparison.OrdinalIgnoreCase))
                            {
                                return;
                            }
                        }
                    }

                    var parent = new Entry(area, parentEntry, slashedSubdirectory, r.FullName, parentEntry == null ? slashedSubdirectory : r.FullName.Substring(parentEntry.FullName.Length), r.FileTime, 0, ignoreDirectory, (FileAttributes)r.Attributes);
                    e2.Add(parent);
                    if (ignoreDirectory)
                    {
                        i += r.ChildCount;
                    }
                    else
                    {
                        int s = i + 1;
                        int e = i + 1 + r.ChildCount;
                        if (r.ChildCount > 16)
                        {
                            ce.AddCount();
                            System.Threading.Tasks.Task.Factory.StartNew(() => { ProcessListFast(scan, area, results, rootFolder, ce, e2, s, e, parent, ignoreContents); ce.Signal(); });
                        }
                        else
                        {
                            ProcessListFast(scan, area, results, rootFolder, ce, e2, s, e, parent, ignoreContents);
                        }
                        i += r.ChildCount;
                    }
                }
                else
                {
                    if (!ignoreFiles)
                    {
                        string fn = r.FullName.Substring(rootFolder.Length + 1);
                        string fnI = fn.ToLowerInvariant();
                        bool ignored = false;
                        if (area != null && scan.FRIncludes != null)
                        {
                            ignored = true;
                            foreach (var y in scan.FRIncludes)
                            {
                                if (y.IsMatch(fn))
                                {
                                    ignored = false;
                                    break;
                                }
                            }
                        }
                        int lastIndex = fn.LastIndexOf('.');
                        if (lastIndex > 0)
                        {
                            int remainder = fn.Length - lastIndex;
                            if (!ignored && scan.ExtIncludes != null)
                            {
                                ignored = true;
                                foreach (var y in scan.ExtIncludes)
                                {
                                    if (y.Length == remainder && string.CompareOrdinal(fnI, lastIndex, y, 0, y.Length) == 0)
                                    {
                                        ignored = false;
                                        break;
                                    }
                                }
                            }
                            if (!ignored && scan.ExtIgnores != null)
                            {
                                foreach (var y in scan.ExtIgnores)
                                {
                                    if (y.Length == remainder && string.CompareOrdinal(fnI, lastIndex, y, 0, y.Length) == 0)
                                    {
                                        ignored = true;
                                        break;
                                    }
                                }
                            }
                        }
                        if (!ignored && scan.FRIgnores != null)
                        {
                            foreach (var y in scan.FRIgnores)
                            {
                                if (y.IsMatch(fnI))
                                {
                                    ignored = true;
                                    break;
                                }
                            }
                        }
                        e2.Add(new Entry(area, parentEntry, fn, r.FullName, parentEntry == null ? fn : r.FullName.Substring(parentEntry.FullName.Length), r.FileTime, r.Length, ignored, (FileAttributes)r.Attributes));
                    }
                }
            }
        }

        private static void PopulateList(System.Collections.Concurrent.ConcurrentBag<Entry> entries, System.Threading.CountdownEvent ce, Area area, Entry parentEntry, DirectoryInfo info, string subdirectory, DirectoryInfo adminFolder, bool ignoreDirectory)
        {
            if (area.InExtern(info))
                return;
            bool ignoreContents = false;
            string slashedSubdirectory = subdirectory;
            if (subdirectory != string.Empty)
            {
                if (slashedSubdirectory[slashedSubdirectory.Length - 1] != '/')
                    slashedSubdirectory += '/';
                if (area != null && area.Directives != null && area.Directives.Include != null)
                {
                    ignoreDirectory = true;
                    foreach (var y in area.Directives.Include.Directories)
                    {
                        if (y.StartsWith(slashedSubdirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            if (slashedSubdirectory.Length <= y.Length - 1)
                                ignoreContents = true;
                            ignoreDirectory = false;
                            break;
                        }
                        else if (slashedSubdirectory.StartsWith(y))
                        {
                            ignoreDirectory = false;
                            break;
                        }
                    }
                    if (area.Directives.Include.RegexDirectoryPatterns != null)
                    {
                        foreach (var y in area.Directives.Include.RegexDirectoryPatterns)
                        {
                            if (y.IsMatch(slashedSubdirectory))
                            {
                                ignoreDirectory = false;
                                break;
                            }
                        }
                    }
                }
                if (area != null && area.Directives != null && area.Directives.Ignore != null && area.Directives.Ignore.RegexDirectoryPatterns != null)
                {
                    foreach (var y in area.Directives.Ignore.Directories)
                    {
                        if (slashedSubdirectory.StartsWith(y, StringComparison.OrdinalIgnoreCase))
                        {
                            ignoreDirectory = true;
                            break;
                        }
                    }
                    foreach (var y in area.Directives.Ignore.RegexDirectoryPatterns)
					{
                        if (y.IsMatch(slashedSubdirectory))
						{
							ignoreDirectory = true;
							break;
						}
					}
				}
                
                if (area != null && area.Directives != null && area.Directives.Externals != null)
                {
                    foreach (var x in area.Directives.Externals)
                    {
                        string extdir = x.Value.Location.Replace('\\', '/');
                        if (extdir[extdir.Length - 1] != '/')
                            extdir += "/";
                        if (string.Equals(slashedSubdirectory, extdir, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
                }

				parentEntry = new Entry(area, parentEntry, slashedSubdirectory, info.FullName, info.Name, info.LastWriteTimeUtc.ToFileTimeUtc(), 0, ignoreDirectory, info.Attributes);
                entries.Add(parentEntry);
                if (ignoreDirectory)
                    return;
            }

			// Don't add children for symlinks.
			if (Utilities.Symlink.Exists(info))
				return;

            List<Task<List<Entry>>> tasks = new List<Task<List<Entry>>>();
            string prefix = string.IsNullOrEmpty(subdirectory) ? string.Empty : slashedSubdirectory;
            foreach (var x in info.GetFileSystemInfos())
            {
                string fn = x.Name;
                string name = prefix + fn;
                if ((x.Attributes & FileAttributes.Directory) != 0)
                {
                    if (fn.Equals(".", StringComparison.Ordinal) || fn.Equals("..", StringComparison.Ordinal))
                        continue;
                    
                    if (fn.Equals(".versionr", StringComparison.Ordinal))
                        continue;

#if DEBUG
                    if (true)
#else
                    if (Utilities.MultiArchPInvoke.IsRunningOnMono)
#endif
                    {
                        PopulateList(entries, ce, area, parentEntry, x as DirectoryInfo, name, adminFolder, ignoreDirectory);
                    }
                    else
                    {
                        ce.AddCount();
                        Utilities.LimitedTaskDispatcher.Factory.StartNew(() => { PopulateList(entries, ce, area, parentEntry, x as DirectoryInfo, name, adminFolder, ignoreDirectory); ce.Signal(); });
                    }
                }
                else if (!ignoreContents)
                {
                    bool ignored = ignoreDirectory;
                    if (!ignored && area != null && area.Directives != null && area.Directives.Include != null && area.Directives.Include.RegexFilePatterns != null)
                    {
                        ignored = true;
                        foreach (var y in area.Directives.Include.RegexFilePatterns)
                        {
                            if (y.IsMatch(name))
                            {
                                ignored = false;
                                break;
                            }
                        }
                    }
                    int lastIndex = fn.LastIndexOf('.');
                    if (lastIndex > 0)
                    {
                        string ext = fn.Substring(lastIndex);
                        if (!ignored && area != null && area.Directives != null && area.Directives.Include != null && area.Directives.Include.Extensions != null)
                        {
                            ignored = true;
                            foreach (var y in area.Directives.Include.Extensions)
                            {
                                if (ext.Equals(y, StringComparison.OrdinalIgnoreCase))
                                {
                                    ignored = false;
                                    break;
                                }
                            }
                        }
                        if (!ignored && area != null && area.Directives != null && area.Directives.Ignore != null && area.Directives.Ignore.Extensions != null)
                        {
                            foreach (var y in area.Directives.Ignore.Extensions)
                            {
                                if (ext.Equals(y, StringComparison.OrdinalIgnoreCase))
                                {
                                    ignored = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (!ignored && area != null && area.Directives != null && area.Directives.Ignore != null && area.Directives.Ignore.RegexFilePatterns != null)
                    {
                        foreach (var y in area.Directives.Ignore.RegexFilePatterns)
                        {
                            if (y.IsMatch(name))
                            {
                                ignored = true;
                                break;
                            }
                        }
                    }
                    entries.Add(new Entry(area, parentEntry, name, x.FullName, x.Name, x.LastWriteTimeUtc.ToFileTimeUtc(), new FileInfo(x.FullName).Length, ignored, x.Attributes));
                }
            }
        }
    }
}
