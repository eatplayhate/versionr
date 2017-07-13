using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Models
{
	public class DirectoryBrowseModel
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

		public DirectoryBrowseModel(Area area, Versionr.Objects.Version version, string branchOrVersion, string path, List<Versionr.Objects.Record> records)
		{
			BasePath = string.Format("/src/{0}/{1}", branchOrVersion, path);
			if (!BasePath.EndsWith("/"))
				BasePath += "/";
			
			Entries.AddRange(from record in records
							 let recordVersion = area.GetVersion(record)
							 select new Entry(record.Name, recordVersion.ID, recordVersion.Author, recordVersion.Message, recordVersion.Timestamp));
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
