using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class DeleteBranchVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [branch name]", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Deletes a branch."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "deletebranch";
            }
        }
        [ValueOption(0)]
        public string Branch { get; set; }
    }
    class DeleteBranch : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            DeleteBranchVerbOptions localOptions = options as DeleteBranchVerbOptions;

            Objects.Branch branch;
            bool multiple;
            if (string.IsNullOrEmpty(localOptions.Branch))
                branch = Workspace.CurrentBranch;
            else
                branch = Workspace.GetBranchByPartialName(localOptions.Branch, out multiple);

            if (branch == null)
            {
                Printer.PrintError("#x#Error:##\n Can't delete branch - unable to identify branch.");
                return false;
            }
            
            bool result = Workspace.DeleteBranch(branch);
            if (result == true)
            {
                Printer.PrintMessage("Deleted branch \"#b#{1}##\" (#c#{0}##).", branch.ID, branch.Name);
            }
            else
                Printer.PrintError("#x#Error:##\n Can't delete branch.");
            return result;
        }
    }
}
