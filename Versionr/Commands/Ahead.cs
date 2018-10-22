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
        public override BaseCommand GetCommand()
        {
            return new Ahead();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Determines if the current (or selected) branch is ahead or behind the specified remote."
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
        [Option('b', "branch", HelpText = "The name of the branch to check.", MutuallyExclusiveSet = "branchselect")]
        public string Branch { get; set; }

        [Option('a', "all-branches", HelpText = "Show all branches ahead or behind server", MutuallyExclusiveSet = "branchselect")]
        public bool AllBranches { get; set; }

        [Option('u', "uninteresting", HelpText = "Include up-to-date and non-synchronised branches. Requires all-branches option")]
        public bool ShowUninteresting { get; set; } = false;

        [Option('d', "deleted", HelpText = "Include deleted branches. Requires all-branches option")]
        public bool IncludeDeleted { get; set; } = false;

    }
    class Ahead : RemoteCommand
    {
        protected override bool RunInternal(IRemoteClient client, RemoteCommandVerbOptions options)
        {
            AheadVerbOptions localOptions = options as AheadVerbOptions;
            Info.DisplayInfo(client.Workspace);
            Objects.Branch desiredBranch = client.Workspace.CurrentBranch;
            if (!string.IsNullOrEmpty(localOptions.Branch))
            {
                bool multiple;
                desiredBranch = client.Workspace.GetBranchByPartialName(localOptions.Branch, out multiple);
                if (desiredBranch == null)
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
                if (x.ID != desiredBranch.ID && !localOptions.AllBranches)
                    continue;

                var localBranch = client.Workspace.GetBranch(x.ID);

                if (localOptions.AllBranches && localBranch == null && !localOptions.ShowUninteresting)
                    continue;

                if (x.Terminus.HasValue && (!localOptions.AllBranches || localOptions.IncludeDeleted))
                {
                    bool present = client.Workspace.GetVersion(x.Terminus.Value) != null;

                    Objects.Version terminus = null;
                    branches.Item3.TryGetValue(x.Terminus.Value, out terminus);

                    string presentMarker = present ? "" : " #w#(behind)##";
                    if (present && localBranch != null)
                    {
                        if (localBranch.Terminus.Value != x.Terminus.Value)
                            presentMarker += " #w#(ahead)##";
                        else
                        {
                            if (localOptions.AllBranches && !localOptions.ShowUninteresting)
                                continue;

                            presentMarker += " #s#(up-to-date)##";
                        }
                    }
                    if (localBranch != null && !localBranch.Terminus.HasValue)
                        presentMarker += " #w#(not locally deleted)##";
                    if (localBranch == null)
                        presentMarker += " #w#(not synchronized)##";

                    string branchMarker = localOptions.AllBranches ? "#b#" + x.Name + "##" : "";

                    if (terminus == null)
                    {
                        Printer.PrintMessage("Remote - #e#(deleted)## {2} - Last version: #e#(unknown)## #b#{0}##{1}", x.Terminus.Value, presentMarker, branchMarker);
                    }
                    else
                        Printer.PrintMessage("Remote - #e#(deleted)## {4} - Last version: #b#{0}##{3}, #q#{2} {1}##", terminus.ShortName, terminus.Timestamp.ToLocalTime(), terminus.Author, presentMarker, branchMarker);
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
                        {
                            if (localOptions.AllBranches && !localOptions.ShowUninteresting)
                                continue;
                            presentMarker += " #s#(up-to-date)##";
                        }
                    }
                    if (localBranch != null && localBranch.Terminus.HasValue)
                        presentMarker += " #e#(locally deleted)##";
                    if (localBranch == null)
                        presentMarker += " #w#(not synchronized)##";

                    string branchMarker = localOptions.AllBranches ? "#b#" + x.Name + "##" : "";

                    var head = branches.Item3[z.Value];
                    Printer.PrintMessage("Remote - #s#(active)## {4} - Version: #b#{0}##{3}, #q#{2} {1}##", head.ShortName, head.Timestamp.ToLocalTime(), head.Author, presentMarker, branchMarker);
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
