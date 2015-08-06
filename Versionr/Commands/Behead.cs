using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class BeheadVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [--branch branchname] version", Verb);
            }
        }

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
                return "behead";
            }
        }
        
        [Option('b', "branch", HelpText = "Specifies a branch to behead the version from.")]
        public string Branch { get; set; }

        [Option('f', "force", HelpText = "Allows removing the only head of a branch.")]
        public bool Force { get; set; }

        [ValueOption(0)]
        public string Target { get; set; }
    }
    class Behead : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            BeheadVerbOptions localOptions = options as BeheadVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            Objects.Version version;
            if (!ws.FindVersion(localOptions.Target, out version))
			{
				if (localOptions.Force)
				{
					if (!ws.ForceBehead(localOptions.Target))
					{
						Printer.PrintError("No head pointing to partial name \"{0}.\"", localOptions.Target);
						return false;
					}
					return true;
				}
				else
				{
					Printer.PrintError("Can't find version with partial name \"{0}.\"", localOptions.Target);
                    return false;
				}

			}
			List<Objects.Head> heads = ws.GetHeads(version.ID);
            if (heads.Count == 0)
            {
                Printer.PrintError("Can't behead version {0}, version is not a head.", version.ID);
                return false;
            }
            if (string.IsNullOrEmpty(localOptions.Branch))
            {
                if (heads.Count != 1)
                {
                    Printer.PrintError("Can't behead version {0}, version is present on {1} branches.", version.ID, heads.Count);
                    foreach (var x in heads)
                        Printer.PrintError("Could be head for branch \"{0}\"", ws.GetBranch(x.Branch).Name);
                    Printer.PrintError("Use the --branch option to specify the head more precisely.");
                    return false;
                }
                var branchHeads = ws.GetBranchHeads(ws.GetBranch(heads[0].Branch));
                if (branchHeads.Count == 1 && !localOptions.Force)
                {
                    Printer.PrintError("Can't behead version - this would leave branch \"{0}\" with no head!", ws.GetBranch(heads[0].Branch).Name);
                    Printer.PrintError("Use --force if this is what you want.");
                }
                return ws.RemoveHead(heads[0]);
            }
            else
            {
                foreach (var x in heads)
                {
                    Objects.Branch b = ws.GetBranch(x.Branch);
                    if (b.Name == localOptions.Branch)
                    {
                        var branchHeads = ws.GetBranchHeads(b);
                        if (branchHeads.Count == 1 && !localOptions.Force)
                        {
                            Printer.PrintError("Can't behead version - this would leave branch \"{0}\" with no head!", b.Name);
                            Printer.PrintError("Use --force if this is what you want.");
							return false;
                        }
                        return ws.RemoveHead(x);
                    }
                }
                Printer.PrintError("Couldn't find branch head with branch: \"{0}\" and version {1}.", localOptions.Branch, version.ID);
            }
            return false;
        }
    }
}
