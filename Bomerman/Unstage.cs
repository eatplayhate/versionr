using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;
using Versionr.Commands;
using Versionr.LocalState;

namespace Bomerman
{
    public class BomUnstageOptions : VerbOptionBase
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Removes unchanged files from stage."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "bom-unstage";
            }
        }
        public override BaseCommand GetCommand()
        {
            return new BomUnstage();
        }
    }
    public class BomUnstage : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            var status = Workspace.GetStatus(ActiveDirectory);
            List<Status.StatusEntry> targets = new List<Status.StatusEntry>();
            foreach (var s in status.Elements)
            {
                if (s.Staged && s.Code == StatusCode.Unchanged)
                {
                    targets.Add(s);
                }
            }
            if (targets.Count > 0)
            {
                Workspace.Revert(targets, false, false, false);
                Printer.PrintMessage("Unstaged pristine {0} files.", targets.Count);
            }
            return true;
        }
    }
}
