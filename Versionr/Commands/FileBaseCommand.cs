using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
    abstract class FileBaseCommandVerbOptions : VerbOptionBase
    {
        [Option('g', "regex", HelpText = "Use regex pattern matching for arguments.", MutuallyExclusiveSet = "all")]
        public bool Regex { get; set; }
        [Option('n', "filename", HelpText = "Matches filenames regardless of full path.", MutuallyExclusiveSet = "all")]
        public bool Filename { get; set; }
        [Option('r', "recursive", DefaultValue = true, HelpText = "Recursively consider objects in directories.")]
        public bool Recursive { get; set; }
        [Option('u', "insensitive", DefaultValue = true, HelpText = "Use case-insensitive matching for objects")]
        public bool Insensitive { get; set; }
        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}#q# [options] ##file1 #q#[file2 ... fileN]", Verb);
            }
        }

        [ValueList(typeof(List<string>))]
        public IList<string> Objects { get; set; }
    }
    abstract class FileBaseCommand : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            FileBaseCommandVerbOptions localOptions = options as FileBaseCommandVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;

            Start();

            Versionr.Status status = null;
            List<Versionr.Status.StatusEntry> targets = null;
            if (ComputeTargets(localOptions))
            {
                status = Workspace.GetStatus(ActiveDirectory);

                GetInitialList(status, localOptions, out targets);

                if (localOptions.Recursive)
                    status.AddRecursiveElements(targets);

                ApplyFilters(status, localOptions, targets);
            }

            if ((targets != null && targets.Count > 0) || !RequiresTargets)
                return RunInternal(Workspace, status, targets, localOptions);

            Printer.PrintWarning("No files selected for {0}", localOptions.Verb);
            return false;
        }

        protected virtual void Start()
        {
        }

        protected virtual bool ComputeTargets(FileBaseCommandVerbOptions localOptions)
        {
            return (localOptions.Objects != null && localOptions.Objects.Count > 0) || OnNoTargetsAssumeAll;
        }

        protected virtual void ApplyFilters(Versionr.Status status, FileBaseCommandVerbOptions localOptions, List<Versionr.Status.StatusEntry> targets)
        {
        }

        protected virtual bool OnNoTargetsAssumeAll
        {
            get
            {
                return false;
            }
        }

        protected virtual void GetInitialList(Versionr.Status status, FileBaseCommandVerbOptions localOptions, out List<Versionr.Status.StatusEntry> targets)
        {
            if (localOptions.Objects != null && localOptions.Objects.Count > 0)
                targets = status.GetElements(localOptions.Objects, localOptions.Regex, localOptions.Filename, localOptions.Insensitive);
            else
                targets = status.Elements;
        }

        protected abstract bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options);

        protected virtual bool RequiresTargets { get { return OnNoTargetsAssumeAll; } }

    }
}
