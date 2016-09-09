using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;

namespace Versionr.Commands
{
    class DiffVersionsVerbOptions : VerbOptionBase
    {
        public override BaseCommand GetCommand()
        {
            return new DiffVersions();
        }

        public override string[] Description
        {
            get
            {
                return new string[]
                {
                    "Displays record differences between two (possibly unrelated) versions.  If #b#base-branch-or-version## is unspecified, the current version is used as a base."
                };
            }
        }

        public override string Verb
        {
            get
            {
                return "diff-version";
            }
        }

        public override string Usage
        {
            get
            {
                return $"#b#versionr #i#{Verb}#q# [options] ##[base-branch-or-version] #q#compare-branch-or-version##";
            }
        }

        [ValueList(typeof(List<string>))]
        public IList<string> Versions { get; set; }
    }

    class DiffVersions : BaseWorkspaceCommand
    {
        protected override bool RunInternal(object options)
        {
            DiffVersionsVerbOptions localOptions = options as DiffVersionsVerbOptions;
            Printer.EnableDiagnostics = localOptions.Verbose;

            Objects.Version baseVersion = null;
            Objects.Version compareVersion = null;

            if (localOptions.Versions.Count == 0)
            {
                Printer.PrintError("Must specify a version or branch to compare to");
                return false;
            }
            else if (localOptions.Versions.Count == 1)
            {
                if (Workspace.HasStagedModifications)
                {
                    Printer.PrintError("Cannot compare current workspace while there are staged modifications");
                    return false;
                }

                baseVersion = Workspace.Version;
                compareVersion = GetVersionByName(localOptions.Versions[0]);
            }
            else if (localOptions.Versions.Count == 2)
            {
                baseVersion = GetVersionByName(localOptions.Versions[0]);
                compareVersion = GetVersionByName(localOptions.Versions[1]);
            }
            else
            {
                Printer.PrintError("Too many versions specified");
                return false;
            }

            if (baseVersion == null || compareVersion == null)
            {
                return false;
            }

            CompareVersions(baseVersion, compareVersion, localOptions);

            return true;
        }

        private class RecordPathComparer : IEqualityComparer<Objects.Record>, IComparer<Objects.Record>
        {
            public static RecordPathComparer Instance = new RecordPathComparer();

            public int Compare(Objects.Record x, Objects.Record y)
            {
                return string.Compare(x.CanonicalName, y.CanonicalName);
            }

            public bool Equals(Objects.Record x, Objects.Record y)
            {
                return x.CanonicalName.Equals(y.CanonicalName);
            }

            public int GetHashCode(Objects.Record obj)
            {
                return obj.CanonicalName.GetHashCode();
            }
        }

        public void CompareVersions(Objects.Version baseVersion, Objects.Version compareVersion, DiffVersionsVerbOptions localOptions)
        {
            var baseRecords = Workspace.GetRecords(baseVersion);
            var compareRecords = Workspace.GetRecords(compareVersion);

            // List additions
            var added = new HashSet<Objects.Record>(compareRecords, RecordPathComparer.Instance);
            added.ExceptWith(baseRecords);
            Report("Add", added.ToList());
            
            // List deletions
            var deleted = new HashSet<Objects.Record>(baseRecords, RecordPathComparer.Instance);
            deleted.ExceptWith(compareRecords);
            Report("Delete", deleted.ToList());

            // Check for modifications
            var compareLookup = new Dictionary<string, Objects.Record>();
            foreach (var record in compareRecords)
                compareLookup[record.CanonicalName] = record;
            var modified = new List<Objects.Record>();
            foreach (var baseRecord in baseRecords)
            {
                Objects.Record compareRecord;
                if (!compareLookup.TryGetValue(baseRecord.CanonicalName, out compareRecord))
                    continue;

                if (compareRecord.DataIdentifier != baseRecord.DataIdentifier || compareRecord.Attributes != baseRecord.Attributes)
                    modified.Add(compareRecord);
            }
            Report("Modify", modified);
        }

        private void Report(string type, List<Objects.Record> records)
        {
            records.Sort(RecordPathComparer.Instance);
            foreach (var record in records)
                Printer.PrintMessage($"#b#{type}## {record.CanonicalName}");
        }

        private Objects.Version GetVersionByName(string name)
        {
            var branch = Workspace.GetBranchByName(name).FirstOrDefault();
            if (branch != null)
            {
                var heads = Workspace.GetBranchHeads(branch);
                if (heads.Count == 0)
                {
                    Printer.PrintError($"Branch {branch.Name} has no heads");
                    return null;
                }
                else if (heads.Count == 1)
                {
                    return Workspace.GetVersion(heads.First().Version);
                }
                else
                {
                    Printer.PrintError($"Branch {branch.Name} has multiple heads");
                    return null;
                }
            }

            var version = Workspace.GetPartialVersion(name);
            if (version == null)
            {
                Printer.PrintError($"'{name}' is not a known branch or version");
                return null;
            }

            return version;
        }
    }
}
