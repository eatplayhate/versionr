using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Models
{
	public class VersionModel
	{
		public Versionr.Objects.Version Version;

		public class Entry
		{
			public Versionr.Objects.AlterationType Type;
			public long Id;
			public string NewName;
			public string OldName;
		}
		public List<Entry> Entries = new List<Entry>();
		
		public VersionModel(Area area, Versionr.Objects.Version version)
		{
			// TODO optimize SQL
			Version = version;
			foreach (var alteration in area.GetAlterations(version))
			{
				var entry = new Entry();
				entry.Type = alteration.Type;
				entry.Id = alteration.Id;
				if (alteration.NewRecord.HasValue)
					entry.NewName = area.GetRecord(alteration.NewRecord.Value).CanonicalName;
				if (alteration.PriorRecord.HasValue)
					entry.OldName = area.GetRecord(alteration.PriorRecord.Value).CanonicalName;
				Entries.Add(entry);
			}
		}
	}
}
