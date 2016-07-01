using System;
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
					"Revert the contents of objects and unincludes them from the next commit.",
                    "",
                    "This uses the same file matching options as #i#record##."
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
        [Option('d', "delete", HelpText = "Delete new files.")]
        public bool Delete { get; set; }

        public override BaseCommand GetCommand()
        {
            return new Revert();
        }

    }
	class Revert : Unrecord
	{
		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
			RevertVerbOptions localOptions = options as RevertVerbOptions;
			ws.Revert(targets, true, localOptions.Interactive, localOptions.Delete, UnrecordFeedback);
			return true;
		}

	}
}
