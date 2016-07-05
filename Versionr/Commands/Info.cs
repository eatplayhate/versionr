﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class InfoVerbOptions : VerbOptionBase
    {
        public override BaseCommand GetCommand()
        {
            return new Info();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Displays current branch/version information."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "info";
            }
        }
    }
    class Info : BaseWorkspaceCommand
    {
        public override bool Headless { get { return true; } }
        protected override bool RunInternal(object options)
        {
            DisplayInfo(Workspace);
            return true;
        }

        internal static void DisplayInfo(Area workspace)
        {
            Printer.WriteLineMessage("Version #b#{0}## on branch \"#b#{1}##\" (rev {2})", workspace.Version.ID, workspace.CurrentBranch.Name, workspace.Version.Revision);
            Printer.WriteLineMessage(" - Committed at: #b#{0}##", workspace.Version.Timestamp.ToLocalTime().ToString());
            Printer.WriteLineMessage(" - Branch ID: #c#{0}##", workspace.CurrentBranch.ShortID);
        }
    }
}
