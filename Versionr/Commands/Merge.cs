﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class MergeVerbOptions : VerbOptionBase
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "abandon all ships"
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "merge";
            }
        }

		[Option('f', "force", HelpText = "Force the merge even if the repository isn't clean" )]
		public bool Force { get; set; }

        [ValueList(typeof(List<string>))]
        public IList<string> Target { get; set; }
    }
    class Merge : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            MergeVerbOptions localOptions = options as MergeVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            foreach (var x in localOptions.Target)
                ws.Merge(x, false, localOptions.Force);
            return true;
        }
    }
}
