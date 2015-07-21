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
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
			CommitVerbOptions localOptions = options as CommitVerbOptions;

			foreach (var x in targets)
			{
                if (x.VersionControlRecord != null && !x.IsDirectory && x.FilesystemEntry != null && x.Code == StatusCode.Modified)
                {
                    string tmp = Utilities.DiffTool.GetTempFilename();
                    if (ws.ExportRecord(x.CanonicalName, null, tmp))
                    {
                        try
                        {
                            Utilities.DiffTool.Diff(tmp, x.Name + "-base", x.CanonicalName, x.Name);
                        }
                        finally
                        {
                            System.IO.File.Delete(tmp);
                        }
                    }
                }
			}
			return true;
		}
	}
}
