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
	}
}
