using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Commands;

namespace Vsr2Git.Commands
{
	public class ReplicateToGitOptions : Versionr.VerbOptionBase
	{
		public override string[] Description
		{
			get
			{
				return new string[] { "Replicate to a Git repository " };
			}
		}

		public override string Verb
		{
			get
			{
				return "replicate-to-git";
			}
		}

		public override BaseCommand GetCommand()
		{
			return new ReplicateToGit();
		}
	}

	class ReplicateToGit : BaseCommand
	{
		public bool Run(DirectoryInfo workingDirectory, object options)
		{
			throw new NotImplementedException();
		}
	}
}
