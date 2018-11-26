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
    public abstract class PatternMatchCommandVerbOptions : VerbOptionBase
    {
        [Option('g', "regex", HelpText = "Use regex pattern matching for arguments.", MutuallyExclusiveSet = "filtertype")]
        public bool Regex { get; set; }
        [Option('n', "filename", HelpText = "Matches filenames regardless of full path.", MutuallyExclusiveSet = "filtertype")]
        public bool Filename { get; set; }
        [Option("recursive", DefaultValue = true, HelpText = "Recursively consider objects in directories.")]
        public bool Recursive { get; set; }
        [Option('u', "insensitive", DefaultValue = true, HelpText = "Use case-insensitive matching for objects")]
        public bool Insensitive { get; set; }
        [Option('w', "windows", HelpText = "Use backslashes in path display")]
        public bool WindowsPaths { get; set; }

        [ValueList(typeof(List<string>))]
        public IList<string> Objects { get; set; }
    }
    public abstract class PatternMatchCommand : BaseWorkspaceCommand
    {
        protected virtual bool ComputeTargets(PatternMatchCommandVerbOptions localOptions)
        {
            return (localOptions.Objects != null && localOptions.Objects.Count > 0) || OnNoTargetsAssumeAll;
        }
        protected abstract bool OnNoTargetsAssumeAll { get; }

        protected PatternMatchCommandVerbOptions FilterOptions { get; set; }
        protected IEnumerable<KeyValuePair<bool, T>> FilterObjects<T>(IEnumerable<KeyValuePair<string, T>> input)
        {
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
                        Match fnMatch = y.Match(fn);
                        Match nMatch = y.Match(x.Key);
                        if (!FilterOptions.Filename)
                        {
                            if (nMatch.Success)
                            {
                                yield return new KeyValuePair<bool, T>(false, x.Value);
                            }
                            break;
                        }
                        else if (fnMatch.Success)
                        {
                            yield return new KeyValuePair<bool, T>(false, x.Value);
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
                                yield return new KeyValuePair<bool, T>(true, x.Value);
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
                                yield return new KeyValuePair<bool, T>(true, x.Value);
                                break;
                            }
                        }
                    }
                }
            }
        }


        protected virtual IEnumerable<KeyValuePair<bool, T>> Filter<T>(IEnumerable<KeyValuePair<string, T>> input)
		{
			if (FilterOptions.Objects.Count == 0 && OnNoTargetsAssumeAll)
			{
                return input.Select(x => new KeyValuePair<bool, T>(false, x.Value));
			}
            return FilterObjects(input);
		}
	}
}
