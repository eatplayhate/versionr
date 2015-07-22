using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
	class LogVerbOptions : VerbOptionBase
	{
		public override string Usage
		{
			get
			{
				return string.Format("Usage: versionr {0} [options] [file]", Verb);
			}
		}

		public override string[] Description
		{
			get
			{
				return new string[]
				{
					"get a log"
				};
			}
		}

		public override string Verb
		{
			get
			{
				return "log";
			}
		}


		[CommandLine.ValueList(typeof(List<string>))]
		public IList<string> Files { get; set; }
	}
	class Log : BaseCommand
	{
		public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
		{
			LogVerbOptions localOptions = options as LogVerbOptions;
			Printer.EnableDiagnostics = localOptions.Verbose;
			Area ws = Area.Load(workingDirectory);
			if (ws == null)
				return false;

			if (localOptions.Files.Count == 0)
				BranchLog(ws);
			else
				FileLog(ws, localOptions.Files[0]);

			return true;
		}

		private static void FileLog(Area ws, string v)
		{
			Printer.PrintMessage("File-specific log not yet implemented");
		}

		private static void BranchLog(Area ws)
		{
			List<Objects.Version> history = ws.History;
			foreach (var x in history)
			{
				Printer.PrintMessage("({3}): {0} - {1}, {2}", x.ID, x.Timestamp.ToLocalTime(), x.Author, x.Revision);
				if (!string.IsNullOrEmpty(x.Message))
					Printer.PrintMessage("{0}", x.Message);
				Printer.PrintMessage(" on branch {0}", ws.GetBranch(x.Branch).Name);
				foreach (var y in ws.GetMergeInfo(x.ID))
				{
					var mergeParent = ws.GetVersion(y.SourceVersion);
					Printer.PrintMessage(" <- Merged from {0} on branch {1}", mergeParent.ID, ws.GetBranch(mergeParent.Branch).Name);
				}
			}
		}
	}
}
