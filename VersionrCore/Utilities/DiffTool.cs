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
			return System.IO.Path.GetTempFileName();
		}

		public static void Diff(string baseFile, string file, string externalTool)
		{
			Diff(baseFile, baseFile, file, file, externalTool, false);
		}

		public static System.Diagnostics.Process Diff(string baseFile, string baseAlias, string file, string fileAlias, string externalTool, bool nonblocking)
		{
            System.Diagnostics.ProcessStartInfo psi;
            if (!string.IsNullOrEmpty(externalTool))
            {
                string xtool = externalTool.Trim();
                int filenameIndex = xtool.Length;
                bool quoted = false;
                int baseIndex = 0;
                for (int i = 0; i < xtool.Length; i++)
                {
                    if (xtool[i] == '"')
                    {
                        if (quoted)
                        {
                            filenameIndex = i + 1;
                            break;
                        }
                        else
                        {
                            baseIndex = 1;
                            quoted = true;
                        }
                    }
                    else if (xtool[i] == ' ' && !quoted)
                    {
                        filenameIndex = i + 1;
                        break;
                    }
                }
                string filename = xtool.Substring(baseIndex, filenameIndex - baseIndex - 1);
                xtool = xtool.Substring(filenameIndex);
                xtool = xtool.Replace("%basename", "\"{2}\"");
                xtool = xtool.Replace("%changedname", "\"{3}\"");
                xtool = xtool.Replace("%base", "\"{0}\"");
                xtool = xtool.Replace("%changed", "\"{1}\"");
                try
                {
                    psi = new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = filename,
                        Arguments = string.Format(xtool, baseFile, file, baseAlias, fileAlias),
                        UseShellExecute = true
                    };
                    var proc = System.Diagnostics.Process.Start(psi);
                    if (nonblocking)
                        return proc;
                    proc.WaitForExit();
                    return null;
                }
                catch
                {
                    Printer.PrintMessage("Error during diff, external diff tool should be specified like this: ");
                    Printer.PrintMessage("Parameters:");
                    Printer.PrintMessage("  #b#%base## - Base filename.");
                    Printer.PrintMessage("  #b#%changed## - Changed filename.");
                    Printer.PrintMessage("  #b#%basename## - Alias for base file.");
                    Printer.PrintMessage("  #b#%changedname## - Alias for changed file.");
                    Printer.PrintMessage("Example:");
                    Printer.PrintMessage("  difftool --unified #b#%base## #b#%changed## --L1 #q#%basename## --L2 #q#%changedname##");
                }
                return null;
            }
            else
            {
                if (MultiArchPInvoke.RunningPlatform != Platform.Windows)
                {
                    psi = new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = "diff",
                        Arguments = string.Format("--unified \"{0}\" \"{1}\"", baseFile, file),
                        UseShellExecute = true
                    };
                }
                else
                {
                    psi = new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = "C:\\Program Files\\TortoiseSVN\\bin\\TortoiseMerge.exe",
                        Arguments = string.Format("/base:\"{0}\" /mine:\"{1}\"", baseFile, file),
                        UseShellExecute = true
                    };
                    if (!System.IO.File.Exists(psi.FileName))
                    {
                        psi = new System.Diagnostics.ProcessStartInfo()
                        {
                            FileName = "C:\\Program Files\\KDiff3\\kdiff3.exe",
                            Arguments = string.Format("{0} {1} --L1 {2} --L2 {3}", baseFile, file, baseAlias, fileAlias),
                            UseShellExecute = true
                        };
                    }
                }
                try
                {
                    var proc = System.Diagnostics.Process.Start(psi);
                    if (nonblocking)
                        return proc;
                    proc.WaitForExit();
                    return null;
                }
                catch
                {
                    if (MultiArchPInvoke.RunningPlatform != Platform.Windows)
                        Printer.PrintMessage("Couldn't run external diff. Make sure you have #b#diff## available or specify an #b#ExternalDiff## program in your directives file.");
                    else
                        Printer.PrintMessage("Couldn't run external diff. Make sure you have #b#kdiff3## or #b#TortoiseMerge## available or specify an #b#ExternalDiff## program in your directives file.");
                    throw;
                }
            }
        }

		public static bool Merge(string baseFile, string file, string output, string externalTool)
		{
			return Merge(baseFile, baseFile, file, file, output, externalTool);
		}
        public static bool Merge(string baseFile, string baseAlias, string file, string fileAlias, string output, string externalTool)
        {
            System.Diagnostics.ProcessStartInfo psi;
            if (!string.IsNullOrEmpty(externalTool))
            {
                string xtool = externalTool.Trim();
                int filenameIndex = xtool.Length;
                bool quoted = false;
                int baseIndex = 0;
                for (int i = 0; i < xtool.Length; i++)
                {
                    if (xtool[i] == '"')
                    {
                        if (quoted)
                        {
                            filenameIndex = i + 1;
                            break;
                        }
                        else
                        {
                            baseIndex = 1;
                            quoted = true;
                        }
                    }
                    else if (xtool[i] == ' ' && !quoted)
                    {
                        filenameIndex = i + 1;
                        break;
                    }
                }
                string filename = xtool.Substring(baseIndex, filenameIndex - baseIndex - 1);
                xtool = xtool.Substring(filenameIndex);
                xtool = xtool.Replace("%file1name", "\"{2}\"");
                xtool = xtool.Replace("%file2name", "\"{3}\"");
                xtool = xtool.Replace("%file1", "\"{0}\"");
                xtool = xtool.Replace("%file2", "\"{1}\"");
                xtool = xtool.Replace("%output", "\"{4}\"");
                try
                {
                    psi = new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = filename,
                        Arguments = string.Format(xtool, baseFile, file, baseAlias, fileAlias, output),
                        UseShellExecute = true
                    };
                    var proc = System.Diagnostics.Process.Start(psi);
                    proc.WaitForExit();
                    return proc.ExitCode == 0;
                }
                catch
                {
                    Printer.PrintMessage("Error during merge, external 2-way merge tool should be specified like this: ");
                    Printer.PrintMessage("Parameters:");
                    Printer.PrintMessage("  #b#%file1## - First changed filename.");
                    Printer.PrintMessage("  #b#%file2## - Second changed filename.");
                    Printer.PrintMessage("  #b#%file1name## - Alias for first file.");
                    Printer.PrintMessage("  #b#%file2name## - Alias for second file.");
                    Printer.PrintMessage("  #b#%output## - Output filename.");
                    Printer.PrintMessage("Example:");
                    Printer.PrintMessage("  merge #b#%file1## #b#%file2##");
                    return false;
                }
            }
            else
            {
                System.Diagnostics.Process proc = null;
                List<KeyValuePair<string, string>> mergeCommands = new List<KeyValuePair<string, string>>();
                if (MultiArchPInvoke.RunningPlatform != Platform.Windows)
                {
                    psi = null;
                }
                else
                {
                    KeyValuePair<string, string> kdiff =
                        new KeyValuePair<string, string>(
                                "C:\\Program Files\\KDiff3\\kdiff3.exe",
                                string.Format("\"{0}\" \"{1}\" -o \"{2}\" --auto --L1 \"{3}\" --L2 \"{4}\"", baseFile, file, output, baseAlias, fileAlias)
                            );

                    mergeCommands.Add(kdiff);
                }
                bool success = false;
                for (int i = 0; i < mergeCommands.Count && !success; i++)
                {
                    try
                    {
                        psi = new System.Diagnostics.ProcessStartInfo()
                        {
                            FileName = System.IO.Path.GetFileName(mergeCommands[i].Key),
                            Arguments = mergeCommands[i].Value,
                            UseShellExecute = true
                        };
                        proc = System.Diagnostics.Process.Start(psi);
                        proc.WaitForExit();
                        return proc.ExitCode == 0;
                    }
                    catch
                    {
                        try
                        {
                            psi = new System.Diagnostics.ProcessStartInfo()
                            {
                                FileName = mergeCommands[i].Key,
                                Arguments = mergeCommands[i].Value,
                                UseShellExecute = true
                            };
                            proc = System.Diagnostics.Process.Start(psi);
                            proc.WaitForExit();
                            return proc.ExitCode == 0;
                        }
                        catch
                        {

                        }
                    }
                }
                if (MultiArchPInvoke.RunningPlatform != Platform.Windows)
                    Printer.PrintMessage("Couldn't run external 2-way merge. Specify an #b#ExternalMerge2Way## program in your directives file.");
                else
                    Printer.PrintMessage("Couldn't run external 2-way merge. Make sure you have #b#kdiff3## available or specify an #b#ExternalMerge2Way## program in your directives file.");
                throw new Exception();
            }
        }
		public static bool Merge3Way(string baseFile, string file1, string file2, string output, string externalTool)
		{
			return Merge3Way(baseFile, baseFile, file1, file1, file2, file2, output, externalTool);
		}

		public static bool Merge3Way(string baseFile, string baseAlias, string file1, string file1Alias, string file2, string file2Alias, string output, string externalTool)
        {
            System.Diagnostics.ProcessStartInfo psi;
            if (!string.IsNullOrEmpty(externalTool))
            {
                string xtool = externalTool.Trim();
                int filenameIndex = xtool.Length;
                bool quoted = false;
                int baseIndex = 0;
                for (int i = 0; i < xtool.Length; i++)
                {
                    if (xtool[i] == '"')
                    {
                        if (quoted)
                        {
                            filenameIndex = i + 1;
                            break;
                        }
                        else
                        {
                            baseIndex = 1;
                            quoted = true;
                        }
                    }
                    else if (xtool[i] == ' ' && !quoted)
                    {
                        filenameIndex = i + 1;
                        break;
                    }
                }
                string filename = xtool.Substring(baseIndex, filenameIndex - baseIndex - 1);
                xtool = xtool.Substring(filenameIndex);
                xtool = xtool.Replace("%basename", "\"{3}\"");
                xtool = xtool.Replace("%file1name", "\"{4}\"");
                xtool = xtool.Replace("%file2name", "\"{5}\"");
                xtool = xtool.Replace("%base", "\"{0}\"");
                xtool = xtool.Replace("%file1", "\"{1}\"");
                xtool = xtool.Replace("%file2", "\"{2}\"");
                xtool = xtool.Replace("%output", "\"{6}\"");
                try
                {
                    psi = new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = filename,
                        Arguments = string.Format(xtool, baseFile, file1, file2, baseAlias, file1Alias, file2Alias, output),
                        UseShellExecute = true
                    };
                    var proc = System.Diagnostics.Process.Start(psi);
                    proc.WaitForExit();
                    return proc.ExitCode == 0;
                }
                catch
                {
                    Printer.PrintMessage("Error during merge, external merge tool should be specified like this: ");
                    Printer.PrintMessage("Parameters:");
                    Printer.PrintMessage("  #b#%base## - Base filename.");
                    Printer.PrintMessage("  #b#%file1## - First changed filename.");
                    Printer.PrintMessage("  #b#%file2## - Second changed filename.");
                    Printer.PrintMessage("  #b#%basename## - Alias for base file.");
                    Printer.PrintMessage("  #b#%file1name## - Alias for first file.");
                    Printer.PrintMessage("  #b#%file2name## - Alias for second file.");
                    Printer.PrintMessage("  #b#%output## - Output filename.");
                    Printer.PrintMessage("Example:");
                    Printer.PrintMessage("  merge #b#%file1## #b#%base## #b#%file2##");
                    return false;
                }
            }
            else
            {
                System.Diagnostics.Process proc = null;
                List<KeyValuePair<string, string>> mergeCommands = new List<KeyValuePair<string, string>>();
                KeyValuePair<string, string> kdiff =
                    new KeyValuePair<string, string>(
                            "C:\\Program Files\\KDiff3\\kdiff3.exe",
                            string.Format("\"{0}\" \"{1}\" \"{2}\" -o \"{3}\" --auto --L1 \"{4}\" --L2 \"{5}\"", baseFile, file1, file2, output, baseAlias, file1Alias, file2Alias)
                        );
                KeyValuePair<string, string> merge =
                    new KeyValuePair<string, string>(
                            "merge",
                            string.Format("\"{1}\" \"{0}\" \"{2}\"", baseFile, file1, file2)
                        );

                mergeCommands.Add(kdiff);
                mergeCommands.Add(merge);
                bool success = false;
                for (int i = 0; i < mergeCommands.Count && !success; i++)
                {
                    try
                    {
                        psi = new System.Diagnostics.ProcessStartInfo()
                        {
                            FileName = System.IO.Path.GetFileName(mergeCommands[i].Key),
                            Arguments = mergeCommands[i].Value,
                            UseShellExecute = true
                        };
                        proc = System.Diagnostics.Process.Start(psi);
                        proc.WaitForExit();
                        return proc.ExitCode == 0;
                    }
                    catch
                    {
                        try
                        {
                            psi = new System.Diagnostics.ProcessStartInfo()
                            {
                                FileName = mergeCommands[i].Key,
                                Arguments = mergeCommands[i].Value,
                                UseShellExecute = true
                            };
                            proc = System.Diagnostics.Process.Start(psi);
                            proc.WaitForExit();
                            return proc.ExitCode == 0;
                        }
                        catch
                        {

                        }
                    }
                }
                if (MultiArchPInvoke.RunningPlatform != Platform.Windows)
                    Printer.PrintMessage("Couldn't run external 3-way merge. Make sure you have #b#merge## available or specify an #b#ExternalMerge## program in your directives file.");
                else
                    Printer.PrintMessage("Couldn't run external 3-way merge. Make sure you have #b#kdiff3## available or specify an #b#ExternalMerge## program in your directives file.");
                throw new Exception();
            }
		}
	}
}
