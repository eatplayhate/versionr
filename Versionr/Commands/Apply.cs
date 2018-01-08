using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class ApplyVerbOptions : VerbOptionBase
    {
        public override BaseCommand GetCommand()
        {
            return new Apply();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Applies a patch file."
                };
            }
        }

        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## [patch file]", Verb);
            }
        }

        public override string Verb
        {
            get
            {
                return "apply";
            }
        }

        [ValueOption(0)]
        public string PatchFile { get; set; }
    }
    class Apply : BaseWorkspaceCommand
    {
        public override bool Headless { get { return true; } }
        protected override bool RunInternal(object options)
        {
            ApplyVerbOptions localOptions = options as ApplyVerbOptions;
            if (System.IO.File.Exists(localOptions.PatchFile))
            {
                return Workspace.ParseAndApplyPatch(localOptions.PatchFile);
            }
            Printer.PrintError("#e#Can't find patch file to load: {0}##", localOptions.PatchFile);
            return false;
        }

        internal static void DisplayInfo(Area workspace)
        {
            Printer.WriteLineMessage("Version #b#{0}## on branch \"#b#{1}##\" (rev {2})", workspace.Version.ID, workspace.CurrentBranch.Name, workspace.Version.Revision);
            Printer.WriteLineMessage(" - Committed at: #b#{0}##", workspace.Version.Timestamp.ToLocalTime().ToString());
            Printer.WriteLineMessage(" - Branch ID: #c#{0}##", workspace.CurrentBranch.ShortID);
        }
    }
}
