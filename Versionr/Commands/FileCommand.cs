using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;
using Versionr.Network;

namespace Versionr.Commands
{
	abstract class FileCommandVerbOptions : FileBaseCommandVerbOptions
    {
		[Option('a', "all", HelpText = "Includes every non-pristine file.", MutuallyExclusiveSet = "regex recursive")]
		public bool All { get; set; }
		[Option('t', "tracked", HelpText = "Matches only files that are tracked by the vault")]
		public bool Tracked { get; set; }
	}
	abstract class FileCommand : FileBaseCommand
    {
        FileCommandVerbOptions FilterOptions { get; set; }
        protected override void GetInitialList(Versionr.Status status, FileBaseCommandVerbOptions options, out List<Versionr.Status.StatusEntry> targets)
        {
            FileCommandVerbOptions localOptions = options as FileCommandVerbOptions;
            FilterOptions = localOptions;
            if (localOptions.All)
                targets = status.Elements;
            else
                targets = status.GetElements(localOptions.Objects, localOptions.Regex, localOptions.Filename, localOptions.Insensitive);
        }

        protected IEnumerable<T> Filter<T>(IEnumerable<KeyValuePair<string, T>> input)
        {
            if (FilterOptions.All)
            {
                foreach (var x in input.Select(x => x.Value))
                    yield return x;
                yield break;
            }

            bool globMatching = false;
            if (!FilterOptions.Regex)
            {
                foreach (var x in FilterOptions.Objects)
                {
                    if (x.Contains("*") || x.Contains("?"))
                    {
                        globMatching = true;
                        break;
                    }
                }
            }

            if (FilterOptions.Regex || globMatching)
            {
                List<System.Text.RegularExpressions.Regex> regexes = new List<System.Text.RegularExpressions.Regex>();
                RegexOptions caseOption = FilterOptions.Insensitive ? RegexOptions.IgnoreCase : RegexOptions.None;
                if (globMatching)
                {
                    foreach (var x in FilterOptions.Objects)
                    {
                        string pattern = "^" + Regex.Escape(x).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                        regexes.Add(new Regex(pattern, RegexOptions.Singleline | caseOption));
                    }
                }
                else
                {
                    foreach (var x in FilterOptions.Objects)
                        regexes.Add(new Regex(x, RegexOptions.Singleline | caseOption));
                }

                foreach (var x in input)
                {
                    foreach (var y in regexes)
                    {
                        int idx = x.Key.LastIndexOf('/');
                        string fn;
                        if (idx == -1)
                            fn = x.Key;
                        else
                            fn = x.Key.Substring(idx + 1);
                        if ((!FilterOptions.Filename && y.IsMatch(x.Key)) || (FilterOptions.Filename && y.IsMatch(fn)))
                        {
                            yield return x.Value;
                            break;
                        }
                    }
                }
            }
            else
            {
                List<string> canonicalPaths = new List<string>();
                foreach (var x in FilterOptions.Objects)
                    canonicalPaths.Add(Workspace.GetLocalPath(System.IO.Path.GetFullPath(x)));

                StringComparison comparisonOptions = FilterOptions.Insensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

                foreach (var x in input)
                {
                    if (FilterOptions.Filename)
                    {
                        int idx = x.Key.LastIndexOf('/', x.Key.Length - 2);
                        string fn;
                        if (idx == -1)
                            fn = x.Key;
                        else
                            fn = x.Key.Substring(idx + 1);
                        foreach (var y in FilterOptions.Objects)
                        {
                            if (string.Equals(fn, y, comparisonOptions) || string.Equals(fn, y + "/", comparisonOptions))
                            {
                                yield return x.Value;
                                break;
                            }
                        }
                    }
                    else
                    {
                        foreach (var y in canonicalPaths)
                        {
                            if (string.Equals(x.Key, y, comparisonOptions) || string.Equals(x.Key, y + "/", comparisonOptions))
                            {
                                yield return x.Value;
                                break;
                            }
                        }
                    }
                }
            }
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
