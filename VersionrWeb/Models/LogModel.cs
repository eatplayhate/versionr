using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;

namespace VersionrWeb.Models
{
	public class LogModel
	{
		public List<Versionr.Objects.Version> Versions;
		public int PageNumber;
		public int PageCount;

		public LogModel(Area area, List<Versionr.Objects.Version> versions, int pageNumber, int pageCount)
		{
			Versions = versions;
			PageNumber = pageNumber;
			PageCount = pageCount;
		}
	}
}
