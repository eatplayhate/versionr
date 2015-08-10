using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Commands
{
	class LogVerbOptions : FileBaseCommandVerbOptions
	{
		[Option('l', "alterations", HelpText = "Display a listing of alterations.", MutuallyExclusiveSet = "concise")]
		public bool Alterations { get; set; }
		[Option('t', "limit", DefaultValue = -1, HelpText = "Limit number of versions to show, 10 default (0 for all).")]
		public int Limit { get; set; }
        [Option('c', "concise", HelpText = "Uses a short log formatting style.", MutuallyExclusiveSet = "alterations")]
        public bool Concise { get; set; }
		[Option('b', "branch", HelpText = "Name of the branch to view", MutuallyExclusiveSet = "version")]
		public string Branch { get; set; }
		[Option('v', "version", HelpText = "Specific version to view", MutuallyExclusiveSet = "branch")]
		public string Version { get; set; }

        public override string[] Description
		{
			get
			{
				return new string[]
				{
					"get a log"
				};
			}
		}

		public override string Verb
		{
			get
			{
				return "log";
			}
		}

	}
	class Log : FileBaseCommand
	{
		protected override bool RequiresTargets { get { return false; } }
		protected override bool OnNoTargetsAssumeAll { get { return true; } }

		protected override bool ComputeTargets(FileBaseCommandVerbOptions localOptions)
		{
			return false;
		}

		class ResolvedAlteration
		{
			public Objects.Alteration Alteration { get; private set; }
			public Objects.Record Record { get; private set; }
			public ResolvedAlteration(Objects.Alteration alteration, Area ws)
			{
				Alteration = alteration;
				if (alteration.NewRecord.HasValue)
					Record = ws.GetRecord(Alteration.NewRecord.Value);
				else if (alteration.PriorRecord.HasValue)
					Record = ws.GetRecord(Alteration.PriorRecord.Value);
				else
					throw new Exception("unexpected");
			}
		}

		IEnumerable<KeyValuePair<bool, ResolvedAlteration>> GetAlterations(Objects.Version v)
		{
			var enumeration = Workspace.GetAlterations(v)
				.Select(x => new ResolvedAlteration(x, Workspace))
				.Select(x => new KeyValuePair<string, ResolvedAlteration>(x.Record.CanonicalName, x));

			return Filter(enumeration);
		}

		protected override bool RunInternal(Area ws, Versionr.Status status, IList<Versionr.Status.StatusEntry> targets, FileBaseCommandVerbOptions options)
		{
			LogVerbOptions localOptions = options as LogVerbOptions;
			Printer.EnableDiagnostics = localOptions.Verbose;

            bool targetedBranch = false;
			Objects.Version version = null;
			if (!string.IsNullOrEmpty(localOptions.Branch))
			{
				bool multipleBranches = false;
				var branch = ws.GetBranchByPartialName(localOptions.Branch, out multipleBranches);
				if (branch == null || multipleBranches)
				{
					Printer.PrintError("No unique branch found for {0}", localOptions.Branch);
					return false;
				}
				version = ws.GetBranchHeadVersion(branch);
                targetedBranch = true;
            }
			else if (!string.IsNullOrEmpty(localOptions.Version))
			{
				version = ws.GetPartialVersion(localOptions.Version);
				if (version == null)
				{
					Printer.PrintError("Couldn't find matching version for {0}", localOptions.Version);
					return false;
				}
			}

			var enumeration = (version == null ? ws.History : ws.GetHistory(version))
				.Select(x => new Tuple<Objects.Version, IEnumerable<KeyValuePair<bool, ResolvedAlteration>>>(x, GetAlterations(x)))
				.Where(x => x.Item2.Any());

			if (localOptions.Limit == -1)
                localOptions.Limit = (version == null || targetedBranch) ? 10 : 1;

            if (localOptions.Limit != 0)
                enumeration = enumeration.Take(localOptions.Limit);

            foreach (var x in enumeration.Reverse())
			{
				Objects.Version v = x.Item1;
                if (localOptions.Concise)
                {
                    string message = v.Message;
                    if (message == null)
                        message = string.Empty;
                    Printer.PrintMessage("#c#{0}:## ({4}/#b#{5}##) {1} #q#({2} {3})##", v.ShortName, message.Replace('\n', ' '), v.Author, new DateTime(v.Timestamp.Ticks, DateTimeKind.Utc).ToShortDateString(), v.Revision, Workspace.GetBranch(v.Branch).Name);
                }
                else
                {
                    Printer.PrintMessage("\n({0}) #c#{1}## on branch #b#{2}##", v.Revision, v.ID, ws.GetBranch(v.Branch).Name);

                    foreach (var y in ws.GetMergeInfo(v.ID))
                    {
                        var mergeParent = ws.GetVersion(y.SourceVersion);
                        Printer.PrintMessage(" <- Merged from {0} on branch {1}", mergeParent.ID, ws.GetBranch(mergeParent.Branch).Name);
                    }

                    Printer.PrintMessage("#b#Author:## {0} #q# {1} ##", v.Author, v.Timestamp.ToLocalTime());
                    Printer.PrintMessage("#b#Message:##\n{0}", string.IsNullOrWhiteSpace(v.Message) ? "<none>" : Printer.Escape(v.Message));

                    if (localOptions.Alterations)
                    {
                        Printer.PrintMessage("#b#Alterations:##");
                        foreach (var y in x.Item2.Select(z => z.Value).OrderBy(z => z.Alteration.Type))
                        {
							Printer.PrintMessage("#{2}#({0})## {1}", y.Alteration.Type.ToString().ToLower(), y.Record.CanonicalName, GetAlterationFormat(y.Alteration.Type));
						}
					}
                }
			}

			return true;
		}

		private string GetAlterationFormat(Objects.AlterationType code)
		{
			switch (code)
			{
				case Objects.AlterationType.Add:
				case Objects.AlterationType.Copy:
					return "s";
				case Objects.AlterationType.Update:
					return "w";
				case Objects.AlterationType.Move:
					return "c";
				case Objects.AlterationType.Delete:
					return "e";
				default:
					throw new Exception("Unknown alteration type");
			}
		}
	}
}
