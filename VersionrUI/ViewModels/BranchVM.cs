using MahApps.Metro.Controls.Dialogs;
using System.Collections.Generic;
using System.Linq;
using Versionr.Objects;
using VersionrUI.Commands;
using VersionrUI.Dialogs;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class BranchVM : NotifyPropertyChangedBase
    {
        public DelegateCommand PullCommand { get; private set; }
        public DelegateCommand PushCommand { get; private set; }
        public DelegateCommand CheckoutCommand { get; private set; }
        public DelegateCommand LogCommand { get; private set; }
        public DelegateCommand CreatePullRequestCommand { get; private set; }

        private readonly AreaVM m_AreaVM;
        private readonly Branch m_Branch;
        private List<VersionVM> m_History = null;
        private string m_SearchText;
        private VersionVM m_SelectedVersion;
        private int m_RevisionLimit = 50;
        private bool m_ForceRefresh = false;

        public string SearchText
        {
            get { return m_SearchText; }
            set
            {
                m_SearchText = value;
                NotifyPropertyChanged(nameof(SearchText));
                NotifyPropertyChanged(nameof(History));
            }
        }

        public BranchVM(AreaVM areaVM, Branch branch)
        {
            m_AreaVM = areaVM;
            m_Branch = branch;

            PullCommand = new DelegateCommand(() => Load(Pull));
            PushCommand = new DelegateCommand(() => Load(Push));
            CheckoutCommand = new DelegateCommand(() => Load(Checkout), () => !IsCurrent);
            LogCommand = new DelegateCommand(Log);
            CreatePullRequestCommand = new DelegateCommand(CreatePullRequest);
        }

        public Branch Branch
        {
            get { return m_Branch; }
        }

        public string Name
        {
            get { return m_Branch.Name; }
        }

        public bool IsDeleted
        {
            get { return m_Branch.Terminus.HasValue; }
        }

        public bool IsCurrent
        {
            get { return m_AreaVM.Area.CurrentBranch.ID == m_Branch.ID; }
        }

        public List<VersionVM> History
        {
            get
            {
                if (m_History == null || m_ForceRefresh)
                    Load(Refresh);
                if (!string.IsNullOrEmpty(SearchText))
                    return FilterHistory(m_History, SearchText);
                return m_History;
            }
        }

        public VersionVM SelectedVersion
        {
            get { return m_SelectedVersion; }
            set
            {
                m_SelectedVersion = value;
                NotifyPropertyChanged(nameof(SelectedVersion));
            }
        }

        public int RevisionLimit
        {
            get { return m_RevisionLimit; }
            set
            {
                if (m_RevisionLimit != value)
                {
                    m_RevisionLimit = value;
                    m_ForceRefresh = true;
                    NotifyPropertyChanged(nameof(RevisionLimit));
                    Load(Refresh);
                }
            }
        }

        private List<VersionVM> FilterHistory(List<VersionVM> history, string searchtext)
        {
            searchtext = searchtext.ToLower();
            return history.Where(version => (!string.IsNullOrEmpty(version.Message) && version.Message.ToLower().Contains(searchtext)) ||
                                            version.ID.ToString().ToLower().Contains(searchtext) ||
                                            version.Author.ToLower().Contains(searchtext) ||
                                            version.Timestamp.ToString().ToLower().Contains(searchtext) ||
                                            version.Alterations.Any(x => x.Name.ToLower().Contains(searchtext)))
                .ToList();
        }

        private static readonly object refreshLock = new object();
        private void Refresh()
        {
            lock (refreshLock)
            {
                m_ForceRefresh = false;

                var headVersion = m_AreaVM.Area.GetBranchHeadVersion(m_Branch);
                int? limit = (RevisionLimit != -1) ? RevisionLimit : (int?)null;
                List<Version> versions = m_AreaVM.Area.GetLogicalHistory(headVersion, false, false, false, limit);
                m_History = new List<VersionVM>();

                foreach (Version version in versions)
                    m_History.Add(new VersionVM(version, m_AreaVM.Area));
                
                NotifyPropertyChanged(nameof(History));
            }
        }

        private void Pull()
        {
            OperationStatusDialog.Start("Pull");
            m_AreaVM.ExecuteClientCommand((c) => c.Pull(true, Name), "pull");
            if(IsCurrent)
                m_AreaVM.Area.Update(new Versionr.Area.MergeSpecialOptions());
            OperationStatusDialog.Finish();
        }

        private void Push()
        {
            OperationStatusDialog.Start("Push");
            m_AreaVM.ExecuteClientCommand((c) => c.Push(Name), "push", true);
            OperationStatusDialog.Finish();
        }

        private void Checkout()
        {
            if (m_AreaVM.Area.Status.HasModifications(false))
            {
                MessageDialogResult result = MessageDialogResult.FirstAuxiliary;
                MainWindow.Instance.Dispatcher.Invoke(async () =>
                {
                    MetroDialogSettings settings = new MetroDialogSettings()
                    {
                        AffirmativeButtonText = "Checkout (keep unversioned files)",
                        NegativeButtonText = "Checkout (purge unversioned files)",
                        FirstAuxiliaryButtonText = "Cancel",
                        ColorScheme = MainWindow.DialogColorScheme
                    };
                    result = await MainWindow.ShowMessage("Checkout",
                        "Vault contains uncommitted changes. Do you want to force the checkout operation?",
                        MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary, settings);
                }).Wait();

                switch (result)
                {
                    case MessageDialogResult.Affirmative:
                        DoCheckout(false);
                        break;
                    case MessageDialogResult.Negative:
                        DoCheckout(true);
                        break;
                    case MessageDialogResult.FirstAuxiliary:
                    default:
                        return;
                }
            }
            else
            {
                DoCheckout(false);
            }
        }

        private void DoCheckout(bool purge)
        {
            OperationStatusDialog.Start("Checkout");
            m_AreaVM.Area.Checkout(Name, purge, false, false);
            m_AreaVM.RefreshBranches();
            OperationStatusDialog.Finish();
        }

        private void Log()
        {
            Version headVersion = m_AreaVM.Area.GetBranchHeadVersion(m_Branch);
            LogDialog.Show(headVersion, m_AreaVM.Area);
        }

        private void CreatePullRequest()
        {
            string url = $"https://bucktr.ea.com:44320/pullrequests/Create?repo={Utilities.RepoName}&origin={Name}";
            System.Diagnostics.Process.Start(url);
        }
    }
}
