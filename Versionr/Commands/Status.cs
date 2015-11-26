using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class StatusVerbOptions : FileBaseCommandVerbOptions
    {
        [Option('l', "nolist", HelpText = "Does not display a listing of file statuses.")]
        public bool NoList { get; set; }
        [Option('s', "summary", HelpText = "Displays a summary at the end of the status block.")]
        public bool Summary { get; set; }
		[Option('a', "all", HelpText = "Includes unchanged files.")]
		public bool All { get; set; }
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
                    "  #e#Conflict##: The file #b#requires intervention## to finish merging. It will #e#obstruct the next commit## until it is resolved.",
					"  #q#Unchanged##: The object has no changes. Only displayed with the #b#--all## option.",
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
    }
    class Status : FileBaseCommand
    {
        protected override void Start()
        {
            Printer.WriteLineMessage("Version #b#{0}## on branch \"#b#{1}##\" (rev {2})", Workspace.Version.ID, Workspace.CurrentBranch.Name, Workspace.Version.Revision);
        }
        protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
        {
            StatusVerbOptions localOptions = (StatusVerbOptions)options;
            if (localOptions.Objects != null && localOptions.Objects.Count > 0)
            {
                if (targets.Count == 0)
                {
                    Printer.PrintError("#x#Error:##\n  Could not find objects matching pattern #b#{0}##", GetPatterns(localOptions.Objects));
                    if (ActiveDirectory.FullName != Workspace.Root.FullName)
                        Printer.PrintMessage("  - Relative to folder \"#b#{0}##\"", Workspace.GetLocalPath(ActiveDirectory.FullName));
                    return false;
                }
            }
            var ss = status;
            if (!string.IsNullOrEmpty(ss.RestrictedPath))
                Printer.WriteLineMessage("  Computing status for path: #b#{0}##", ss.RestrictedPath);
            Printer.WriteLineMessage("");
            int[] codeCount = new int[(int)StatusCode.Count];
            if (status.MergeInputs.Count > 0)
                Printer.WriteLineMessage("Workspace has #b#{0}## pending merges.", status.MergeInputs.Count);
            foreach (var x in status.MergeInputs)
            {
                Printer.WriteLineMessage(" #c#{0}#q# from branch \"#b#{1}##\" (rev {2})", x.ID, Workspace.GetBranch(x.Branch).Name, x.Revision);
            }
            if (status.MergeInputs.Count > 0)
                Printer.WriteLineMessage("");
            IEnumerable<Versionr.Status.StatusEntry> operands = targets.Where(x => { codeCount[(int)x.Code]++; return x.Code != StatusCode.Ignored; });
            if (!localOptions.All)
                operands = operands.Where(x => x.Code != StatusCode.Unchanged);
            string localRestrictedPath = null;
            if (ss.RestrictedPath != null)
                localRestrictedPath = ws.GetLocalCanonicalName(ss.RestrictedPath);
            if (!localOptions.NoList)
            {
                foreach (var x in operands.OrderBy(x => x.CanonicalName))
                {
                    string name = ws.GetLocalCanonicalName(x.CanonicalName);
                    if (localRestrictedPath != null)
                        name = name.Substring(localRestrictedPath.Length);
                    int index = name.LastIndexOf('/');
                    if (index != name.Length - 1)
                        name = name.Insert(index + 1, "#b#");
                    if (name.Length == 0)
                        name = "#q#<parent directory>##";
					if (x.IsSymlink)
						name += " #q# -> " + (x.FilesystemEntry != null ? x.FilesystemEntry.SymlinkTarget : x.VersionControlRecord.Fingerprint);
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

            foreach (var x in Workspace.Externs)
            {
                bool include = true;
                if (!string.IsNullOrEmpty(ss.RestrictedPath))
                    include = Workspace.PathContains(ss.RestrictedPath, System.IO.Path.Combine(ws.Root.FullName, x.Value.Location));
                else
                    include = Filter(new KeyValuePair<string, object>[] { new KeyValuePair<string, object>(x.Value.Location, new object()) }).FirstOrDefault().Value != null;
                if (include)
                {
                    System.IO.DirectoryInfo directory = new System.IO.DirectoryInfo(System.IO.Path.Combine(Workspace.Root.FullName, x.Value.Location));
                    if (directory.Exists)
                    {
                        Printer.WriteLineMessage("\nExternal #c#{0}## ({1}):", x.Key, x.Value.Location);
                        new Status() { DirectExtern = true }.Run(directory, options);
                    }
                    else
                        Printer.WriteLineMessage("\nExternal #c#{0}## ({1}): #e#missing##", x.Key, x.Value.Location);
                }
            }
            return true;
        }

        private string GetPatterns(IList<string> objects)
        {
            var patterns = objects.Select(x => "`" + x + "`").ToList();
            if (patterns.Count == 1)
                return patterns[0];
            return "[" + string.Join(", ", patterns) + "]";
        }

        private string GetStatus(Versionr.Status.StatusEntry x)
        {
            var info = GetStatusText(x);
            string text = "(" + info.Item2 + ")";
            while (text.Length < 14)
                text = " " + text;
            text = "#" + info.Item1 + "#" + text;
            if (x.Staged)
                text += "#b#*##";
            else
                text += " ";
            return text;
        }

        protected override bool ComputeTargets(FileBaseCommandVerbOptions localOptions)
        {
            return true;
        }

        protected override bool OnNoTargetsAssumeAll
        {
            get
            {
                return true;
            }
        }

        public static Tuple<char, string> GetStatusText(Versionr.Status.StatusEntry x)
        {
            return GetStatusText(x.Code, x.Staged);
        }

        public static Tuple<char, string> GetStatusText(Versionr.StatusCode code, bool staged)
        {
            switch (code)
            {
                case StatusCode.Added:
                    return staged ? new Tuple<char, string>('s', "added")
                        : new Tuple<char, string>('e', "error");
                case StatusCode.Conflict:
                    return staged ? new Tuple<char, string>('e', "conflict")
                        : new Tuple<char, string>('e', "conflict");
                case StatusCode.Obstructed:
                    return staged ? new Tuple<char, string>('e', "obstructed")
                        : new Tuple<char, string>('e', "obstructed");
                case StatusCode.Copied:
                    return staged ? new Tuple<char, string>('s', "copied")
                        : new Tuple<char, string>('w', "copied");
                case StatusCode.Deleted:
                    return staged ? new Tuple<char, string>('c', "deleted")
                        : new Tuple<char, string>('w', "missing");
                case StatusCode.Missing:
                    return staged ? new Tuple<char, string>('e', "error")
                        : new Tuple<char, string>('w', "missing");
                case StatusCode.Masked:
                    return staged ? new Tuple<char, string>('c', "merged")
                        : new Tuple<char, string>('w', "ignored");
                case StatusCode.Modified:
                    return staged ? new Tuple<char, string>('s', "modified")
                        : new Tuple<char, string>('w', "changed");
                case StatusCode.Renamed:
                    return staged ? new Tuple<char, string>('s', "renamed")
                        : new Tuple<char, string>('w', "renamed");
                case StatusCode.Unversioned:
                    return staged ? new Tuple<char, string>('e', "error")
                        : new Tuple<char, string>('w', "unversioned");
				case StatusCode.Ignored:
					return staged ? new Tuple<char, string>('e', "error")
						: new Tuple<char, string>('q', "ignored");
				case StatusCode.Unchanged:
					return staged ? new Tuple<char, string>('e', "unchanged")
						: new Tuple<char, string>('q', "unchanged");
				default:
                    throw new Exception();
            }
        }
    }
}
