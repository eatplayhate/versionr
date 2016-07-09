﻿using Nancy.ViewEngines.Razor;
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

		private static readonly Dictionary<string, string> EmailMapping = new Dictionary<string, string>()
		{
			{ "Lewis", "lstrudwick@ea.com" },
			{ "Andrew", "acrawford@ea.com" },
			{ "Alex", "alex.holkner@gmail.com" },
		};

		private static readonly string DefaultEmailDomain = "ea.com";

		public static IHtmlString GravatarLink(string email)
		{
			// HAX: Map Versionr user names to email:
			if (!email.Contains("@"))
			{
				string name = email;
				if (!EmailMapping.TryGetValue(name, out email))
					email = string.Format("{0}@{1}", name, DefaultEmailDomain);
			}

			email = email.Trim().ToLower();
			var emailBytes = Encoding.UTF8.GetBytes(email);
			var md5Bytes = System.Security.Cryptography.MD5.Create().ComputeHash(emailBytes);
			var sb = new StringBuilder();
			foreach (byte b in md5Bytes)
				sb.Append(b.ToString("x2"));
			var s = string.Format("//www.gravatar.com/avatar/{0}?d=identicon", sb.ToString());
			return new NonEncodedHtmlString(s);
		}
	}
}
