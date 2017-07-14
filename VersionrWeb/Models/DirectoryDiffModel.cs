using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Models
{
    public class DirectoryDiffModel
    {
        public string BasePath;

        public class Entry
        {
            public Entry(string name, Guid version, string author, string message, DateTime timestamp)
            {
                IsDirectory = name.EndsWith("/");
                Name = name;
                if (IsDirectory)
                    Name = name.Substring(0, Name.Length - 1);

                Version = version;
                Author = author;
                Message = message;
                Timestamp = timestamp;
            }

            public string Name;
            public bool IsDirectory;
            public Guid Version;
            public string Author;
            public string Message;
            public DateTime Timestamp;
        }
        public List<Entry> Entries = new List<Entry>();

        public DirectoryDiffModel(Area area, Versionr.Objects.Version version, string path, List<KeyValuePair<Versionr.Objects.AlterationType, Versionr.Objects.Record>> records)
        {
            BasePath = string.Format("/diff/{0}/{1}", version.ID, path);
            if (!BasePath.EndsWith("/"))
                BasePath += "/";

            Entries.AddRange(records.Select(x => new Entry(x.Value.Name, version.ID, version.Author, version.Message, version.Timestamp)));
            Entries.Sort((a, b) =>
            {
                var x = a.IsDirectory.CompareTo(b.IsDirectory);
                if (x != 0)
                    return -x;
                return string.Compare(a.Name, b.Name, true);
            });
        }
    }
}
