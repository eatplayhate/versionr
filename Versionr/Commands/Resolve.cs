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
                    "Removes conflict markers from a file."
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
