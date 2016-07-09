using Nancy.ViewEngines.Razor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb.Models
{
	public static class Util
	{
		public static IHtmlString ShortGuid(Guid guid)
		{
			var s = guid.ToString().Substring(0, 8);
			return new NonEncodedHtmlString(s);
		}

		public static IHtmlString ApproxRelativeTime(DateTime dateTime)
		{
			var s = dateTime.ToRelativeDateString(true);
			return new NonEncodedHtmlString(s);
		}

		public static IHtmlString FormatSize(long size)
		{
			string s;
			if (size < 1024)
				s = string.Format("{0} bytes", size);
			else if (size < 1024 * 1024)
				s = string.Format("{0:N0} KB", size / 1024.0);
			else if (size < 1024 * 1024 * 1024)
				s = string.Format("{0:N0} MB", size / (1024.0 * 1024.0));
			else if (size < 1024L * 1024 * 1024 * 1024)
				s = string.Format("{0:N0} GB", size / (1024.0 * 1024.0 * 1024.0));
			else
				s = string.Format("{0:N2} TB", size / (1024.0 * 1024.0 * 1024.0 * 1024.0));
			return new NonEncodedHtmlString(s);
		}

		public static IHtmlString CreateRawLink(string branchOrVersion, string path)
		{
			var s = string.Format("/raw/{0}/{1}", branchOrVersion, path);
			return new NonEncodedHtmlString(s);
		}
	}
}
