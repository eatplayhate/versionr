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
		[Option('g', "regex", DefaultValue = false, HelpText = "Use regex pattern matching for arguments.", MutuallyExclusiveSet ="all")]
		public bool Regex { get; set; }
		[Option('n', "filename", DefaultValue = false, HelpText = "Matches filenames regardless of full path.", MutuallyExclusiveSet = "all")]
		public bool Filename { get; set; }
		[Option('a', "all", DefaultValue = false, HelpText = "Includes every changed or unversioned file.", MutuallyExclusiveSet = "regex recursive")]
		public bool All { get; set; }
		[Option('r', "recursive", DefaultValue = true, HelpText = "Recursively add objects in directories.")]
		public bool Recursive { get; set; }
		[Option('i', "insensitive", DefaultValue = true, HelpText = "Use case-insensitive matching for objects")]
		public bool Insensitive { get; set; }
		[Option('o', "modified", HelpText = "Matches only modified files")]
		public bool Modified { get; set; }
		public override string Usage
		{
			get
			{
				return string.Format("Usage: versionr {0} [options] file1 [file2 ... fileN]", Verb);
			}
		}

		[ValueList(typeof(List<string>))]
		public IList<string> Objects { get; set; }
	}
	abstract class FileCommand : BaseCommand
	{
		public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
		{
			FileCommandVerbOptions localOptions = options as FileCommandVerbOptions;
			Printer.EnableDiagnostics = localOptions.Verbose;
			Area ws = Area.Load(workingDirectory);
			if (ws == null)
				return false;

			var status = ws.Status;
			List<Versionr.Status.StatusEntry> targets;

			if (localOptions.All)
				targets = status.Elements;
			else
				targets = status.GetElements(localOptions.Objects, localOptions.Regex, localOptions.Filename, localOptions.Insensitive);

			if (localOptions.Recursive)
				status.AddRecursiveElements(targets);

			if (localOptions.Modified)
				targets = targets.Where(x => x.Code == StatusCode.Modified).ToList();

			if (targets.Count > 0 || !RequiresTargets)
				return RunInternal(ws, status, targets, localOptions);

			Printer.PrintWarning("No files selected for {0}", localOptions.Verb);
			return false;
		}

		protected abstract bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileCommandVerbOptions options);

		protected virtual bool RequiresTargets { get { return true; } }

	}
}
