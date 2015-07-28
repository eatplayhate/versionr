using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
	public class SvnIntegration
	{
		public static bool ApliesTo(string path)
		{
			// Only for windows
			if (MultiArchPInvoke.IsRunningOnMono)
				return false;

			if (Directory.Exists(path))
				return false;

			return true;
		}
		public static bool IsSymlink(string path)
		{
			return GetSymlinkTarget(path) != null;
		}
		public static void DeleteSymlink(string path)
		{
			File.Delete(path);
		}
		public static bool CreateSymlink(string path, string target)
		{
			File.WriteAllText(path, "link " + target);
			return true;
		}
		public static string GetSymlinkTarget(string path)
		{
			try
			{
				string line = File.ReadLines(path).FirstOrDefault();
				if (line == null)
					return null;
				var regex = new Regex("^link (.+)$");
				var match = regex.Match(line);
				if (match.Success)
					return match.Groups[1].Value;
				return null;
			}
			catch
			{
				return null;
			}
        }
	}
}
