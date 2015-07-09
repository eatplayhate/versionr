using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class StatusVerbOptions : VerbOptionBase
    {
        [Option("nolist", DefaultValue = false, HelpText = "Does not display the full list of files.")]
        public bool NoList { get; set; }
        [Option('s', "summary", DefaultValue = false, HelpText = "Displays a summary at the end of the status block.")]
        public bool Summary { get; set; }
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "This command will determine the current status of the Versionr",
                    "repository tree.", 
                    "",
                    "It will determine if any files have been added, changed, or removed",
                    "when compared with the currently checked-out version on record.",
                };
            }
        }

        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [options]", Verb);
            }
        }

        public override string Verb
        {
            get
            {
                return "status";
            }
        }
    }
    class Status : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            StatusVerbOptions localOptions = options as StatusVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;

            var ss = ws.Status;
            System.Console.WriteLine("Version {0} on branch \"{1}\"\n", ss.CurrentVersion.ID, ss.Branch.Name);
            int[] codeCount = new int[(int)StatusCode.Count];
            foreach (var x in ss.Elements)
            {
				if (x.Code == StatusCode.Unchanged)
                    continue;
                codeCount[(int)x.Code]++;
                if (!localOptions.NoList)
                {
                    string name = x.FilesystemEntry != null ? x.FilesystemEntry.CanonicalName : x.VersionControlRecord.CanonicalName;
                    System.Console.WriteLine(" {1} {0}", name, GetStatus(x));
                    if (x.Code == StatusCode.Renamed || x.Code == StatusCode.Copied)
                        System.Console.WriteLine("  <== {0}", x.VersionControlRecord.CanonicalName);
                }
            }
            if (localOptions.Summary)
            {
                System.Console.WriteLine("Summary:");
                for (int i = 0; i < codeCount.Length; i++)
                    System.Console.WriteLine("  {0} {2} {1}", codeCount[i], codeCount[i] != 1 ? "Objects" : "Object", ((StatusCode)i).ToString());
            }
            return true;
        }

        private string GetStatus(Versionr.Status.StatusEntry x)
        {
            switch (x.Code)
            {
                case StatusCode.Added:
                    return x.Staged ? "(added)" : "(error)";
                case StatusCode.Conflict:
                    return x.Staged ? "(conflict)" : "(conflict)";
                case StatusCode.Copied:
                    return x.Staged ? "(added - copied)" : "(copied)";
                case StatusCode.Deleted:
                    return x.Staged ? "(deleted)" : "(missing)";
                case StatusCode.Missing:
                    return x.Staged ? "(error)" : "(missing)";
                case StatusCode.Modified:
                    return x.Staged ? "(modified)" : "(changed)";
                case StatusCode.Renamed:
                    return x.Staged ? "(renamed)" : "(renamed)";
                case StatusCode.Unversioned:
                    return x.Staged ? "(error)" : "(unversioned)";
				case StatusCode.Ignored:
					return x.Staged ? "(error)" : "(ignored)";
                default:
                    throw new Exception();
            }
        }

        private static string ShortStatusCode(StatusCode code)
        {
            switch (code)
            {
                case StatusCode.Missing:
                    return "%";
                case StatusCode.Deleted:
                    return "D";
                case StatusCode.Modified:
                    return "M";
                case StatusCode.Added:
                    return "+";
                case StatusCode.Unchanged:
                    return "-";
                case StatusCode.Unversioned:
                    return "?";
                case StatusCode.Renamed:
                    return "R";
                case StatusCode.Copied:
                    return "C";
                case StatusCode.Conflict:
                    return "!";
				case StatusCode.Ignored:
					return string.Empty;
            }
            throw new Exception();
        }
    }
}
