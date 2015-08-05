using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class RenameBranchVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [--branch name] newname", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Renames a branch."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "renamebranch";
            }
        }
        [Option('b', "branch", HelpText = "Selects a branch to rename, if not specified it will rename the current branch.")]
        public string Branch { get; set; }

        [ValueOption(0)]
        public string Name { get; set; }
    }
    class RenameBranch : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            RenameBranchVerbOptions localOptions = options as RenameBranchVerbOptions;

            Objects.Branch branch;
            bool multiple;
            if (string.IsNullOrEmpty(localOptions.Branch))
                branch = Workspace.CurrentBranch;
            else
                branch = Workspace.GetBranchByPartialName(localOptions.Branch, out multiple);

            if (branch == null)
            {
                Printer.PrintError("#x#Error:##\n Can't rename branch - unable to identify object for rename.");
                return false;
            }

            bool result = Workspace.RenameBranch(branch, localOptions.Name);
            if (result == true)
                Printer.PrintMessage("Renamed branch #c#{0}## from \"#b#{1}##\" to \"#b#{2}##\".", branch.ID, branch.Name, localOptions.Name);
            else
                Printer.PrintError("#x#Error:##\n Can't rename branch.");
            return result;
        }
    }
}
