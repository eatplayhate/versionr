using CommandLine;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr;
using Versionr.Commands;

namespace Vsr2Git.Commands
{
	public class ReplicateToGitOptions : Versionr.VerbOptionBase
	{
		public override string[] Description
		{
			get
			{
				return new string[] { "Replicate to a Git repository " };
			}
		}

		public override string Verb
		{
			get
			{
				return "replicate-to-git";
			}
		}

		public override BaseCommand GetCommand()
		{
			return new ReplicateToGit();
		}

		[Option('g', "git-repository", HelpText = "Location of Git repository (containing .git directory)", Required=true)]
		public string GitRepository { get; set; }
	}

	class ReplicationVersion
	{
		public Guid VsrId;

		public ReplicationVersion(Guid vsrId)
		{
			VsrId = vsrId;
		}
	}


	class ReplicateToGit : BaseCommand
	{
		private Area m_VsrArea;
		private Repository m_GitRepository;
		
		private Dictionary<Guid, string> m_VersionrToGitMapping = new Dictionary<Guid, string>();

		private bool HasMapping(Guid vsrVersionId)
		{
			return m_VersionrToGitMapping.ContainsKey(vsrVersionId);
		}

		private string GetGitCommitForVersionrId(Guid vsrId)
		{
			string gitId;
			if (m_VersionrToGitMapping.TryGetValue(vsrId, out gitId))
				return gitId;

			return null;
		}

		private void Replicate(Versionr.Objects.Version vsrVersion)
		{
			if (HasMapping(vsrVersion.ID))
				return;

			string branchName = m_VsrArea.GetBranch(vsrVersion.Branch).Name;
			Printer.PrintMessage("Replicate {0} on {1}: {2}", vsrVersion.ID, branchName, vsrVersion.Message);

			if (!vsrVersion.Parent.HasValue)
			{
				// Initial commit, ensure repository is empty
				// TODO
			}
			else
			{
				// Update HEAD to parent
				m_GitRepository.Refs.UpdateTarget(m_GitRepository.Refs.Head, m_VersionrToGitMapping[vsrVersion.Parent.Value]);
			}

			// TODO merge parents

			var alterations = m_VsrArea.GetAlterations(vsrVersion);
			foreach (var alteration in alterations)
			{
				switch (alteration.Type)
				{
					case Versionr.Objects.AlterationType.Add:
					case Versionr.Objects.AlterationType.Update:
					case Versionr.Objects.AlterationType.Copy:
						var record = m_VsrArea.GetRecord(alteration.NewRecord.Value);
						ReplicateRecord(record);
						break;
					case Versionr.Objects.AlterationType.Move:
						// TODO
						break;
					case Versionr.Objects.AlterationType.Delete:
						// TODO
						break;
				}
				
				//

			}
			
			var author = new Signature(vsrVersion.Author, GetAuthorEmail(vsrVersion.Author), vsrVersion.Timestamp); // TODO map name to email
			var committer = author;
			var gitCommit = m_GitRepository.Commit(vsrVersion.Message, author, committer, new CommitOptions() { PrettifyMessage = false });
			m_VersionrToGitMapping[vsrVersion.ID] = gitCommit.Id.Sha;

			// Link git commit to vsr version that generated it
			m_GitRepository.Notes.Add(gitCommit.Id, "versionr-id: " + vsrVersion.ID.ToString(), author, committer, "commits");
		}

		private string GetAuthorEmail(string author)
		{
			// TODO
			return author + "@ea.com";
		}

		private void ReplicateRecord(Versionr.Objects.Record vsrRecord)
		{
			Printer.PrintMessage("  Update {0}", vsrRecord.CanonicalName);
			using (var stream = m_VsrArea.ObjectStore.GetRecordStream(vsrRecord))
			{
				var blob = m_GitRepository.ObjectDatabase.CreateBlob(stream);
				m_GitRepository.Index.Add(blob, vsrRecord.CanonicalName, Mode.NonExecutableFile);
			}
		}

		private static string FormatNoteVersionrId(Guid vsrId)
		{
			return "versionr-id: " + vsrId;
		}

		private Guid? ParseNoteVersionrId(string note)
		{
			if (note.StartsWith("versionr-id: "))
				return Guid.Parse(note.Substring("versionr-id: ".Length));
			return null;
		}
		
		public bool Run(DirectoryInfo workingDirectory, object options)
		{
			var localOptions = options as ReplicateToGitOptions;
			m_GitRepository = new Repository(localOptions.GitRepository);
			m_VsrArea = Area.Load(workingDirectory, true);

			// Populate replication map from git notes
			try
			{
				foreach (var note in m_GitRepository.Notes["commits"])
				{
					Guid? versionrId = ParseNoteVersionrId(note.Message);
					if (versionrId.HasValue)
						m_VersionrToGitMapping[versionrId.Value] = note.TargetObjectId.Sha;
				}
			}
			catch (NotFoundException)
			{ }

			// Find list of heads to replicate
			var replicationStack = new Stack<ReplicationVersion>();
			foreach (var branch in m_VsrArea.Branches)
			{
				foreach (var head in m_VsrArea.GetBranchHeads(branch))
				{
					replicationStack.Push(new ReplicationVersion(head.Version));
				}
			}

			// Recursively replicate all required versions
			while (replicationStack.Count > 0)
			{
				var replicationVersion = replicationStack.Peek();
				var vsrVersion = m_VsrArea.GetVersion(replicationVersion.VsrId);
				bool hasParents = true;

				if (vsrVersion.Parent.HasValue && !HasMapping(vsrVersion.Parent.Value))
				{
					replicationStack.Push(new ReplicationVersion(vsrVersion.Parent.Value));
					hasParents = false;
				}

				foreach (var merge in m_VsrArea.GetMergeInfo(vsrVersion.ID))
				{
					if (merge.Type == Versionr.Objects.MergeType.Rebase)
						continue;

					if (!HasMapping(merge.SourceVersion))
					{
						replicationStack.Push(new ReplicationVersion(merge.SourceVersion));
						hasParents = false;
					}
				}

				if (hasParents)
				{
					Replicate(vsrVersion);
					replicationStack.Pop();
				}
			}

			// Replicate branch pointers
			var branchNames = new HashSet<string>();
			foreach (var branch in m_VsrArea.Branches)
			{
				var branchHeads = m_VsrArea.GetBranchHeads(branch);
				foreach (var head in branchHeads)
				{
					string gitCommit = GetGitCommitForVersionrId(head.Version);
					if (gitCommit == null)
						continue;

					string gitBranchName;
					if (branchHeads.Count > 1)
						gitBranchName = "vsr-" + branch.Name + "-" + head.Version.ToString();
					else
						gitBranchName = branch.Name;

					Printer.PrintMessage("Updating branch tip {0}", gitBranchName);
					m_GitRepository.Branches.Add(gitBranchName, gitCommit, true);
					branchNames.Add(gitBranchName);
				}
			}
			
			// Remove stale branch pointers from previous multi-head replication
			foreach (
				var branch in (
					from b in m_GitRepository.Branches
					let name = b.FriendlyName
					where name.StartsWith("vsr-") && !branchNames.Contains(name)
					select b
				).ToArray())
			{
				Printer.PrintMessage("Removing old vsr branch tip {0}", branch.FriendlyName);
				m_GitRepository.Branches.Remove(branch);
			}


			return true;
		}
	}
}
