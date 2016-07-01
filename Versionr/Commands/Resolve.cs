using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
    class ResolveVerbOptions : FileCommandVerbOptions
    {
        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Removes conflict markers from specified objects in the workspace.",
                    "",
                    "Resolve will also delete generated .mine, .theirs and .base files."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "resolve";
            }
        }

        public override BaseCommand GetCommand()
        {
            return new Resolve();
        }
    }
    class Resolve : Unrecord
    {
        protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
        {
            ResolveVerbOptions localOptions = options as ResolveVerbOptions;
            ws.Resolve(targets, UnrecordFeedback);
            return true;
        }

    }
}
