using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
	class DiffVerbOptions : VerbOptionBase
	{
		public override string Usage
		{
			get
			{
				return string.Format("Usage: versionr {0} files", Verb);
			}
		}

		public override string[] Description
		{
			get
			{
				return new string[]
				{
					"Diff a file"
				};
			}
		}

		public override string Verb
		{
			get
			{
				return "diff";
			}
		}


		[ValueOption(0)]
		public string Target { get; set; }
	}
	class Diff : BaseCommand
	{
		public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
		{
			DiffVerbOptions localOptions = options as DiffVerbOptions;
			Printer.EnableDiagnostics = localOptions.Verbose;
			Area ws = Area.Load(workingDirectory);
			if (ws == null)
				return false;

			string tmp = Utilities.DiffTool.GetTempFilename();
			if (ws.ExportRecord(localOptions.Target, null, tmp))
			{
				Utilities.DiffTool.Diff(tmp, localOptions.Target);
				System.IO.File.Delete(tmp);
				return true;
			}
			Printer.PrintError("Could not restore {0}", localOptions.Target);
			return false;
		}
	}
}
