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

			List<Objects.Version> history = null;
			if (version == null)
				history = ws.History;
			else
				history = ws.GetHistory(version);

			var enumeration = history.Where(y => HasAlterationForTarget(y, targets));
            if (localOptions.Limit == -1)
                localOptions.Limit = (version == null || targetedBranch) ? 10 : 1;

            if (localOptions.Limit != 0)
                enumeration = enumeration.Take(localOptions.Limit);

            foreach (var x in enumeration.Reverse())
			{
                if (localOptions.Concise)
                {
                    string message = x.Message;
                    if (message == null)
                        message = string.Empty;
                    Printer.PrintMessage("#c#{0}:## ({4}/#b#{5}##) {1} #q#({2} {3})##", x.ShortName, message.Replace('\n', ' '), x.Author, new DateTime(x.Timestamp.Ticks, DateTimeKind.Utc).ToShortDateString(), x.Revision, Workspace.GetBranch(x.Branch).Name);
                }
                else
                {
                    Printer.PrintMessage("\n({0}) #c#{1}## on branch #b#{2}##", x.Revision, x.ID, ws.GetBranch(x.Branch).Name);

                    foreach (var y in ws.GetMergeInfo(x.ID))
                    {
                        var mergeParent = ws.GetVersion(y.SourceVersion);
                        Printer.PrintMessage(" <- Merged from {0} on branch {1}", mergeParent.ID, ws.GetBranch(mergeParent.Branch).Name);
                    }

                    Printer.PrintMessage("#b#Author:## {0} #q# {1} ##", x.Author, x.Timestamp.ToLocalTime());
                    Printer.PrintMessage("#b#Message:##\n{0}", string.IsNullOrWhiteSpace(x.Message) ? "<none>" : Printer.Escape(x.Message));

                    if (localOptions.Alterations)
                    {
                        Printer.PrintMessage("#b#Alterations:##");
                        foreach (var y in ws.GetAlterations(x).OrderBy(z => z.Type))
                        {
							Objects.Record rec = y.NewRecord.HasValue ? ws.GetRecord(y.NewRecord.Value) : y.PriorRecord.HasValue ? ws.GetRecord(y.PriorRecord.Value) : null;
							if (rec != null && IsTarget(rec, targets))
								Printer.PrintMessage("#{2}#({0})## {1}", y.Type.ToString().ToLower(), rec.CanonicalName, GetAlterationFormat(y.Type));
						}
					}
                }
			}

			return true;
		}

		private bool HasAlterationForTarget(Objects.Version v, IList<Versionr.Status.StatusEntry> targets)
		{
			if (targets == null || targets.Count == 0)
				return true;

			foreach (var x in Workspace.GetAlterations(v))
			{
				if (x.NewRecord.HasValue)
				{
					if (IsTarget(Workspace.GetRecord(x.NewRecord.Value), targets))
						return true;
				}
				else if (x.PriorRecord.HasValue)
				{
					if (IsTarget(Workspace.GetRecord(x.PriorRecord.Value), targets))
						return true;
				}
			}
			return false;
		}

		private bool IsTarget(Objects.Record rec, IList<Versionr.Status.StatusEntry> targets)
		{
			if (targets == null || targets.Count == 0)
				return true;

			if (rec == null)
				return false;

			return targets.Where(x => x.CanonicalName == rec.CanonicalName).FirstOrDefault() != null;
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
