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
        }
        public long Length { get; set; }
        public DateTime ModificationTime { get; set; }
        public Objects.Attributes Attributes { get; set; }
        public bool IsDirectory
        {
            get
            {
                return CanonicalName.EndsWith("/");
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
        }

        internal bool DataEquals(string fingerprint, long size)
        {
            return Length == size && Hash == fingerprint;
        }

        public Entry(Area area, Entry parent, FileInfo info, string canonicalName, bool ignored)
        {
            Parent = parent;
            Area = area;
            CanonicalName = canonicalName;
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

            if (Info.Attributes.HasFlag(FileAttributes.Hidden))
                Attributes = Attributes | Objects.Attributes.Hidden;
            if (Info.Attributes.HasFlag(FileAttributes.ReadOnly))
                Attributes = Attributes | Objects.Attributes.ReadOnly;
        }

        internal static string CheckHash(FileInfo info)
        {
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
    public class FileStatus
    {
        public List<Entry> Entries { get; set; }
        public FileStatus(Area root, DirectoryInfo rootFolder)
        {
            Entries = GetEntryList(root, rootFolder, root.AdministrationFolder);
            int files = Entries.Where(x => !x.IsDirectory).Count();
            Printer.PrintDiagnostics("Current working directory has {0} file{1} in {2} director{3}.", files, files == 1 ? "" : "s", Entries.Count - files, (Entries.Count - files) == 1 ? "y" : "ies");
        }

        public static List<Entry> GetEntryList(Area area, DirectoryInfo root, DirectoryInfo adminFolder)
        {
            List<Entry> entries = PopulateList(area, null, root, string.Empty, adminFolder, false);
            return entries;
        }

        private static List<Entry> PopulateList(Area area, Entry parentEntry, DirectoryInfo info, string subdirectory, DirectoryInfo adminFolder, bool ignoreDirectory)
        {
            List<Entry> result = new List<Entry>();
            if (subdirectory != string.Empty)
            {
                if (area?.Directives?.Include?.RegexPatterns != null)
                {
                    ignoreDirectory = true;
                    foreach (var y in area.Directives.Include.RegexPatterns)
                    {
                        var match = y.Match(subdirectory + "/");
                        if (match.Success)
                        {
                            ignoreDirectory = false;
                            break;
                        }
                    }
                }
                if (area?.Directives?.Ignore?.RegexPatterns != null)
				{
					foreach (var y in area.Directives.Ignore.RegexPatterns)
					{
                        var match = y.Match(subdirectory + "/");
                        if (match.Success)
						{
							ignoreDirectory = true;
							break;
						}
					}
				}

				parentEntry = new Entry(area, parentEntry, info, subdirectory + "/", ignoreDirectory);
                result.Add(parentEntry);
            }
            foreach (var x in info.GetFiles())
            {
                if (x.Name == "." || x.Name == "..")
                    continue;
                string name = subdirectory == string.Empty ? x.Name : subdirectory + "/" + x.Name;
				bool ignored = ignoreDirectory;
                if (!ignored && area?.Directives?.Include?.RegexPatterns != null)
                {
                    ignored = true;
                    foreach (var y in area.Directives.Include.RegexPatterns)
                    {
                        var match = y.Match(name);
                        if (match.Success)
                        {
                            ignored = false;
                            break;
                        }
                    }
                }
                if (!ignored && area?.Directives?.Include?.Extensions != null)
                {
                    ignored = true;
                    foreach (var y in area.Directives.Include.Extensions)
                    {
                        if (x.Extension == y)
                        {
                            ignored = false;
                            break;
                        }
                    }
                }
                if (!ignored && area?.Directives?.Ignore?.Extensions != null)
                {
                    foreach (var y in area.Directives.Ignore.Extensions)
                    {
                        if (x.Extension == y)
                        {
							ignored = true;
                            break;
                        }
                    }
                }
                if (!ignored && area?.Directives?.Ignore?.RegexPatterns != null)
                {
                    foreach (var y in area.Directives.Ignore.RegexPatterns)
                    {
                        var match = y.Match(name);
                        if (match.Success)
                        {
							ignored = true;
                            break;
                        }
                    }
                }
                result.Add(new Entry(area, parentEntry, x, name, ignored));
            }
            List<Task<List<Entry>>> tasks = new List<Task<List<Entry>>>();
            foreach (var x in info.GetDirectories())
            {
                if (x.FullName == adminFolder.FullName)
                    continue;
                string name = subdirectory == string.Empty ? x.Name : subdirectory + "/" + x.Name;

                if (Utilities.MultiArchPInvoke.IsRunningOnMono)
                {
                    result.AddRange(PopulateList(area, parentEntry, x, name, adminFolder, ignoreDirectory));
                }
                else
                {
                    tasks.Add(Task.Run(() => { return PopulateList(area, parentEntry, x, name, adminFolder, ignoreDirectory); }));
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
