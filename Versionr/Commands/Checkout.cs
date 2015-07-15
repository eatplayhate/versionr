using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Versionr.Commands
{
    class CheckoutVerbOptions : VerbOptionBase
    {
        public override string Usage
        {
            get
            {
                return string.Format("Usage: versionr {0} [options] [target]", Verb);
            }
        }

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
                return "checkout";
            }
        }
		[Option('p', "purge", HelpText = "Remove all unversioned files from the repository")]
		public bool Purge { get; set; }

        [ValueList(typeof(List<string>))]
        public IList<string> Target { get; set; }
    }
    class Checkout : BaseCommand
    {
        public bool Run(System.IO.DirectoryInfo workingDirectory, object options)
        {
            CheckoutVerbOptions localOptions = options as CheckoutVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;
            Area ws = Area.Load(workingDirectory);
            if (ws == null)
                return false;
            ws.Checkout(localOptions.Target[0], localOptions.Purge);
			return true;
        }
    }
}
