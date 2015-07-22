﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
	class UnrecordVerbOptions : FileCommandVerbOptions
	{
		public override string[] Description
		{
			get
			{
				return new string[]
				{
					"Removes the file from inclusion in the next commit.",
				};
			}
		}

		public override string Verb
		{
			get
			{
				return "unrecord";
			}
		}

        [Option('i', "interactive", HelpText = "Provides an interactive prompt for each matched file.")]
        public bool Interactive { get; set; }
    }
	class Unrecord : FileCommand
	{
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
			UnrecordVerbOptions localOptions = options as UnrecordVerbOptions;
			ws.Revert(targets, false, localOptions.Interactive);
			return true;
		}

	}
}
