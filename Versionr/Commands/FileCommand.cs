using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
	abstract class FileCommandVerbOptions : VerbOptionBase
	{
		[Option('g', "regex", HelpText = "Use regex pattern matching for arguments.", MutuallyExclusiveSet ="all")]
		public bool Regex { get; set; }
		[Option('n', "filename", HelpText = "Matches filenames regardless of full path.", MutuallyExclusiveSet = "all")]
		public bool Filename { get; set; }
		[Option('a', "all", HelpText = "Includes every non-pristine file.", MutuallyExclusiveSet = "regex recursive")]
		public bool All { get; set; }
		[Option('r', "recursive", DefaultValue = true, HelpText = "Recursively add objects in directories.")]
		public bool Recursive { get; set; }
		[Option('i', "insensitive", DefaultValue = true, HelpText = "Use case-insensitive matching for objects")]
		public bool Insensitive { get; set; }
		[Option('t', "tracked", HelpText = "Matches only files that are tracked by the vault")]
		public bool Tracked { get; set; }
		public override string Usage
		{
			get
			{
				return string.Format("#b#versionr #i#{0}#q# [options] ##file1 #q#[file2 ... fileN]", Verb);
			}
		}

		[ValueList(typeof(List<string>))]
		public IList<string> Objects { get; set; }
	}
	abstract class FileCommand : BaseWorkspaceCommand
	{
		protected override bool RunInternal(object options)
		{
			FileCommandVerbOptions localOptions = options as FileCommandVerbOptions;
			Printer.EnableDiagnostics = localOptions.Verbose;

			var status = Workspace.Status;
			List<Versionr.Status.StatusEntry> targets;

			if (localOptions.All)
				targets = status.Elements;
			else
				targets = status.GetElements(localOptions.Objects, localOptions.Regex, localOptions.Filename, localOptions.Insensitive);

			if (localOptions.Recursive)
				status.AddRecursiveElements(targets);

			if (localOptions.Tracked)
				targets = targets.Where(x => x.VersionControlRecord != null).ToList();

			if (targets.Count > 0 || !RequiresTargets)
				return RunInternal(Workspace, status, targets, localOptions);

			Printer.PrintWarning("No files selected for {0}", localOptions.Verb);
			return false;
		}

		protected abstract bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileCommandVerbOptions options);

		protected virtual bool RequiresTargets { get { return true; } }

	}
}
