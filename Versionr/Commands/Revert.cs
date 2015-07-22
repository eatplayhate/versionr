﻿using System;
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
        [Option('i', "interactive", HelpText = "Provides an interactive prompt for each matched file.")]
        public bool Interactive { get; set; }

    }
	class Revert : FileCommand
	{
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
			RevertVerbOptions localOptions = options as RevertVerbOptions;
			ws.Revert(targets, true, localOptions.Interactive);
			return true;
		}

	}
}
