using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Network;

namespace Versionr.Commands
{
    class AheadVerbOptions : RemoteCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Determines if the selected branch is ahead or behind the remote."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "ahead";
            }
        }
        [Option('b', "branch", HelpText = "The name of the branch to check.")]
        public string Branch { get; set; }
    }
    class Ahead : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
        {
            AheadVerbOptions localOptions = options as AheadVerbOptions;
            Info.DisplayInfo(client.Workspace);
            Objects.Branch localBranch = client.Workspace.CurrentBranch;
            if (!string.IsNullOrEmpty(localOptions.Branch))
            {
                bool multiple;
                localBranch = client.Workspace.GetBranchByPartialName(localOptions.Branch, out multiple);
                if (localBranch == null)
                {
                    Printer.PrintError("#e#Error:## Local branch #b#`{0}`## not found.", localOptions.Branch);
                    return false;
                }
            }
            var branches = client.ListBranches();
            if (branches == null)
            {
                Printer.PrintError("#e#Error:## Server does not support branch list operation.");
                return false;
            }
            
            foreach (var x in branches.Item1)
            {
                if (x.ID != localBranch.ID)
                    continue;
                if (x.Terminus.HasValue)
                {
                    var terminus = branches.Item3[x.Terminus.Value];
                    bool present = client.Workspace.GetVersion(x.Terminus.Value) != null;
                    string presentMarker = present ? "" : " #w#(behind)##";
                    if (present && localBranch != null)
                    {
                        if (localBranch.Terminus.Value != x.Terminus.Value)
                            presentMarker += " #w#(ahead)##";
                        else
                            presentMarker += " #s#(up-to-date)##";
                    }
                    if (localBranch != null && !localBranch.Terminus.HasValue)
                        presentMarker += " #w#(not locally deleted)##";
                    if (localBranch == null)
                        presentMarker += " #w#(not synchronized)##";
                    Printer.PrintMessage("Remote - #e#(deleted)## - Last version: #b#{0}##{3}, #q#{2} {1}##", terminus.ShortName, terminus.Timestamp.ToLocalTime(), terminus.Author, presentMarker);
                }
                foreach (var z in branches.Item2.Where(y => y.Key == x.ID))
                {
                    bool present = client.Workspace.GetVersion(z.Value) != null;
                    string presentMarker = present ? "" : " #w#(behind)##";
                    if (present && localBranch != null)
                    {
                        var localHeads = client.Workspace.GetBranchHeads(localBranch);
                        if (localHeads.Count == 1 && localHeads[0].Version != z.Value)
                            presentMarker += " #w#(ahead)##";
                        else
                            presentMarker += " #s#(up-to-date)##";
                    }
                    if (localBranch != null && localBranch.Terminus.HasValue)
                        presentMarker += " #e#(locally deleted)##";
                    if (localBranch == null)
                        presentMarker += " #w#(not synchronized)##";
                    var head = branches.Item3[z.Value];
                    Printer.PrintMessage("Remote - #s#(active)## - Version: #b#{0}##{3}, #q#{2} {1}##", head.ShortName, head.Timestamp.ToLocalTime(), head.Author, presentMarker);
                }
            }
            return true;
        }

        protected override bool Headless
        {
            get
            {
                return true;
            }
        }

        protected override bool UpdateRemoteTimestamp
        {
            get
            {
                return true;
            }
        }
    }
}
