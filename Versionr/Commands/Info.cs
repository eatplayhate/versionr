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
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0}", Verb);
            }
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
            Printer.WriteLineMessage("Version #b#{0}## on branch \"#b#{1}##\" (rev {2})", Workspace.Version.ID, Workspace.CurrentBranch.Name, Workspace.Version.Revision);
            Printer.WriteLineMessage(" - Committed at: #b#{0}##", Workspace.Version.Timestamp.ToLocalTime().ToString());
            Printer.WriteLineMessage(" - Branch ID: #c#{0}##", Workspace.CurrentBranch.ShortID);
            return true;
        }
    }
}
