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

        static DateTime UnixTimeEpoch = new DateTime(370, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        
        public static List<FlatFSEntry> GetFlatEntries(string root)
        {
            List<FlatFSEntry> entries = new List<FlatFSEntry>();
            if (root.EndsWith("/"))
                root = root.Substring(0, root.Length - 1);
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

        public bool IsVersionrRoot { get; set; } = false;

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
                if (localName == ".versionr/" && parent != null)
                    parent.IsVersionrRoot = true;
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

        private static readonly char[] s_Base16NibbleTable = new[]
        {
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'
        };

        internal static string CheckHash(FileInfo info)
        {
#if SHOW_HASHES
            Printer.PrintDiagnostics("Hashing: {0}", info.FullName);
#endif
            using (var hasher = System.Security.Cryptography.SHA1.Create())
            using (var fs = info.OpenRead())
            {
                byte[] hashBytes = hasher.ComputeHash(fs);

                char[] chararray = new char[hashBytes.Length * 2];
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    chararray[i * 2] = s_Base16NibbleTable[hashBytes[i] >> 4];
                    chararray[i * 2 + 1] = s_Base16NibbleTable[hashBytes[i] & 0xF];
                }

                return new string(chararray);
            }
        }
    }
    public static class FileSystemInfoExt
    {
        [DllImport("VersionrCore.Posix")]
        public static unsafe extern int getfullpath(string rootdir, byte* data, int dataLength);

        [DllImport("shlwapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool PathIsNetworkPath(string pszPath);

        private static Func<string, string> GetPathCorrectCase = null;
        private static bool? NetworkSafeMode = null;

        public static string GetFullPathNative(FileSystemInfo info)
        {
            if (Versionr.Utilities.MultiArchPInvoke.IsRunningOnMono)
            {
                try
                {
                    byte[] buffer = new byte[16535];
                    unsafe
                    {
                        fixed (byte* b = &buffer[0])
                        {
                            if (getfullpath(info.FullName, b, buffer.Length) == 1)
                            {
                                return Encoding.UTF8.GetString(buffer, 0, buffer.Count(x => x != 0));
                            }
                        }
                    }
                }
                catch
                {
                    return null;
                }
            }
            else
            {
                if (GetPathCorrectCase == null && !NetworkSafeMode.HasValue)
                {
                    try
                    {
                        var asm = System.Reflection.Assembly.LoadFrom(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "/x64/VersionrCore.Win32.dll");
                        GetPathCorrectCase = asm.GetType("Versionr.Win32.FileSystem").GetMethod("GetPathWithCorrectCase").CreateDelegate(typeof(Func<string, string>)) as Func<string, string>;
                    }
                    catch
                    {
                        NetworkSafeMode = true;
                    }
                }
                if (!NetworkSafeMode.HasValue)
                    NetworkSafeMode = PathIsNetworkPath(info.FullName);
                if (NetworkSafeMode == true)
                    return null;
                return GetPathCorrectCase(info.FullName);
            }
            return null;
        }

        public static String GetFullNameWithCorrectCase(this FileSystemInfo fileOrFolder)
        {
            //Check whether null to simulate instance method behavior
            if (Object.ReferenceEquals(fileOrFolder, null)) throw new NullReferenceException();
            string s1 = GetFullPathNative(fileOrFolder);
            if (s1 != null)
                return s1;
                
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
    public class FileTreeEntry
    {
        public Entry Object { get; set; }
        public FileTreeEntry Parent { get; set; }

        public FileTreeEntry()
        {
        }

        public FileTreeEntry(Entry e)
        {
            Object = e;
        }

        public bool IsRoot
        {
            get
            {
                return Object == null;
            }
        }
        public virtual bool IsDirectory
        {
            get
            {
                return false;
            }
        }
        public virtual bool IsEmpty
        {
            get
            {
                return false;
            }
        }
        public virtual bool IsEmptyStrict
        {
            get
            {
                return false;
            }
        }
        public virtual bool IsVersionrRoot
        {
            get
            {
                return false;
            }
        }
        public string FullName
        {
            get
            {
                if (Object != null)
                    return Object.FullName;
                return string.Empty;
            }
        }
        public string Name
        {
            get
            {
                if (Object != null)
                    return Object.Name;
                return string.Empty;
            }
        }

        public virtual IEnumerable<FileTreeEntry> Contents
        {
            get
            {
                return Enumerable.Empty<FileTreeEntry>();
            }
        }
        public virtual IEnumerable<FileTreeFolder> Folders
        {
            get
            {
                return Enumerable.Empty<FileTreeFolder>();
            }
        }
        public virtual IEnumerable<FileTreeEntry> Files
        {
            get
            {
                return Enumerable.Empty<FileTreeEntry>();
            }
        }
        public virtual FileTreeEntry this[string name]
        {
            get
            {
                return null;
            }
        }

        internal virtual void Initialize()
        {

        }

        internal virtual void Add(FileTreeEntry e)
        {
            throw new NotImplementedException();
        }
        public override string ToString()
        {
            return Object.CanonicalName;
        }
    }
    public class FileTreeFolder : FileTreeEntry
    {
        protected Lazy<Dictionary<string, FileTreeEntry>> m_Lookup;
        protected List<FileTreeFolder> m_Folders = new List<FileTreeFolder>();
        protected List<FileTreeEntry> m_Files = new List<FileTreeEntry>();

        public FileTreeFolder() : base()
        {
        }
        public FileTreeFolder(Entry e) : base(e)
        {
        }
        public override bool IsVersionrRoot
        {
            get
            {
                var lastEntry = m_Folders.TakeWhile(x => x.Name.CompareTo(".versionr/") <= 0).LastOrDefault();
                if (lastEntry != null)
                    return lastEntry.Name == ".versionr/";
                return false;
            }
        }
        public override bool IsEmpty
        {
            get
            {
                return m_Folders.Count == 0 && m_Files.Count == 0;
            }
        }
        public override bool IsEmptyStrict
        {
            get
            {
                return m_Files.Count == 0 && m_Folders.All(x => x.IsEmptyStrict);
            }
        }
        public override bool IsDirectory
        {
            get
            {
                return true;
            }
        }
        public override IEnumerable<FileTreeFolder> Folders
        {
            get
            {
                return m_Folders;
            }
        }
        public override IEnumerable<FileTreeEntry> Files
        {
            get
            {
                return m_Files;
            }
        }
        public override IEnumerable<FileTreeEntry> Contents
        {
            get
            {
                return m_Folders.Concat(m_Files);
            }
        }
        public override FileTreeEntry this[string name]
        {
            get
            {
                FileTreeEntry entry;
                if (m_Lookup.Value.TryGetValue(name, out entry))
                    return entry;
                return null;
            }
        }

        internal override void Initialize()
        {
            m_Files.Sort((a, b) => (b.Name.CompareTo(a.Name)));
            m_Folders.Sort((a, b) => (b.Name.CompareTo(a.Name)));

            m_Lookup = new Lazy<Dictionary<string, FileTreeEntry>>(() =>
            {
                Dictionary<string, FileTreeEntry> d = new Dictionary<string, FileTreeEntry>();
                foreach (var x in Contents)
                {
                    d[x.Object.Name] = x;
                }
                return d;
            });
        }
        internal override void Add(FileTreeEntry e)
        {
            e.Parent = this;
            if (e.IsDirectory)
                m_Folders.Add(e as FileTreeFolder);
            else
                m_Files.Add(e);
        }
    }
    public class FileStatus
    {
        public List<Entry> Entries { get; set; }
        public FileTreeEntry Root { get; set; }

        public FileStatus(Area root, DirectoryInfo rootFolder)
        {
            rootFolder = new DirectoryInfo(rootFolder.GetFullNameWithCorrectCase());
            Entries = GetEntryList(root, rootFolder, root.AdministrationFolder);
            BuildTree();
        }

        private void BuildTree()
        {
            Root = new FileTreeFolder();
            Dictionary<Entry, FileTreeEntry> mapping = new Dictionary<Entry, FileTreeEntry>();
            int files = 0;
            foreach (var x in Entries)
            {
                var fte = GetEntry(mapping, x);
                if (!fte.IsDirectory)
                    files++;
            }
            foreach (var x in mapping)
            {
                x.Value.Initialize();
            }
            Printer.PrintDiagnostics("Current working directory has {0} file{1} in {2} director{3}.", files, files == 1 ? "" : "s", Entries.Count - files, (Entries.Count - files) == 1 ? "y" : "ies");
        }

        private FileTreeEntry GetEntry(Dictionary<Entry, FileTreeEntry> mapping, Entry x)
        {
            if (x == null)
                return Root;
            FileTreeEntry parentEntry;
            if (mapping.TryGetValue(x, out parentEntry))
                return parentEntry;
            FileTreeEntry fte = x.IsDirectory ? new FileTreeFolder(x) : new FileTreeEntry(x);
            mapping.Add(x, fte);
            GetEntry(mapping, x.Parent).Add(fte);
            return fte;
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
                    if (x.Name == "." || x.Name == "..")
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
                    int cc = 0;
                    if (x.Name != ".versionr")
                        cc = GetEntriesRecursive(result, fse.FullName, new DirectoryInfo(fn + x.Name));
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

        private static List<Entry> GetEntryList(Area area, DirectoryInfo root, DirectoryInfo adminFolder)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Restart();
            try
            {
                Func<string, List<FlatFSEntry>> nativeGenerator = null;
                if (!Utilities.MultiArchPInvoke.IsRunningOnMono)
                {
                    if (GetFSFast == null)
                    {
                        var asm = System.Reflection.Assembly.LoadFrom(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "/x64/VersionrCore.Win32.dll");
                        GetFSFast = asm.GetType("Versionr.Win32.FileSystem").GetMethod("EnumerateFileSystem").CreateDelegate(typeof(Func<string, List<FlatFSEntry>>)) as Func<string, List<FlatFSEntry>>;
                    }
                    nativeGenerator = GetFSFast;
                }
                else
                {
                    nativeGenerator = PosixFS.GetFlatEntries;
                }

                List<FlatFSEntry> flatEntries = null;
                if (!Utilities.MultiArchPInvoke.IsRunningOnMono)
                {
                }

                if (nativeGenerator != null)
                {
                    string fn = root.FullName.Replace('\\', '/');
                    if (fn[fn.Length - 1] != '/')
                        fn += '/';
                    System.Net.Sockets.Socket socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                    if (flatEntries == null)
                    {
                        Printer.PrintDiagnostics("#q#Using native file scanner...##");
                        sw.Restart();
                        flatEntries = nativeGenerator(fn);
                        if (area.GetLocalPath(fn) != "")
                            flatEntries.Insert(0, new FlatFSEntry() { Attributes = (int)root.Attributes, ChildCount = flatEntries.Count, Length = -1, FileTime = root.LastWriteTimeUtc.Ticks, FullName = fn });
                        sw.Restart();
                    }
                }
                if (flatEntries != null)
                {
                    FSScan scan = new FSScan();
                    scan.FRIgnores = area?.Directives?.Ignore?.RegexFilePatterns;
                    scan.FRIncludes = area?.Directives?.Include?.RegexFilePatterns;
                    scan.ExtIgnores = area?.Directives?.Ignore?.Extensions;
                    scan.ExtIncludes = area?.Directives?.Include?.Extensions;
                    List<Entry> e2 = new List<Entry>(flatEntries.Count);
                    System.Collections.Concurrent.ConcurrentBag<Entry> entries2 = new System.Collections.Concurrent.ConcurrentBag<Entry>();
                    System.Threading.CountdownEvent ce2 = new System.Threading.CountdownEvent(1);
                    ProcessListFast(scan, area, flatEntries, area.RootDirectory.FullName, ce2, entries2, 0, flatEntries.Count, null);
                    ce2.Signal();
                    ce2.Wait();
                    var ea = entries2.ToArray();
                    e2.Capacity = ea.Length;
                    e2.AddRange(ea);
                    return e2;
                }
            }
            catch (System.Exception e)
            {
                // try again with the slow mode
                Printer.PrintDiagnostics("#q#Couldn't use fast scanners {0}##", e);
            }
            Printer.PrintDiagnostics("#q#Using fallback file scanner...##");
            sw.Restart();
            System.Collections.Concurrent.ConcurrentBag<Entry> entries = new System.Collections.Concurrent.ConcurrentBag<Entry>();
            System.Threading.CountdownEvent ce = new System.Threading.CountdownEvent(1);
            PopulateList(entries, ce, area, null, root, area.GetLocalPath(root.FullName), adminFolder, false);
            ce.Signal();
            ce.Wait();
            Entry[] entryArray = entries.ToArray();
            Array.Sort(entryArray, (Entry x, Entry y) => { return string.CompareOrdinal(x.CanonicalName, y.CanonicalName); });
            return entryArray.ToList();
        }

        private static void ProcessListFast(FSScan scan, Area area, List<FlatFSEntry> results, string rootFolder, System.Threading.CountdownEvent ce, System.Collections.Concurrent.ConcurrentBag<Entry> e2, int start, int end, Entry parentEntry = null, bool ignoreFiles = false)
        {
            int rflen = rootFolder.Length;
            if (rootFolder[rflen - 2] == ':')
                rflen--;
            rflen++;
            for (int i = start; i < end; i++)
            {
                var r = results[i];
                if (r.Length == -1)
                {
                    bool ignoreDirectory = false;
                    bool ignoreContents = false;
                    bool hide = false;
                    string slashedSubdirectory = r.FullName.Substring(rflen);

                    if (slashedSubdirectory == ".versionr/")
                    {
                        if (parentEntry == null)
                        {
                            if (r.ChildCount != 0)
                                throw new Exception();
                            continue;
                        }
                    }

                    CheckDirectoryIgnores(area, slashedSubdirectory, ref ignoreDirectory, ref ignoreContents, ref hide);

                    if (hide)
                    {
                        i += r.ChildCount;
                        continue;
                    }

                    var parent = new Entry(area, parentEntry, slashedSubdirectory, r.FullName, parentEntry == null ? slashedSubdirectory : r.FullName.Substring(parentEntry.FullName.Length), r.FileTime, 0, ignoreDirectory, (FileAttributes)r.Attributes);

                    if (parent.IsSymlink)
                    {
                        i += r.ChildCount;
                        continue;
                    }

                    e2.Add(parent);
                    if (ignoreDirectory)
                    {
                        // have to find child files and mark them specifically as ignored
                        int next = r.ChildCount + i + 1;
                        for (int x = i + 1; x < next; x++)
                        {
                            if (results[x].Length != -1)
                            {
                                var f = results[x];
                                string fn = f.FullName.Substring(rflen);

                                var ignoredFile = new Entry(area, parent, fn, f.FullName, f.FullName.Substring(parent.FullName.Length), f.FileTime, f.Length, true, (FileAttributes)f.Attributes);
                                e2.Add(ignoredFile);
                            }
                            else
                                x += results[x].ChildCount;
                        }
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
                        string fn = r.FullName.Substring(rflen);
                        string fnI = fn.ToLowerInvariant();
                        bool ignored = CheckFileIgnores(area, fn, fnI, scan.FRIncludes, scan.FRIgnores, scan.ExtIncludes, scan.ExtIgnores);
                        e2.Add(new Entry(area, parentEntry, fn, r.FullName, parentEntry == null ? fn : r.FullName.Substring(parentEntry.FullName.Length), r.FileTime, r.Length, ignored, (FileAttributes)r.Attributes));
                    }
                }
            }
        }

        private static bool CheckFileIgnores(Area area, string fn, string fnI, System.Text.RegularExpressions.Regex[] frIncludes, System.Text.RegularExpressions.Regex[] frIgnores, string[] extIncludes, string[] extIgnores)
        {
            bool ignored = false;
            if (area != null && frIncludes != null)
            {
                ignored = true;
                foreach (var y in frIncludes)
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
                if (!ignored && extIncludes != null)
                {
                    ignored = true;
                    foreach (var y in extIncludes)
                    {
                        if (y.Length == remainder && string.CompareOrdinal(fnI, lastIndex, y, 0, y.Length) == 0)
                        {
                            ignored = false;
                            break;
                        }
                    }
                }
                if (!ignored && extIgnores != null)
                {
                    foreach (var y in extIgnores)
                    {
                        if (y.Length == remainder && string.CompareOrdinal(fnI, lastIndex, y, 0, y.Length) == 0)
                        {
                            ignored = true;
                            break;
                        }
                    }
                }
            }
            if (!ignored && frIgnores != null)
            {
                foreach (var y in frIgnores)
                {
                    if (y.IsMatch(fnI))
                    {
                        ignored = true;
                        break;
                    }
                }
            }
            return ignored;
        }

        private static void CheckDirectoryIgnores(Area area, string slashedSubdirectory, ref bool ignoreDirectory, ref bool ignoreContents, ref bool hide)
        {
            if (area?.Directives?.Include != null)
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
            if (area?.Directives?.Ignore != null)
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
                if (area.Directives.Ignore.RegexDirectoryPatterns != null)
                {
                    foreach (var y in area.Directives.Ignore.RegexDirectoryPatterns)
                    {
                        if (y.IsMatch(ssi))
                        {
                            ignoreDirectory = true;
                            break;
                        }
                    }
                }
            }

            if (area?.Directives?.Externals != null)
            {
                foreach (var x in area.Directives.Externals)
                {
                    string extdir = x.Value.Location.Replace('\\', '/');
                    if (extdir[extdir.Length - 1] != '/')
                        extdir += "/";
                    if (string.Equals(slashedSubdirectory, extdir, StringComparison.OrdinalIgnoreCase))
                    {
                        hide = true;
                        return;
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

                bool hide = false;
                if (slashedSubdirectory == ".versionr/")
                {
                    if (parentEntry == null)
                        return;
                    else
                        parentEntry.IsVersionrRoot = true;
                }

                CheckDirectoryIgnores(area, slashedSubdirectory, ref ignoreDirectory, ref ignoreContents, ref hide);

                if (hide)
                    return;

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
                if (fn.EndsWith(".pch"))
                {
                    int qx = 0;
                }
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
                        area.GetTaskFactory().StartNew(() => { PopulateList(entries, ce, area, parentEntry, x as DirectoryInfo, name, adminFolder, ignoreDirectory); ce.Signal(); });
                    }
                }
                else if (!ignoreContents)
                {
                    bool ignored = CheckFileIgnores(area, name, name.ToLowerInvariant(), area?.Directives?.Include?.RegexFilePatterns, area?.Directives?.Ignore?.RegexFilePatterns, area?.Directives?.Include?.Extensions, area?.Directives?.Ignore?.Extensions);
                    entries.Add(new Entry(area, parentEntry, name, x.FullName, x.Name, x.LastWriteTimeUtc.ToFileTimeUtc(), new FileInfo(x.FullName).Length, ignored, x.Attributes));
                }
            }
        }
    }
}
