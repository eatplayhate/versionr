using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class CheckoutVerbOptions : VerbOptionBase
    {
        public override BaseCommand GetCommand()
        {
            return new Checkout();
        }

        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}## #q#[options]## [branch or version]", Verb);
            }
        }
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Checkout will move the current workspace to a specific version (or the head of a specific branch).",
                    "",
                    "It will also remove any staged operations (including merge information)."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "checkout";
            }
        }
        [Option('f', "force", HelpText = "Allow checking out even if non-pristine files will be overwritten.")]
        public bool Force { get; set; }
        [Option('p', "purge", HelpText = "Remove all unversioned files from the repository")]
		public bool Purge { get; set; }

        [Option("partial", HelpText = "Sets a partial path within the vault.")]
        public string Partial { get; set; }

        [Option("metadata", HelpText = "Moves version tip and clears merge info, but does not alter the state of files on disk.")]
        public bool Metadata { get; set; }

        [ValueOption(0)]
        public string Target { get; set; }
    }
    class Checkout : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            CheckoutVerbOptions localOptions = options as CheckoutVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            string target;
            if (string.IsNullOrEmpty(localOptions.Target))
                target = Workspace.CurrentBranch.Name;
            else
                target = localOptions.Target;
            if (!localOptions.Force)
            {
                if (Workspace.Status.HasModifications(false))
                {
                    Printer.Write(Printer.MessageType.Error, "#x#Error:##\n  Vault contains uncommitted changes. Use the `#b#--force##` option to allow overwriting.\n");
                    return false;
                }
            }
            if (localOptions.Partial != null)
                Workspace.SetPartialPath(localOptions.Partial);
            Workspace.Checkout(target, localOptions.Purge, false, localOptions.Metadata);
			return true;
        }
    }
}
