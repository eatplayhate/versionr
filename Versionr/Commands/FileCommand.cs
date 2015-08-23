using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
	abstract class FileCommandVerbOptions : FileBaseCommandVerbOptions
    {
		[Option('a', "all", HelpText = "Includes every non-pristine file.", MutuallyExclusiveSet = "filtertype")]
		public bool All { get; set; }
		[Option('t', "tracked", HelpText = "Matches only files that are tracked by the vault")]
		public bool Tracked { get; set; }

        public new static string[] SharedDescription
        {
            get
            {
                return FileBaseCommandVerbOptions.SharedDescription.Concat(new string[]
                {

                }).ToArray();
            }
        }
	}
	abstract class FileCommand : FileBaseCommand
    {
		protected override void GetInitialList(Versionr.Status status, FileBaseCommandVerbOptions options, out List<Versionr.Status.StatusEntry> targets)
        {
            FileCommandVerbOptions localOptions = options as FileCommandVerbOptions;
			if (localOptions.All)
				targets = status.Elements;
			else
				base.GetInitialList(status, options, out targets);
        }

		protected override IEnumerable<KeyValuePair<bool, T>> Filter<T>(IEnumerable<KeyValuePair<string, T>> input)
		{
			FileCommandVerbOptions options = FilterOptions as FileCommandVerbOptions;
            if (options.All)
			{
				foreach (var x in input.Select(x => x.Value))
					yield return new KeyValuePair<bool, T>(false, x);
				yield break;
			}
			foreach (var x in base.Filter(input))
				yield return x;
		}

		protected override bool ComputeTargets(FileBaseCommandVerbOptions options)
        {
            if (!base.ComputeTargets(options))
            {
                FileCommandVerbOptions localOptions = options as FileCommandVerbOptions;
                return localOptions.All || localOptions.Tracked;
            }
            return true;
        }

        protected override void ApplyFilters(Versionr.Status status, FileBaseCommandVerbOptions options, List<Versionr.Status.StatusEntry> targets)
        {
            FileCommandVerbOptions localOptions = options as FileCommandVerbOptions;

            if (localOptions.Tracked)
                targets = targets.Where(x => x.VersionControlRecord != null).ToList();
        }

		protected override bool RequiresTargets { get { return true; } }

	}
}
