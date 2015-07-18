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
        [Option('n', "nolist", HelpText = "Does not display a listing of file statuses.")]
        public bool NoList { get; set; }
        [Option('s', "summary", HelpText = "Displays a summary at the end of the status block.")]
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

        public override string Verb
        {
            get
            {
                return "status";
            }
        }
    }
    class Status : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            StatusVerbOptions localOptions = (StatusVerbOptions)options;
            var ss = Workspace.Status;
            Printer.WriteLineMessage("Version #b#{0}## on branch \"#b#{1}##\"\n", ss.CurrentVersion.ID, ss.Branch.Name);
            int[] codeCount = new int[(int)StatusCode.Count];
            foreach (var x in ss.Elements)
            {
				if (x.Code == StatusCode.Unchanged)
                    continue;
                codeCount[(int)x.Code]++;
                if (!localOptions.NoList)
                {
                    string name = x.FilesystemEntry != null ? x.FilesystemEntry.CanonicalName : x.VersionControlRecord.CanonicalName;
                    int index = name.LastIndexOf('/');
                    if (index != name.Length - 1)
                        name = name.Insert(index + 1, "#b#");
                    Printer.WriteLineMessage("{1}## {0}", name, GetStatus(x));
                    if (x.Code == StatusCode.Renamed || x.Code == StatusCode.Copied)
                        Printer.WriteLineMessage("                  #q#<== {0}", x.VersionControlRecord.CanonicalName);
                }
            }
            if (localOptions.Summary)
            {
                Printer.WriteLineMessage("Summary:");
                for (int i = 0; i < codeCount.Length; i++)
                    Printer.WriteLineMessage("  {0} {2} {1}", codeCount[i], codeCount[i] != 1 ? "Objects" : "Object", ((StatusCode)i).ToString());
            }
            return true;
        }

        private string GetStatus(Versionr.Status.StatusEntry x)
        {
            var info = GetStatusText(x);
            string text = info.Item2;
            while (text.Length < 14)
                text = " " + text;
            text = "#" + info.Item1 + "#" + text;
            if (x.Staged)
                text += "#b#*##";
            else
                text += " ";
            return text;
        }

        private Tuple<char, string> GetStatusText(Versionr.Status.StatusEntry x)
        {
            switch (x.Code)
            {
                case StatusCode.Added:
                    return x.Staged ? new Tuple<char, string>('s', "(added)")
                        : new Tuple<char, string>('e', "(error)");
                case StatusCode.Conflict:
                    return x.Staged ? new Tuple<char, string>('e', "(conflict)")
                        : new Tuple<char, string>('e', "(conflict)");
                case StatusCode.Copied:
                    return x.Staged ? new Tuple<char, string>('s', "(added - copied)")
                        : new Tuple<char, string>('w', "(copied)");
                case StatusCode.Deleted:
                    return x.Staged ? new Tuple<char, string>('b', "(deleted)")
                        : new Tuple<char, string>('w', "(missing)");
                case StatusCode.Missing:
                    return x.Staged ? new Tuple<char, string>('e', "(error)")
                        : new Tuple<char, string>('w', "(missing)");
                case StatusCode.Modified:
                    return x.Staged ? new Tuple<char, string>('s', "(modified)")
                        : new Tuple<char, string>('w', "(changed)");
                case StatusCode.Renamed:
                    return x.Staged ? new Tuple<char, string>('s', "(renamed)")
                        : new Tuple<char, string>('w', "(renamed)");
                case StatusCode.Unversioned:
                    return x.Staged ? new Tuple<char, string>('e', "(error)")
                        : new Tuple<char, string>('w', "(unversioned)");
                case StatusCode.Ignored:
                    return x.Staged ? new Tuple<char, string>('e', "(error)")
                        : new Tuple<char, string>('q', "(ignored)");
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
