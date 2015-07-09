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
        public Area Area { get; set; }
        public string CanonicalName { get; set; }
        public string Hash { get; set; }
        public long Length { get; set; }
        public DateTime ModificationTime { get; set; }
        public bool IsDirectory
        {
            get
            {
                return CanonicalName.EndsWith("/");
            }
        }
        public Entry(Area area, string canonicalName)
        {
            Area = area;
            CanonicalName = canonicalName;
            Hash = string.Empty;
            if (!IsDirectory)
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

        private void GetInfo()
        {
            FileInfo info = new FileInfo(Path.Combine(Area.Root.FullName, CanonicalName));
            Length = info.Length;
            Hash = CheckHash(info);
            ModificationTime = info.LastWriteTimeUtc;
        }

        internal static string CheckHash(FileInfo info)
        {
            var hasher = System.Security.Cryptography.SHA384.Create();
            byte[] hashBytes = hasher.ComputeHash(info.OpenRead());
            string hashValue = string.Empty;
            foreach (var h in hashBytes)
                hashValue += string.Format("{0:X2}", h);
            return hashValue;
        }
    }
    public class Snapshot
    {
        public List<Entry> Entries { get; set; }
        public Snapshot(Area root, List<string> fileEntries)
        {
            Entries = new List<Entry>();
            List<Task<Entry>> tasks = new List<Task<Entry>>();
            foreach (var x in fileEntries)
            {
                tasks.Add(Task.Run<Entry>(() => { return new Entry(root, x); }));
            }
            Task.WaitAll(tasks.ToArray());
            foreach (var x in tasks)
                Entries.Add(x.Result);
        }

        public static List<string> GetEntryList(DirectoryInfo root, DirectoryInfo adminFolder)
        {
            List<string> entries = new List<string>();
            PopulateList(entries, root, string.Empty, adminFolder);
            return entries;
        }

        private static void PopulateList(List<string> entryNames, DirectoryInfo info, string subdirectory, DirectoryInfo adminFolder)
        {
            foreach (var x in info.GetFiles())
            {
                if (x.Name == "." || x.Name == "..")
                    continue;
                entryNames.Add(subdirectory == string.Empty ? x.Name : subdirectory + "/" + x.Name);
            }
            foreach (var x in info.GetDirectories())
            {
                if (x.FullName == adminFolder.FullName)
                    continue;
                string name = subdirectory == string.Empty ? x.Name : subdirectory + "/" + x.Name;
                entryNames.Add(name + "/");
                PopulateList(entryNames, x, name, adminFolder);
            }
        }
    }
}
