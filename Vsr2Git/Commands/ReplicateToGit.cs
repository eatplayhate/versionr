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

		[Option('g', "git-repository", HelpText = "Path to Git repository (either bare or non-bare)", Required=true)]
		public string GitRepository { get; set; }

		[Option("init-bare", HelpText = "Initialize a new, bare repository if the repository doesn't exist", MutuallyExclusiveSet = "init")]
		public bool InitBare { get; set; }

		[Option("init", HelpText = "Initialize a new, non-bare repository if the repository doesn't exist", MutuallyExclusiveSet="init")]
		public bool InitNonBare { get; set; }
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
		private ReplicateToGitOptions m_Options;
		private Area m_VsrArea;
		private Repository m_GitRepository;
		
		private Dictionary<Guid, string> m_VersionrToGitMapping = new Dictionary<Guid, string>();

		private bool HasMapping(Guid vsrVersionId)
		{
			return m_VersionrToGitMapping.ContainsKey(vsrVersionId);
		}

		private Commit GetGitCommitForVersionrId(Guid vsrId)
		{
			string gitId;
			if (m_VersionrToGitMapping.TryGetValue(vsrId, out gitId))
				return m_GitRepository.Lookup<Commit>(gitId);

			return null;
		}

		private void Replicate(Versionr.Objects.Version vsrVersion, IEnumerable<Versionr.Objects.MergeInfo> mergeParents)
		{
			if (HasMapping(vsrVersion.ID))
				return;

			string branchName = m_VsrArea.GetBranch(vsrVersion.Branch).Name;
			Printer.PrintMessage("Replicate {0} on {1}: {2}", vsrVersion.ID, branchName, vsrVersion.Message);
			
			// Choose author
			var author = new Signature(vsrVersion.Author, GetAuthorEmail(vsrVersion.Author), vsrVersion.Timestamp.ToLocalTime()); // TODO map name to email
			var committer = author;

			// Choose parent commit
			List<Commit> gitParents = new List<Commit>();
			TreeDefinition treeDefinition;
			if (!vsrVersion.Parent.HasValue)
			{
				// Initial commit, ensure repository is empty
				if (m_GitRepository.Head != null && m_GitRepository.Head.Tip != null)
				{
					throw new InvalidOperationException("Cannot replicate root version into non-empty repository");
				}
				treeDefinition = new TreeDefinition();
			}
			else
			{
				var gitParentCommit = GetGitCommitForVersionrId(vsrVersion.Parent.Value);
				gitParents.Add(gitParentCommit);
				treeDefinition = TreeDefinition.From(gitParentCommit);
			}

			// Add merge parents
			foreach (var mergeInfo in mergeParents)
			{
				gitParents.Add(GetGitCommitForVersionrId(mergeInfo.SourceVersion));
			}

			// Make alterations to tree
			var alterations = m_VsrArea.GetAlterations(vsrVersion);
			foreach (var alteration in alterations)
			{
				treeDefinition = AlterTree(treeDefinition, alteration);
			}

			// Create commit record
			var tree = m_GitRepository.ObjectDatabase.CreateTree(treeDefinition);
			var gitCommit = m_GitRepository.ObjectDatabase.CreateCommit(author, committer, vsrVersion.Message, tree, gitParents, false, null);
			
			// Link git commit to vsr version that generated it
			m_VersionrToGitMapping[vsrVersion.ID] = gitCommit.Id.Sha;
			m_GitRepository.Notes.Add(gitCommit.Id, "versionr-id: " + vsrVersion.ID.ToString(), author, committer, "commits");
		}

		private string GetAuthorEmail(string author)
		{
			// TODO
			return author + "@ea.com";
		}

		private TreeDefinition AlterTree(TreeDefinition treeDefinition, Versionr.Objects.Alteration alteration)
		{
			switch (alteration.Type)
			{
				case Versionr.Objects.AlterationType.Add:
				case Versionr.Objects.AlterationType.Update:
				case Versionr.Objects.AlterationType.Copy:
					return AddRecordToTree(treeDefinition, m_VsrArea.GetRecord(alteration.NewRecord.Value));
				case Versionr.Objects.AlterationType.Move:
					treeDefinition = AddRecordToTree(treeDefinition, m_VsrArea.GetRecord(alteration.NewRecord.Value));
					return RemoveRecordFromTree(treeDefinition, m_VsrArea.GetRecord(alteration.PriorRecord.Value));
				case Versionr.Objects.AlterationType.Delete:
					return RemoveRecordFromTree(treeDefinition, m_VsrArea.GetRecord(alteration.PriorRecord.Value));
				default:
					throw new NotImplementedException();
			}
		}

		private TreeDefinition AddRecordToTree(TreeDefinition treeDefinition, Versionr.Objects.Record vsrRecord)
		{
			if (vsrRecord.IsSymlink)
			{
				// TODO 
				throw new NotSupportedException();
			}
			else if (vsrRecord.IsDirectory)
			{
				// git does not support committing empty directories.  We could synthesize a ".gitkeep" file as suggested
				// on StackOverflow if the directory is empty for this commit.
				return treeDefinition;
			}
			else if (vsrRecord.IsFile)
			{
				var mode = Mode.NonExecutableFile;
				if ((vsrRecord.Attributes & Versionr.Objects.Attributes.Executable) != 0)
					mode = Mode.ExecutableFile;

				using (var stream = m_VsrArea.ObjectStore.GetRecordStream(vsrRecord))
				{
					var blob = m_GitRepository.ObjectDatabase.CreateBlob(stream);
					return treeDefinition.Add(vsrRecord.CanonicalName, blob, mode);
				}
			}
			else
			{
				throw new NotImplementedException();
			}
		}

		private TreeDefinition RemoveRecordFromTree(TreeDefinition treeDefinition, Versionr.Objects.Record vsrRecord)
		{
			if (vsrRecord.IsDirectory)
			{
				// git does not support removing directories
				return treeDefinition;
			}
			else
			{
				return treeDefinition.Remove(vsrRecord.CanonicalName);
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
		
		private bool LoadGitRepository()
		{
			string path = m_Options.GitRepository;

			try
			{
				// Try to load existing repository
				m_GitRepository = new Repository(path);
			}
			catch (RepositoryNotFoundException)
			{
				if (!m_Options.InitBare && !m_Options.InitNonBare)
				{
					Printer.PrintMessage("No repository found at #b#{0}##, check your path or specify #b#--init## or #b#--init-bare##", path);
					return false;
				}

				if (m_Options.InitBare && System.IO.Directory.Exists(path))
				{
					Printer.PrintMessage("Cannot create bare repository at #b#{0}##, directory already exists.  Check your path or specify #b#--init## instead.", path);
					return false;
				}

				if (m_Options.InitBare)
				{
					Printer.PrintMessage("Initializing bare repository at #b#{0}##", path);
					Repository.Init(path, true);
				}
				else
				{
					Printer.PrintMessage("Initializing non-bare repository at #b#{0}##", path);
					Repository.Init(path);
				}

				m_GitRepository = new Repository(path);
			}

			return true;
		}

		private void LoadVsrRepository(DirectoryInfo workingDirectory)
		{
			m_VsrArea = Area.Load(workingDirectory, true);
		}

		public bool Run(DirectoryInfo workingDirectory, object options)
		{
			m_Options = (ReplicateToGitOptions)options;

			if (!LoadGitRepository())
				return false;

			LoadVsrRepository(workingDirectory);
			
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

				var mergeParents = m_VsrArea.GetMergeInfo(vsrVersion.ID);
				foreach (var merge in mergeParents)
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
					Replicate(vsrVersion, mergeParents);
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
					var gitCommit = GetGitCommitForVersionrId(head.Version);
					if (gitCommit == null)
						continue;

					string gitBranchName;
					if (branchHeads.Count > 1)
						gitBranchName = "vsr-" + branch.Name + "-" + head.Version.ToString();
					else
						gitBranchName = branch.Name;

					Printer.PrintMessage("Updating branch {0}", gitBranchName);
					if (m_GitRepository.Head != null && m_GitRepository.Head.FriendlyName == gitBranchName)
					{
						// Currently tracking this branch, we have to temporarily move HEAD somewhere else while
						// we update the branch pointer
						string canonicalName = m_GitRepository.Head.CanonicalName;
						m_GitRepository.Refs.UpdateTarget("HEAD", gitCommit.Id.Sha);
						m_GitRepository.Branches.Add(gitBranchName, gitCommit, true);
						m_GitRepository.Refs.UpdateTarget("HEAD", canonicalName);
					}
					else
					{
						m_GitRepository.Branches.Add(gitBranchName, gitCommit, true);
					}
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
