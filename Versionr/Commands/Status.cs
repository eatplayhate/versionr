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
                    "#q#This command will determine the current status of the Versionr repository tree.", 
                    "",
                    "The operation will involve walking the entire directory tree of the vault and comparing each file against the #b#currently checked out version#q#.",
                    "",
                    "Objects that have operations #b#recorded#q# for the next ##commit#q# are marked with an asterisk (#b#*#q#).",
                    "",
                    "The following status codes are available:",
                    "  #s#Added##: Not part of the vault, but marked for inclusion.",
                    "  #s#Modified##: Object has changed, and the changes are marked for inclusion.",
                    "  #b#Deleted##: Was part of the vault, deleted from the disk and marked for removal.",
                    "  #w#Unversioned##: Object is not part of vault.",
                    "  #w#Missing##: Object is part of the vault, but is not present on the disk.",
                    "  #w#Changed##: Object has changed, but the changes are not marked for inclusion.",
                    "  #w#Renamed##: Object has been matched to a deleted object in the vault.",
                    "  #w#Copied##: Object is not part of the vault but is a copy of an object that is.",
                    "  #e#Conflict##: The file ##requires intervention#q# to finish merging. It will obstruct the next ##commit#q# until it is resolved.",
                    "",
                    "To record additional objects for inclusion in the next #b#commit#q#, see the #b#record#q# command.",
                    "",
                    "To reduce computation time, files are #b#not#q# checked for modifications unless their ##timestamp has been changed#q# from the current version#q#.",
                    "",
                    "The `#b#status#q#` command will respect patterns in the #b#.vrmeta#q# directive file.",
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

        [ValueOption(0)]
        public string Folder { get; set; }
    }
    class Status : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            StatusVerbOptions localOptions = (StatusVerbOptions)options;
            System.IO.DirectoryInfo info = ActiveDirectory;
            if (localOptions.Folder != null)
            {
                System.IO.DirectoryInfo path = new System.IO.DirectoryInfo(localOptions.Folder);
                if (!path.Exists)
                {
                    Printer.PrintError("#x#Error:##\n  Path \"#b#{0}##\" does not exist!", localOptions.Folder);
                    return false;
                }
                if (!path.FullName.StartsWith(Workspace.Root.FullName))
                {
                    Printer.PrintError("#x#Error:##\n  Path \"#b#{0}##\" is not part of the vault!", localOptions.Folder);
                    return false;
                }
                info = path;
            }
            var ss = Workspace.GetStatus(info);
            Printer.WriteLineMessage("Version #b#{0}## on branch \"#b#{1}##\"", ss.CurrentVersion.ID, ss.Branch.Name);
            if (ss.RestrictedPath != null)
                Printer.WriteLineMessage("  Computing status for path: #b#{0}##", ss.RestrictedPath);
            Printer.WriteLineMessage("");
            int[] codeCount = new int[(int)StatusCode.Count];
            foreach (var x in ss.Elements)
            {
				if (x.Code == StatusCode.Unchanged)
                    continue;
                codeCount[(int)x.Code]++;
                if (x.Code == StatusCode.Ignored)
                    continue;
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
                Printer.WriteLineMessage("\n#b#Summary:##");
                for (int i = 0; i < codeCount.Length; i++)
                    Printer.WriteLineMessage("  {0} #b#{2}## #q#{1}##", codeCount[i], codeCount[i] != 1 ? "Objects" : "Object", ((StatusCode)i).ToString());
                Printer.WriteLineMessage("\n  {0}#q# files in ##{1}#q# diectories ({2} ignored)", ss.Files, ss.Directories, ss.IgnoredObjects);
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
                    return x.Staged ? new Tuple<char, string>('s', "(copied)")
                        : new Tuple<char, string>('w', "(copied)");
                case StatusCode.Deleted:
                    return x.Staged ? new Tuple<char, string>('c', "(deleted)")
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
    }
}
