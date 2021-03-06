﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    class PullVerbOptions : RemoteCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Pull will download all revisions up to the head of the current (or a specified) branch from a remote server.",
                    "",
                    "By using the #b#--update## option, after data is downloaded, the vault will automatically run the #i#Update## operation.",
                    "",
                    "Remotes can be configured using the #b#set-remote## command."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "pull";
            }
        }
        [Option('l', "list", HelpText = "Lists branches on the remote server, but does not pull them.")]
        public bool List { get; set; }
        [Option('d', "deleted", HelpText = "Requires --list, shows deleted branches.")]
        public bool Deleted { get; set; }
        [Option('b', "branch", HelpText="The name of the branch to pull.")]
        public string RemoteBranch { get; set; }
        [Option('o', "objects", HelpText = "Retrieve remote object payloads as well as metadata.")]
        public bool? PullObjects { get; set; }
        [Option('a', "all", HelpText = "Pull all branches on the server.")]
        public bool PullAll { get; set; }
        [Option('u', "update", DefaultValue = false, HelpText = "Update the local revision after pulling data.")]
        public bool Update { get; set; }
        [Option('D', "accept-deletes", HelpText ="Accept all incoming branch-deletes without prompting, even if the branch has an updated terminus.")]
        public bool AcceptDeletes { get; set; }

        public override BaseCommand GetCommand()
        {
            return new Pull();
        }
    }
    class Pull : RemoteCommand
    {
        protected override bool RunInternal(IRemoteClient client, RemoteCommandVerbOptions options)
        {
            PullVerbOptions localOptions = options as PullVerbOptions;
            bool objects = true;
            if (localOptions.PullAll)
                objects = false;
            if (localOptions.List)
            {
                var branches = client.ListBranches();
                if (branches == null)
                    return false;
                Printer.PrintMessage("Displaying remote branches:");
                foreach (var x in branches.Item1)
                {
                    if (!localOptions.Deleted && x.Terminus.HasValue)
                        continue;
                    string tipmarker = "";
                    if (x.ID == client.Workspace.CurrentBranch.ID)
                        tipmarker = " #w#*<current>##";
                    Printer.PrintMessage("#b#{1}## - #c#{0}##{2}", x.ID, x.Name, tipmarker);
                    string heading = string.Empty;
                    var localBranch = client.Workspace.GetBranch(x.ID);
                    if (x.Terminus.HasValue)
                    {
                        Objects.Version terminus = null;
                        bool present = client.Workspace.GetVersion(x.Terminus.Value) != null;
                        string presentMarker = present ? "" : " #w#(behind)##";
                        if (!branches.Item3.TryGetValue(x.Terminus.Value, out terminus))
                        {
                            Printer.PrintMessage("  #e#(deleted)## - #w#(no data)##");
                            continue;
                        }
                        if (present && localBranch != null)
                        {
                            if (localBranch.Terminus.Value != x.Terminus.Value)
                                presentMarker += " #w#(ahead)##";
                        }
                        if (localBranch != null && !localBranch.Terminus.HasValue)
                            presentMarker += " #w#(not locally deleted)##";
                        if (localBranch == null)
                            presentMarker += " #w#(not synchronized)##";
                        Printer.PrintMessage("  #e#(deleted)## - Last version: #b#{0}##{3}, #q#{2} {1}##", terminus.ShortName, terminus.Timestamp.ToLocalTime(), terminus.Author, presentMarker);
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
                        }
                        if (localBranch != null && localBranch.Terminus.HasValue)
                            presentMarker += " #e#(locally deleted)##";
                        if (localBranch == null)
                            presentMarker += " #w#(not synchronized)##";
                        var head = branches.Item3[z.Value];
                        Printer.PrintMessage("  #s#(head)## - Version: #b#{0}##{3}, #q#{2} {1}##", head.ShortName, head.Timestamp.ToLocalTime(), head.Author, presentMarker);
                    }
                }
                return true;
            }

            if (!client.Pull(localOptions.PullObjects.HasValue ? localOptions.PullObjects.Value : objects, localOptions.RemoteBranch, localOptions.PullAll, localOptions.AcceptDeletes))
                return false;
            if (localOptions.Update)
            {
                client.Workspace.Update(new Area.MergeSpecialOptions());
            }
            return true;
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
