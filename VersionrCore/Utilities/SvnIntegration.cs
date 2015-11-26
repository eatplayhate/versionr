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
		public static Regex[] SymlinkPatterns { get; internal set; }

        public static bool ApliesTo(FileSystemInfo info, string hintPath)
        {
            if (SymlinkPatterns == null || SymlinkPatterns.Length == 0)
                return false;

            // Only for windows
            if (MultiArchPInvoke.IsRunningOnMono)
                return false;

            if (info.Attributes.HasFlag(FileAttributes.Directory))
                return false;

            string path = string.IsNullOrEmpty(hintPath) ? info.FullName.Replace('\\', '/') : hintPath;
            foreach (var x in SymlinkPatterns)
            {
                var match = x.Match(path);
                if (match.Success)
                    return true;
            }

            return false;
        }

        public static bool ApliesTo(string path)
		{
			if (SymlinkPatterns == null || SymlinkPatterns.Length == 0)
				return false;

			// Only for windows
			if (MultiArchPInvoke.IsRunningOnMono)
				return false;

			if (Directory.Exists(path))
				return false;

			path = path.Replace('\\', '/');
			foreach (var x in SymlinkPatterns)
			{
				var match = x.Match(path);
				if (match.Success)
					return true;
			}

			return false;
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
