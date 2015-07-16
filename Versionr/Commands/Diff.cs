using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
	class DiffVerbOptions : FileCommandVerbOptions
	{
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
	}

	class Diff : FileCommand
	{
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileCommandVerbOptions options)
		{
			CommitVerbOptions localOptions = options as CommitVerbOptions;

			foreach (var x in targets)
			{
				string tmp = Utilities.DiffTool.GetTempFilename();
				if (ws.ExportRecord(x.CanonicalName, null, tmp))
				{
					Utilities.DiffTool.Diff(tmp, x.CanonicalName);
					System.IO.File.Delete(tmp);
				}
				Printer.PrintError("Could not restore {0}", x.CanonicalName);
			}
			return true;
		}
	}
}
