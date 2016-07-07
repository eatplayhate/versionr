using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
    public interface BaseCommand
    {
        bool Run(System.IO.DirectoryInfo workingDirectory, object options);
    }
}
