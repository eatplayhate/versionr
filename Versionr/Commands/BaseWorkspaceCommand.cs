using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
    abstract class BaseWorkspaceCommand : BaseCommand
    {
        public bool DirectExtern { get; set; }
        public Area Workspace { get; set; }
        public System.IO.DirectoryInfo ActiveDirectory { get; set; }
        public virtual bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            ActiveDirectory = workingDirectory;
            VerbOptionBase localOptions = options as VerbOptionBase;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Printer.Quiet = localOptions.Quiet;

            using (Workspace = Area.Load(workingDirectory, false, DirectExtern))
            {
                if (Workspace == null)
                {
                    Printer.Write(Printer.MessageType.Error, string.Format("#x#Error:##\n  The current directory #b#`{0}`## is not part of a vault.\n", workingDirectory.FullName));
                    return false;
                }
                return RunInternal(options);
            }
        }

        protected abstract bool RunInternal(object options);
    }
}
