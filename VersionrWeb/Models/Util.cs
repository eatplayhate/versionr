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

	}
}
