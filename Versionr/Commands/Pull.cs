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
                    "abandon all ships"
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
        [Option('a', "all", DefaultValue = true, HelpText = "Pull all branches on the server.")]
        public bool PullAll { get; set; }
        [Option('u', "update", DefaultValue = false, HelpText = "Update the local revision after pulling data.")]
        public bool Update { get; set; }
    }
    class Pull : RemoteCommand
    {
        protected override bool RunInternal(Client client, RemoteCommandVerbOptions options)
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
                    if (x.Terminus.HasValue)
                    {
                        var terminus = branches.Item3[x.Terminus.Value];
                        Printer.PrintMessage("  #e#(deleted)## - Last version: #b#{0}##, #q#{2} {1}##", terminus.ShortName, terminus.Timestamp.ToLocalTime(), terminus.Author);
                    }
                    foreach (var z in branches.Item2.Where(y => y.Key == x.ID))
                    {
                        var head = branches.Item3[z.Value];
                        Printer.PrintMessage("  #s#(head)## - Version: #b#{0}##, #q#{2} {1}##", head.ShortName, head.Timestamp.ToLocalTime(), head.Author);
                    }
                }
                return true;
            }

            if (!client.Pull(localOptions.PullObjects.HasValue ? localOptions.PullObjects.Value : objects, localOptions.RemoteBranch, localOptions.PullAll))
                return false;
            if (localOptions.Update)
            {
                client.Workspace.Update();
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
