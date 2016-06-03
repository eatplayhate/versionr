﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class MergeVerbOptions : MergeSharedOptions
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

        [Option("reintegrate", HelpText = "Deletes the branch once the merge finishes.")]
        public bool Reintegrate { get; set; }

        [Option("ignore-merge-ancestry", HelpText = "Ignores prior merge results when computing changes.")]
        public bool IgnoreMergeAncestry { get; set; }

        [ValueList(typeof(List<string>))]
        public IList<string> Target { get; set; }
    }
    abstract class MergeSharedOptions : VerbOptionBase
    {
        [Option("metadata-only", HelpText = "Record merge without altering workspace contents (ADVANCED FEATURE ONLY).")]
        public bool Metadata { get; set; }
        [Option("force-theirs", MutuallyExclusiveSet = "theirsmine", HelpText = "Use remote files instead of merging results where both inputs have changed the data.")]
        public bool Theirs { get; set; }
        [Option("force-mine", MutuallyExclusiveSet = "theirsmine", HelpText = "Use local files instead of merging results where both inputs have changed the data.")]
        public bool Mine { get; set; }

        [Option('s', "simple", HelpText = "Disable recursive merge engine")]
        public bool Simple { get; set; }
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
            Area.MergeSpecialOptions opt = new Area.MergeSpecialOptions()
            {
                AllowRecursiveMerge = !localOptions.Simple,
                IgnoreMergeParents = localOptions.IgnoreMergeAncestry,
                Reintegrate = localOptions.Reintegrate,
                MetadataOnly = localOptions.Metadata,
                ResolutionStrategy = localOptions.Mine ? Area.MergeSpecialOptions.ResolutionSystem.Mine : (localOptions.Theirs ? Area.MergeSpecialOptions.ResolutionSystem.Theirs : Area.MergeSpecialOptions.ResolutionSystem.Normal)
            };
            foreach (var x in localOptions.Target)
                ws.Merge(x, false, opt);
            return true;
        }
    }
}
