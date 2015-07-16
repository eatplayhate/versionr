using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
	class RevertVerbOptions : FileCommandVerbOptions
	{
		public override string[] Description
		{
			get
			{
				return new string[]
				{
					"Revert the contents of the file and unincludes it from the next commit."
				};
			}
		}

		public override string Verb
		{
			get
			{
				return "revert";
			}
		}

	}
	class Revert : FileCommand
	{
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileCommandVerbOptions options)
		{
			RevertVerbOptions localOptions = options as RevertVerbOptions;
			ws.Revert(targets, true);
			return true;
		}

	}
}
