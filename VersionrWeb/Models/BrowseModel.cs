using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Models
{
	public class BrowseModel
	{
		private Area m_Area;

		public bool HasParentPath = true;
		public bool IsDirectory;
		public string[] Breadcrumbs;

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

		public BrowseModel(string path, string branchOrVersion)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				path = "";
				IsDirectory = true;
				HasParentPath = false;
			}

			Breadcrumbs = path.Split('/');

			m_Area = Area.Load(new DirectoryInfo(Environment.CurrentDirectory), true);
			if (branchOrVersion == null)
				branchOrVersion = "master";

			Guid versionId;
			if (!Guid.TryParse(branchOrVersion, out versionId))
			{
				var branch = m_Area.GetBranchByName(branchOrVersion).FirstOrDefault();
				versionId = m_Area.GetBranchHead(branch).Version;
			}

			var version = m_Area.GetVersion(versionId);
			var records = m_Area.GetRecords(version); // TODO filter path at DB
			var directoryRecords = new List<Versionr.Objects.Record>();
			foreach (var record in records)
			{
				string recordPath = record.CanonicalName;
				if (recordPath.EndsWith("/"))
				{
					recordPath = recordPath.Substring(0, recordPath.Length - 1);
				}

				if (recordPath == path)
				{
					if (record.IsDirectory)
						IsDirectory = true;
					else
						break;
				}
				else if (Path.GetDirectoryName(recordPath).Replace('\\', '/') == path)
				{
					directoryRecords.Add(record);
				}
			}

			Entries.AddRange(from record in directoryRecords
							 let recordVersion = m_Area.GetVersion(record)
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
