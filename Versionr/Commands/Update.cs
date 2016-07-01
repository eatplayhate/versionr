using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class UpdateVerbOptions : MergeSharedOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Update will take your current revision to the head of the branch (for example, if you have just pulled from the server), and/or it will merge in up to one other branch head.",
                    "",
                    "This allows you to easily reconcile work which has been pulled from a server with your local commits. The merge operation is equivalent to manually merging the other head revision, and requires committing once the merge has been run.",
                    "",
                    "Update is safe to use on non-pristine workspaces."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "update";
            }
        }

        public override BaseCommand GetCommand()
        {
            return new Update();
        }
    }
    class Update : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            UpdateVerbOptions localOptions = options as UpdateVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            Area.MergeSpecialOptions opt = new Area.MergeSpecialOptions()
            {
                AllowRecursiveMerge = !localOptions.Simple,
                IgnoreMergeParents = false,
                Reintegrate = false,
                MetadataOnly = localOptions.Metadata,
                ResolutionStrategy = localOptions.Mine ? Area.MergeSpecialOptions.ResolutionSystem.Mine : (localOptions.Theirs ? Area.MergeSpecialOptions.ResolutionSystem.Theirs : Area.MergeSpecialOptions.ResolutionSystem.Normal)
            };
            ws.Update(opt);
            return true;
        }
    }
}
