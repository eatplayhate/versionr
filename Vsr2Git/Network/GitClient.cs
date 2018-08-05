using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Versionr;
using Versionr.LocalState;
using Versionr.Utilities;

namespace Vsr2Git.Network
{

	class GitClient : Versionr.Network.IRemoteClient
	{
		private Area m_VsrArea;
		private Repository m_GitRepository;
		private string m_URL;

		private Vsr2GitDirectives m_Directives;
		private Dictionary<Guid, string> m_VersionrToGitMapping = new Dictionary<Guid, string>();

        public bool Connected
        {
            get
            {
                return m_GitRepository != null;
            }
        }
		
		public GitClient(Area workspace, Repository repository, string url)
		{
			m_VsrArea = workspace;
			m_GitRepository = repository;
			m_URL = url;

            string error;
            m_Directives = DirectivesUtils.LoadDirectives<Vsr2GitDirectives>(DirectivesUtils.GetVRMetaPath(m_VsrArea), "Vsr2Git", out error);
		}

		public string URL
		{
			get
			{
				return m_URL;
			}
		}

		public Area Workspace
		{
			get
			{
				return m_VsrArea;
			}
		}

        public DirectoryInfo BaseDirectory
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public bool Clone(bool full)
		{
			throw new NotSupportedException();
		}

		public void Close()
		{
			m_GitRepository.Dispose();
		}

		public List<string> GetMissingData(List<Versionr.Objects.Record> missingRecords, List<string> missingData)
		{
			throw new NotSupportedException();
		}

		public Tuple<List<Versionr.Objects.Branch>, List<KeyValuePair<Guid, Guid>>, Dictionary<Guid, Versionr.Objects.Version>> ListBranches()
		{
			throw new NotSupportedException();
		}

		public bool Pull(bool pullRemoteObjects, string branchName, bool allBranches = false, bool acceptDeletes = false)
		{
			throw new NotSupportedException();
		}

		public bool PullStash(string x)
		{
			throw new NotSupportedException();
		}
		
		public bool PushRecords()
		{
			throw new NotSupportedException();
		}
		
		public bool PushStash(Area.StashInfo stash)
		{
			throw new NotSupportedException();
		}

		public bool SyncAllRecords()
		{
			throw new NotSupportedException();
		}

		public bool SyncCurrentRecords()
		{
			throw new NotSupportedException();
		}

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

		private void Replicate(Versionr.Objects.Version vsrVersion)
		{
			string branchName = m_VsrArea.GetBranch(vsrVersion.Branch).Name;
			Printer.PrintMessage("Replicate {0} on {1}: {2}", vsrVersion.ID, branchName, vsrVersion.Message);

			// Choose author
			var author = new Signature(GetAuthorIdentity(vsrVersion.Author), vsrVersion.Timestamp.ToLocalTime());
			var committer = author;
			Printer.PrintDiagnostics("  Author = {0}", author);
			Printer.PrintDiagnostics("  Committer = {0}", committer);

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
				Printer.PrintDiagnostics("  No parents; root commit");
				treeDefinition = new TreeDefinition();
			}
			else
			{
				var gitParentCommit = GetGitCommitForVersionrId(vsrVersion.Parent.Value);
				Printer.PrintDiagnostics("  Parent: vsr {0} = git {1}", vsrVersion.Parent.Value, gitParentCommit);
				gitParents.Add(gitParentCommit);
				treeDefinition = TreeDefinition.From(gitParentCommit);
			}

			// Add merge parents
			var mergeParents = m_VsrArea.GetMergeInfo(vsrVersion.ID);
			foreach (var mergeInfo in mergeParents)
			{
				var gitMergeCommit = GetGitCommitForVersionrId(mergeInfo.SourceVersion);
				Printer.PrintDiagnostics("  Parent: vsr {0} = git {1}", mergeInfo.SourceVersion, gitMergeCommit);
				gitParents.Add(gitMergeCommit);
			}

			// Make alterations to tree
			var alterations = m_VsrArea.GetAlterations(vsrVersion);
			foreach (var alteration in alterations)
			{
				treeDefinition = AlterTree(treeDefinition, alteration);
			}

			// Message can't be null
			if (vsrVersion.Message == null)
				vsrVersion.Message = string.Empty;

			// Create commit record
			var tree = m_GitRepository.ObjectDatabase.CreateTree(treeDefinition);
			var gitCommit = m_GitRepository.ObjectDatabase.CreateCommit(author, committer, vsrVersion.Message, tree, gitParents, false, null);
			Printer.PrintDiagnostics("  Created git commit {0}", gitCommit.Id);

			// Link git commit to vsr version that generated it
			m_VersionrToGitMapping[vsrVersion.ID] = gitCommit.Id.Sha;
			m_GitRepository.Notes.Add(gitCommit.Id, "versionr-id: " + vsrVersion.ID.ToString(), author, committer, "commits");
		}

		private Regex AuthorEmailRegex = new Regex(@"(?<Name>[^<]+)\s+\<(?<Email>[^>]+)\>", RegexOptions.Compiled);

		private Identity GetAuthorIdentity(string author)
		{
			string gitAuthor;
			if (m_Directives?.Authors != null && m_Directives.Authors.TryGetValue(author, out gitAuthor))
			{
				// Git author specified in .vrmeta as either "email@here.com" or "Bob James <email@here.com>"
				var m = AuthorEmailRegex.Match(gitAuthor);
				if (m.Success)
					return new Identity(m.Groups["Name"].Value, m.Groups["Email"].Value);
				else
					return new Identity(author, gitAuthor);
			}
			else if (m_Directives?.DefaultAuthorEmailDomain != null)
			{
				return new Identity(author, author + "@" + m_Directives.DefaultAuthorEmailDomain);
			}
			else
			{
				return new Identity(author, author + "@versionr");
			}
		}

		private IEnumerable<Versionr.Objects.Record> GetTargetRecords(IEnumerable<Versionr.Objects.Alteration> alterations)
		{
			foreach (var alteration in alterations)
			{
				switch (alteration.Type)
				{
					case Versionr.Objects.AlterationType.Add:
					case Versionr.Objects.AlterationType.Update:
					case Versionr.Objects.AlterationType.Copy:
					case Versionr.Objects.AlterationType.Move:
						yield return m_VsrArea.GetRecord(alteration.NewRecord.Value);
						break;
					case Versionr.Objects.AlterationType.Delete:
                    case Versionr.Objects.AlterationType.Discard:
                        break;
					default:
						throw new NotImplementedException();
				}
			}
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
                case Versionr.Objects.AlterationType.Discard:
                    return treeDefinition;
                default:
					throw new NotImplementedException();
			}
		}

		private TreeDefinition AddRecordToTree(TreeDefinition treeDefinition, Versionr.Objects.Record vsrRecord)
		{
			if (vsrRecord.IsSymlink)
			{
				Printer.PrintDiagnostics("  Add symlink {0} -> {1}", vsrRecord.CanonicalName, vsrRecord.Fingerprint);
				using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(vsrRecord.Fingerprint)))
				{
					var blob = m_GitRepository.ObjectDatabase.CreateBlob(stream);
					return treeDefinition.Add(vsrRecord.CanonicalName, blob, Mode.SymbolicLink);
				}
			}
			else if (vsrRecord.IsDirectory)
			{
				// git does not support committing empty directories.  We could synthesize a ".gitkeep" file as suggested
				// on StackOverflow if the directory is empty for this commit.
				Printer.PrintDiagnostics("  Ignore directory record {0}", vsrRecord.CanonicalName);
				return treeDefinition;
			}
			else if (vsrRecord.IsFile)
			{
				var mode = Mode.NonExecutableFile;
				if ((vsrRecord.Attributes & Versionr.Objects.Attributes.Executable) != 0)
					mode = Mode.ExecutableFile;

				using (var stream = m_VsrArea.ObjectStore.GetRecordStream(vsrRecord))
				{
					stream.Position = 0;
					Printer.PrintDiagnostics("  Add record {0} ({1} bytes{2})", vsrRecord.CanonicalName, stream.Length, mode == Mode.ExecutableFile ? " [Executable]" : "");
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
				Printer.PrintDiagnostics("  Ignore directory record {0}", vsrRecord.CanonicalName);
				return treeDefinition;
			}
			else
			{
				Printer.PrintDiagnostics("  Remove record {0}", vsrRecord.CanonicalName);
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
		
		/// <summary>
		/// Traverse versions backwards from branch heads, and accumulate a list of versions that
		/// need to be replicated in order.
		/// </summary>
		/// <returns></returns>
		private List<Versionr.Objects.Version> GetVersionsToReplicate(string branchName = null)
		{
			var result = new List<Versionr.Objects.Version>();
			var resultHash = new HashSet<Guid>(m_VersionrToGitMapping.Keys);

			// Find list of heads to replicate
			var replicationStack = new Stack<Guid>();
			foreach (var branch in m_VsrArea.Branches)
			{
				if (branchName != null && branch.Name != branchName)
					continue;

				foreach (var head in m_VsrArea.GetBranchHeads(branch))
				{
					replicationStack.Push(head.Version);
				}
			}

			// Recursively add all required parent versions
			while (replicationStack.Count > 0)
			{
				var vsrVersion = m_VsrArea.GetVersion(replicationStack.Peek());
				bool hasParents = true;

				if (vsrVersion.Parent.HasValue && !resultHash.Contains(vsrVersion.Parent.Value))
				{
					replicationStack.Push(vsrVersion.Parent.Value);
					hasParents = false;
				}

				var mergeParents = m_VsrArea.GetMergeInfo(vsrVersion.ID);
				foreach (var merge in mergeParents)
				{
					if (merge.Type == Versionr.Objects.MergeType.Rebase)
						continue;

					if (!resultHash.Contains(merge.SourceVersion))
					{
						replicationStack.Push(merge.SourceVersion);
						hasParents = false;
					}
				}

				if (hasParents)
				{
					if (!resultHash.Contains(vsrVersion.ID))
					{
						resultHash.Add(vsrVersion.ID);
						result.Add(vsrVersion);
					}
					replicationStack.Pop();
				}
			}

			return result;
		}

        public bool RequestUpdate
        {
            get
            {
                return false;
            }
        }

		public bool Push(string branchName)
		{
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

			// Determine list and order of versions to replicate
			Printer.PrintMessage("Determining versions to replicate...");
			var versionsToReplicate = GetVersionsToReplicate();
			Printer.PrintMessage("There are {0} versions to replicate", versionsToReplicate.Count);

			// Determine missing records
			var missingRecords = new List<Versionr.Objects.Record>();
			foreach (var vsrVersion in versionsToReplicate)
			{
				var alterations = m_VsrArea.GetAlterations(vsrVersion);
				var targetRecords = GetTargetRecords(alterations);
				missingRecords.AddRange(m_VsrArea.FindMissingRecords(targetRecords));
			}

			// Download missing records
			Printer.PrintMessage("Downloading data for {0} records ({1} total)", missingRecords.Count, Versionr.Utilities.Misc.FormatSizeFriendly(missingRecords.Sum(x => x.Size)));
			m_VsrArea.GetMissingObjects(missingRecords, null);

			// Perform replication
			foreach (var vsrVersion in versionsToReplicate)
			{
				Replicate(vsrVersion);
			}

			// Replicate branch pointers
			var branchNames = new HashSet<string>();
			foreach (var branch in m_VsrArea.Branches)
			{
				if (branchName != null && branch.Name != branchName)
					continue;

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

					var gitBranch = m_GitRepository.Branches.FirstOrDefault(b => b.FriendlyName == gitBranchName);
					if (gitBranch == null || gitBranch.Tip.Id != gitCommit.Id)
					{
						Printer.PrintMessage("Updating branch {0} to {1}", gitBranchName, gitCommit);
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
					}
					else
					{
						Printer.PrintDiagnostics("Branch {0} already at tip {1}", gitBranchName, gitCommit);
					}
					branchNames.Add(gitBranchName);
				}
			}

			// Remove stale branch pointers from previous multi-head replication
			if (branchName == null)
			{
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
			}

			Printer.PrintMessage("Git push complete");

			return true;
		}

        public bool AcquireLock(string path, string branch, bool allBranches, bool full, bool steal)
        {
            throw new NotImplementedException();
        }

        public bool ReleaseLocks(List<RemoteLock> locks)
        {
            throw new NotImplementedException();
        }

        public bool ListLocks(string path, string branch, bool allBranches, bool full)
        {
            throw new NotImplementedException();
        }

        public bool BreakLocks(string path, string branch, bool allBranches, bool full)
        {
            throw new NotImplementedException();
        }

        public List<Area.StashInfo> ListStashes(List<string> names)
        {
            throw new NotImplementedException();
        }
    }
}
