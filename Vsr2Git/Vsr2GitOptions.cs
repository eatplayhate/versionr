using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vsr2Git
{
	public class Vsr2GitOptions
	{
		[VerbOption("replicate-to-git", HelpText = "Replicate Versionr changes to a git repository")]
		public Commands.ReplicateToGitOptions ReplicateToGit { get; set; }
        [HelpOption]
        public string GetUsage()
        {
            var help = new CommandLine.Text.HelpText
            {
                Heading = new CommandLine.Text.HeadingInfo("Plugin: #b#Vsr2Git##"),
                AddDashesToOption = false,
            };
            help.AddPreOptionsLine("Enables replication of the versionr vault to a git repository.\n\n#b#Commands:");
            help.ShowHelpOption = false;
            help.AddOptions(this);
            return help;
        }
    }
}
