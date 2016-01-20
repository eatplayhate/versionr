using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class ListBranchVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [options] [name]", Verb);
            }
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Lists branches, optionally filtering by name or ID."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "listbranch";
            }
        }
        [Option('d', "deleted", HelpText = "Include deleted branches.")]
        public bool Deleted { get; set; }
        [Option('p', "partial", HelpText = "Use partial name matching.")]
        public bool Partial { get; set; }

        [ValueOption(0)]
        public string Name { get; set; }
    }
    class ListBranch : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            ListBranchVerbOptions localOptions = options as ListBranchVerbOptions;

            var branches = Workspace.GetBranches(localOptions.Name, localOptions.Deleted, localOptions.Partial);
            foreach (var x in branches)
            {
                string tipmarker = "";
                if (x.ID == Workspace.CurrentBranch.ID)
                    tipmarker = " #w#*<current>##";
                Printer.PrintMessage("#b#{1}## - #c#{0}##{2}", x.ID, x.Name, tipmarker);
                string heading = string.Empty;
                if (x.Terminus.HasValue)
                {
                    var terminus = Workspace.GetVersion(x.Terminus.Value);
                    Printer.PrintMessage("  #e#(deleted)## - Last version: #b#{0}##, #q#{2} {1}##", terminus.ShortName, terminus.Timestamp.ToLocalTime(), terminus.Author);
                }
                var heads = Workspace.GetBranchHeads(x);
                foreach (var z in heads)
                {
                    var head = Workspace.GetVersion(z.Version);
                    Printer.PrintMessage("  #s#(head)## - Version: #b#{0}##, #q#{2} {1}##", head.ShortName, head.Timestamp.ToLocalTime(), head.Author);
                }
				Printer.PrintMessage("");
            }
            return true;
        }
    }
}
