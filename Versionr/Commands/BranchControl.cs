using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class BranchControlVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## #q#[-b branchname]## <options>", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Modifies branch metadata."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "branch-control";
            }
        }
        [Option('b', "branch", Required = false, HelpText = "Selects a branch to modify, if not specified the target will be the current branch.")]
        public string Branch { get; set; }
        [Option("disallow-merge", Required = false, MutuallyExclusiveSet = "merge", HelpText = "Disallows merging from the specified branch into the target branch.")]
        public string DisallowMerge { get; set; }
        [Option("allow-merge", Required = false, MutuallyExclusiveSet = "merge", HelpText = "Allows merging from the specified branch into the target branch.")]
        public string AllowMerge { get; set; }
        
        public override BaseCommand GetCommand()
        {
            return new BranchControl();
        }
    }
    class BranchControl : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            BranchControlVerbOptions localOptions = options as BranchControlVerbOptions;

            Objects.Branch branch;
            bool multiple;
            if (string.IsNullOrEmpty(localOptions.Branch))
                branch = Workspace.CurrentBranch;
            else
                branch = Workspace.GetBranchByPartialName(localOptions.Branch, out multiple);

            if (branch == null)
            {
                Printer.PrintError("#x#Error:##\n Can't modify branch - unable to identify object for rename.");
                return false;
            }

            bool didsomething = false;

            if (!string.IsNullOrEmpty(localOptions.DisallowMerge))
            {
                Objects.Branch otherBranch = Workspace.GetBranchByPartialName(localOptions.DisallowMerge, out multiple);
                if (otherBranch == null)
                {
                    Printer.PrintError("#x#Error:##\n Can't modify branch information - unknown branch for disallowing merges \"{0}\".", localOptions.DisallowMerge);
                    return false;
                }
                didsomething = true;
                if (Workspace.DisallowMerge(branch, otherBranch))
                    Printer.PrintMessage("#s#Success:## now disallowing merges from branch #b#\"{0}\"## ({1}) into #b#\"{2}\"## ({3})", otherBranch.Name, otherBranch.ShortID, branch.Name, branch.ShortID);
                else
                    Printer.PrintMessage("#w#Failure:## merges already disallowed.");
            }
            if (!string.IsNullOrEmpty(localOptions.AllowMerge))
            {
                Objects.Branch otherBranch = Workspace.GetBranchByPartialName(localOptions.AllowMerge, out multiple);
                if (otherBranch == null)
                {
                    Printer.PrintError("#x#Error:##\n Can't modify branch information - unknown branch for allowing merges \"{0}\".", localOptions.DisallowMerge);
                    return false;
                }
                didsomething = true;
                if (Workspace.AllowMerge(branch, otherBranch))
                    Printer.PrintMessage("#s#Success:## now allowing merges from branch #b#\"{0}\"## ({1}) into #b#\"{2}\"## ({3})", otherBranch.Name, otherBranch.ShortID, branch.Name, branch.ShortID);
                else
                    Printer.PrintMessage("#w#Failure:## merges already allowed.");
            }
            if (!didsomething)
            {
                var metainfo = Workspace.GetBranchMetadata(branch);
                Printer.PrintMessage("Displaying branch metadata info for #b#\"{0}\"## ({1})", branch.Name, branch.ID);
                foreach (var x in metainfo)
                {
                    string inherited = string.Empty;
                    if (x.Branch != branch.ID)
                        inherited = " #q#(inherited from \"" + Workspace.GetBranch(x.Branch).Name + "\")##";
                    Printer.PrintMessage("[{0}] - {1}{2}", x.Type, x.Operand1, inherited);
                }
            }
            return true;
        }
    }
}
