using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.LocalState
{
    public class RemoteConfig
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public long Id { get; set; }
        [SQLite.Indexed]
        public string Name { get; set; }
        public string Host { get; set; }
        public string Module { get; set; }
        public int Port { get; set; }
        public DateTime LastPull { get; set; }

		[SQLite.Ignore]
		public string URL
		{
			get
			{
				// Legacy: prefer Host:Port/Module (a versionr URL)
				if (!string.IsNullOrEmpty(Host))
					return string.Format("vsr://{0}:{1}/{2}", Host, Port, Module);
				
				// Preferred: store URL in Module
				return Module;
			}
		}
    }
}
