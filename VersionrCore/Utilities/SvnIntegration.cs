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

        public static bool AppliesTo(FileSystemInfo info, string hintPath)
        {
            if (SymlinkPatterns == null || SymlinkPatterns.Length == 0)
                return false;

            // Only for windows
            if (MultiArchPInvoke.IsRunningOnMono)
                return false;

            if ((info.Attributes & FileAttributes.Directory) != 0)
                return false;

            string path = string.IsNullOrEmpty(hintPath) ? info.FullName.Replace('\\', '/') : hintPath;
            foreach (var x in SymlinkPatterns)
            {
                if (x.IsMatch(path))
                    return true;
            }

            return false;
        }
        public static bool AppliesToFile(string fullpath, string hintPath)
        {
            if (SymlinkPatterns == null || SymlinkPatterns.Length == 0)
                return false;

            // Only for windows
            if (MultiArchPInvoke.IsRunningOnMono)
                return false;

            string path = string.IsNullOrEmpty(hintPath) ? fullpath.Replace('\\', '/') : hintPath;
            foreach (var x in SymlinkPatterns)
            {
                if (x.IsMatch(path))
                    return true;
            }

            return false;
        }

        public static bool AppliesTo(string path)
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
                if (x.IsMatch(path))
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
                using (StreamReader sr = File.OpenText(path))
                {
                    string line = sr.ReadLine();
                    if (line == null)
                        return null;
                    if (line.StartsWith("link ", StringComparison.Ordinal))
                    {
                        return line.Substring(5);
                    }
                    return null;
                }
            }
			catch
			{
				return null;
			}
        }
	}
}
