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
    public abstract class FileBaseCommandVerbOptions : VerbOptionBase
    {
        [Option('g', "regex", HelpText = "Use regex pattern matching for arguments.", MutuallyExclusiveSet = "filtertype")]
        public bool Regex { get; set; }
        [Option('n', "filename", HelpText = "Matches filenames regardless of full path.", MutuallyExclusiveSet = "filtertype")]
        public bool Filename { get; set; }
        [Option("recursive", DefaultValue = true, HelpText = "Recursively consider objects in directories.")]
        public bool Recursive { get; set; }
        [Option('u', "insensitive", DefaultValue = true, HelpText = "Use case-insensitive matching for objects")]
        public bool Insensitive { get; set; }
        [Option('t', "tracked", HelpText = "Matches only files that are tracked by the vault")]
        public bool Tracked { get; set; }
        [Option('i', "ignored", HelpText = "Show ignored files")]
        public bool Ignored { get; set; }
        [Option('w', "windows", HelpText = "Use backslashes in path display")]
        public bool WindowsPaths { get; set; }
        [Option('e', "skipempty", HelpText = "Remove all empty directories.")]
        public bool SkipEmpty { get; set; }

        [Option('r', "recorded", HelpText = "Matches only files that are recorded")]
        public bool Recorded { get; set; }

        public override string Usage
        {
            get
            {
                return string.Format("#b#versionr #i#{0}#q# [options] ##[file1 #q#file2 ... fileN##]", Verb);
            }
        }

        public static string[] SharedDescription
        {
            get
            {
                return new string[]
                {
                    "",
                    "#b#Matching Objects in Versionr#q#",
                    "",
                    "Versionr uses a mixture of path, wildcard and regex based matching systems to provide arguments to commands that require files.",
                    "",
                    "By default, Versionr uses partial matching of names as its primary mechanism. "
                };
            }
        }

        [ValueList(typeof(List<string>))]
        public IList<string> Objects { get; set; }
    }
    public abstract class FileBaseCommand : BaseWorkspaceCommand
    {
        bool RemovedOnlyTarget;
        protected override bool RunInternal(object options)
        {
            FileBaseCommandVerbOptions localOptions = options as FileBaseCommandVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;

            if (localOptions.Objects != null)
            {
                if (SupportsTags)
                {
                    TagList = localOptions.Objects.Where(x => x.StartsWith("#")).Select(x => x.Substring(1)).ToList();
                    localOptions.Objects = localOptions.Objects.Where(x => !x.StartsWith("#")).ToList();
                }
            }

			FilterOptions = localOptions;
			Start();

            if (FilterOptions.Objects != null && FilterOptions.Objects.Count == 1)
            {
                try
                {
                    System.IO.DirectoryInfo info = new System.IO.DirectoryInfo(FilterOptions.Objects[0]);
                    if (info.Exists)
					{
						ActiveDirectory = info;
						FilterOptions.Objects.RemoveAt(0);
						RemovedOnlyTarget = true;
					}
				}
                catch
                {

                }
            }

            Versionr.Status status = null;
            List<Versionr.Status.StatusEntry> targets = null;
            if (ComputeTargets(localOptions))
            {
                status = Workspace.GetStatus(ActiveDirectory);

                GetInitialList(status, localOptions, out targets);

                if (localOptions.Recursive)
                    status.AddRecursiveElements(targets);

                ApplyFilters(status, localOptions, ref targets);
            }

            if ((targets != null && targets.Count > 0) || !RequiresTargets)
                return RunInternal(Workspace, status, targets, localOptions);

            Printer.PrintWarning("No files selected for {0}", localOptions.Verb);
            return false;
        }

		protected virtual void Start()
        {
        }

        protected virtual bool ComputeTargets(FileBaseCommandVerbOptions localOptions)
        {
            return (localOptions.Objects != null && localOptions.Objects.Count > 0) || OnNoTargetsAssumeAll || localOptions.Recorded || localOptions.Tracked;
        }

        protected virtual void ApplyFilters(Versionr.Status status, FileBaseCommandVerbOptions localOptions, ref List<Versionr.Status.StatusEntry> targets)
        {
            IEnumerable<Versionr.Status.StatusEntry> entries = targets;
            if (!localOptions.Ignored)
                entries = entries.Where(x => x.Staged == true || !(x.Code == StatusCode.Ignored && x.VersionControlRecord == null));
            if (localOptions.Tracked)
                entries = entries.Where(x => x.Staged == true || (x.VersionControlRecord != null && x.Code != StatusCode.Copied && x.Code != StatusCode.Renamed));
            if (localOptions.Recorded)
                entries = entries.Where(x => x.Staged == true);
            if (localOptions.SkipEmpty)
            {
                Dictionary<Versionr.Status.StatusEntry, bool> allow = new Dictionary<Versionr.Status.StatusEntry, bool>();
                Dictionary<string, Versionr.Status.StatusEntry> mapper = new Dictionary<string, Versionr.Status.StatusEntry>();
                entries = entries.ToList();
                
                foreach (var x in entries)
                {
                    if (x.IsDirectory)
                    {
                        allow[x] = false;
                        mapper[x.CanonicalName] = x;
                    }
                }
                foreach (var x in entries)
                {
                    if (x.IsDirectory)
                        continue;
                    string s = x.CanonicalName;
                    int index = s.LastIndexOf('/');
                    if (index != -1)
                    {
                        string dir = s.Substring(0, index + 1);
                        // Sometimes files in ignored directories show up here, so check to make sure the directory exists first
                        if (mapper.TryGetValue(dir, out var value))
                            allow[value] = true;
                    }
                }
                entries = entries.Where(x => !x.IsDirectory || allow[x]).ToList();
            }
            if (!ReferenceEquals(targets, entries))
                targets = entries.ToList();
        }

        protected virtual bool OnNoTargetsAssumeAll
        {
            get
            {
                return RemovedOnlyTarget;
            }
        }

        protected virtual void GetInitialList(Versionr.Status status, FileBaseCommandVerbOptions localOptions, out List<Versionr.Status.StatusEntry> targets)
        {
            if (localOptions.Objects != null && localOptions.Objects.Count > 0)
                targets = status.GetElements(localOptions.Objects, localOptions.Regex, localOptions.Filename, localOptions.Insensitive);
            else
                targets = status.Elements;
        }

        protected abstract bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options);

        protected virtual bool RequiresTargets { get { return !OnNoTargetsAssumeAll; } }
        protected virtual bool SupportsTags { get { return false; } }
        protected List<string> TagList { get; set; } = new List<string>();
        protected FileBaseCommandVerbOptions FilterOptions { get; set; }
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
