using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
    public class Entry
    {
        string m_Hash = null;
        public Area Area { get; set; }
        public string CanonicalName { get; set; }
        public FileInfo Info { get; set; }
        public DirectoryInfo DirectoryInfo { get; set; }
        public Entry Parent { get; set; }
        public string Hash
        {
            get
            {
                lock (this)
                {
                    if (m_Hash == null)
                        m_Hash = CheckHash(Info);
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
                return !IsSymlink && CanonicalName.EndsWith("/");
            }
        }
		public bool IsSymlink
		{
			get
			{
				return Attributes.HasFlag(Objects.Attributes.Symlink);
            }
		}
		public string SymlinkTarget
		{
			get
			{
				if (Info != null)
					return Utilities.Symlink.GetTarget(Info.FullName);
				if (DirectoryInfo != null)
					return Utilities.Symlink.GetTarget(DirectoryInfo.FullName);
				return null;
			}
		}
		public bool Ignored { get; private set; }

        public Entry(Area area, Entry parent, DirectoryInfo info, string canonicalName, bool ignored)
		{
            Parent = parent;
            Area = area;
            CanonicalName = canonicalName;
            DirectoryInfo = info;
            ModificationTime = DirectoryInfo.LastWriteTimeUtc;
			Ignored = ignored;
			m_Hash = string.Empty;

			if (info.Attributes.HasFlag(FileAttributes.Hidden))
                Attributes = Attributes | Objects.Attributes.Hidden;
            if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
                Attributes = Attributes | Objects.Attributes.ReadOnly;
			if (Utilities.Symlink.Exists(info))
			{
				Attributes = Attributes | Objects.Attributes.Symlink;
				if (CanonicalName.EndsWith("/"))
					CanonicalName = canonicalName.Substring(0, canonicalName.Length - 1);
			}
		}

        internal bool DataEquals(string fingerprint, long size)
        {
            return Length == size && Hash == fingerprint;
        }

        public Entry(Area area, Entry parent, FileInfo info, string canonicalName, bool ignored)
        {
            Parent = parent;
            Area = area;
            CanonicalName = info.Name == ".vrmeta" ? info.Name : canonicalName;
            Info = info;
			Ignored = ignored;
            GetInfo();
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
                if (Info != null)
                    return Info.Name;
                if (DirectoryInfo != null)
                    return DirectoryInfo.Name;
                throw new Exception();
            }
        }

        private void GetInfo()
        {
            Length = Info.Length;
            ModificationTime = Info.LastWriteTimeUtc;

			if (Utilities.Symlink.Exists(Info, CanonicalName))
			{
				Attributes = Attributes | Objects.Attributes.Symlink;
				Length = 0;
			}
			if (Info.Attributes.HasFlag(FileAttributes.Hidden))
                Attributes = Attributes | Objects.Attributes.Hidden;
            if (Info.Attributes.HasFlag(FileAttributes.ReadOnly))
                Attributes = Attributes | Objects.Attributes.ReadOnly;
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

        public static List<Entry> GetEntryList(Area area, DirectoryInfo root, DirectoryInfo adminFolder)
        {
            List<Entry> entries = PopulateList(area, null, root, area.GetLocalPath(root.FullName), adminFolder, false);
            return entries;
        }

        private static List<Entry> PopulateList(Area area, Entry parentEntry, DirectoryInfo info, string subdirectory, DirectoryInfo adminFolder, bool ignoreDirectory)
        {
            List<Entry> result = new List<Entry>();
            if (area.InExtern(info))
                return result;
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
                        if (!extdir.EndsWith("/"))
                            extdir += "/";
                        if (string.Equals(slashedSubdirectory, extdir, StringComparison.OrdinalIgnoreCase))
                        {
                            return result;
                        }
                    }
                }

				parentEntry = new Entry(area, parentEntry, info, slashedSubdirectory, ignoreDirectory);
                result.Add(parentEntry);
                if (ignoreDirectory)
                    return result;
            }

			// Don't add children for symlinks.
			if (Utilities.Symlink.Exists(info))
				return result;

            List<Task<List<Entry>>> tasks = new List<Task<List<Entry>>>();
            string prefix = string.IsNullOrEmpty(subdirectory) ? string.Empty : slashedSubdirectory;
            foreach (var x in info.GetFileSystemInfos())
            {
                string fn = x.Name;
                string name = prefix + fn;
                if (x.Attributes.HasFlag(FileAttributes.Directory))
                {
                    if (fn == "." || fn == "..")
                        continue;
                    
                    if (name == adminFolder.Name)
                        continue;

#if DEBUG
                    if (true)
#else
                    if (Utilities.MultiArchPInvoke.IsRunningOnMono)
#endif
                    {
                        result.AddRange(PopulateList(area, parentEntry, x as DirectoryInfo, name, adminFolder, ignoreDirectory));
                    }
                    else
                    {
                        tasks.Add(Utilities.LimitedTaskDispatcher.Factory.StartNew(() => { return PopulateList(area, parentEntry, x as DirectoryInfo, name, adminFolder, ignoreDirectory); }));
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
                    result.Add(new Entry(area, parentEntry, x as FileInfo, name, ignored));
                }
            }
            if (tasks.Count > 0)
            {
                Task.WaitAll(tasks.ToArray());
                foreach (var x in tasks)
                    result.AddRange(x.Result);
            }
            return result;
        }
    }
}
