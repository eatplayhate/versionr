using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
	public class DiffTool
	{
		public static string GetTempFilename()
		{
			return System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetTempFileName());
		}

		public static void Diff(string baseFile, string file)
		{
			Diff(baseFile, baseFile, file, file);
		}

		public static void Diff(string baseFile, string baseAlias, string file, string fileAlias)
		{
			System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
			{
				FileName = "C:\\Program Files\\TortoiseSVN\\bin\\TortoiseMerge.exe",
				Arguments = string.Format("/base:\"{0}\" /mine:\"{1}\"", baseFile, file)
			};
			var proc = System.Diagnostics.Process.Start(psi);
			proc.WaitForExit();
		}

		public static bool Merge(string baseFile, string file, string output)
		{
			return Merge(baseFile, baseFile, file, file, output);
		}
        public static bool Merge(string baseFile, string baseAlias, string file, string fileAlias, string output)
		{
			System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
			{
				FileName = "C:\\Program Files\\KDiff3\\kdiff3.exe",
				Arguments = string.Format("\"{0}\" \"{1}\" -o \"{2}\" --auto --L1 \"{3}\" --L2 \"{4}\"", baseFile, file, output, baseAlias, fileAlias)
			};
			var proc = System.Diagnostics.Process.Start(psi);
			proc.WaitForExit();
			return (proc.ExitCode == 0);
        }
		public static bool Merge3Way(string baseFile, string file1, string file2, string output)
		{
			return Merge3Way(baseFile, baseFile, file1, file1, file2, file2, output);
		}

		public static bool Merge3Way(string baseFile, string baseAlias, string file1, string file1Alias, string file2, string file2Alias, string output)
		{
			System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
			{
				FileName = "C:\\Program Files\\KDiff3\\kdiff3.exe",
				Arguments = string.Format("\"{0}\" \"{1}\" \"{2}\" -o \"{3}\" --auto --L1 \"{4}\" --L2 \"{5}\"", baseFile, file1, file2, output, baseAlias, file1Alias, file2Alias)
			};
			var proc = System.Diagnostics.Process.Start(psi);
			proc.WaitForExit();
			return (proc.ExitCode == 0);
		}
	}
}
