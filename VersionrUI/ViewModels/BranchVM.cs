using MahApps.Metro.Controls.Dialogs;
using System.Collections.Generic;
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

        private AreaVM _areaVM;
        private Branch _branch;
        private List<VersionVM> _history = null;
        private string _searchText;
        private VersionVM _selectedVersion;
        private int _revisionLimit = 50;
        private static Dictionary<int, string> _revisionLimitOptions = new Dictionary<int, string>()
        {
            { 50, "50" },
            { 100, "100" },
            { 150, "150" },
            { 200, "200" },
            { -1, "All" },
        };

        public string SearchText
        {
            get { return _searchText; }
            private set
            {
                _searchText = value;
                NotifyPropertyChanged("SearchText");
                NotifyPropertyChanged("History");
            }
        }

        public BranchVM(AreaVM areaVM, Branch branch)
        {
            _areaVM = areaVM;
            _branch = branch;

            PullCommand = new DelegateCommand(() => Load(Pull));
            PushCommand = new DelegateCommand(() => Load(Push));
            CheckoutCommand = new DelegateCommand(() => Load(Checkout), () => !IsCurrent);
            LogCommand = new DelegateCommand(Log);
        }

        public Branch Branch
        {
            get { return _branch; }
        }

        public string Name
        {
            get { return _branch.Name; }
        }

        public bool IsDeleted
        {
            get { return _branch.Terminus.HasValue; }
        }

        public bool IsCurrent
        {
            get { return _areaVM.Area.CurrentBranch.ID == _branch.ID; }
        }

        public List<VersionVM> History
        {
            get
            {
                if (_history == null)
                    Load(Refresh);
                if (!string.IsNullOrEmpty(SearchText))
                    return FilterHistory(_history, SearchText);
                return _history;
            }
        }

        public VersionVM SelectedVersion
        {
            get { return _selectedVersion; }
            private set
            {
                _selectedVersion = value;
                NotifyPropertyChanged("SelectedVersion");
            }
        }

        public int RevisionLimit
        {
            get { return _revisionLimit; }
            set
            {
                if (_revisionLimit != value)
                {
                    _revisionLimit = value;
                    NotifyPropertyChanged("RevisionLimit");
                    NotifyPropertyChanged("History");
                }
            }
        }

        public Dictionary<int, string> RevisionLimitOptions
        {
            get { return _revisionLimitOptions; }
        }

        private List<VersionVM> FilterHistory(List<VersionVM> history, string searchtext)
        {
            searchtext = searchtext.ToLower();
            List<VersionVM> results = new List<VersionVM>();
            foreach (VersionVM version in history)
            {
                if (version.Message.ToLower().Contains(searchtext) ||
                    version.ID.ToString().ToLower().Contains(searchtext) ||
                    version.Author.ToLower().Contains(searchtext) ||
                    version.Timestamp.ToString().ToLower().Contains(searchtext))
                {
                    results.Add(version);
                }
            }
            return results;
        }

        private static object refreshLock = new object();
        private void Refresh()
        {
            lock (refreshLock)
            {
                var headVersion = _areaVM.Area.GetBranchHeadVersion(_branch);
                int? limit = (RevisionLimit != -1) ? RevisionLimit : (int?)null;
                List<Version> versions = _areaVM.Area.GetLogicalHistory(headVersion, limit);
                _history = new List<VersionVM>();

                foreach (Version version in versions)
                    _history.Add(new VersionVM(version, _areaVM.Area));
                NotifyPropertyChanged("History");
            }
        }

        private void Pull()
        {
            OperationStatusDialog.Start("Pull");
            _areaVM.ExecuteClientCommand((c) => c.Pull(true, Name), "pull");
            if(IsCurrent)
                _areaVM.Area.Update();
            OperationStatusDialog.Finish();
        }

        private void Push()
        {
            OperationStatusDialog.Start("Push");
            _areaVM.ExecuteClientCommand((c) => c.Push(Name), "push", true);
            OperationStatusDialog.Finish();
        }

        private void Checkout()
        {
            if (_areaVM.Area.Status.HasModifications(false))
            {
                MessageDialogResult result = MessageDialogResult.FirstAuxiliary;
                MainWindow.Instance.Dispatcher.Invoke(async () =>
                {
                    MetroDialogSettings settings = new MetroDialogSettings()
                    {
                        AffirmativeButtonText = "Checkout (keep unversioned files)",
                        NegativeButtonText = "Checkout (purge unversioned files)",
                        FirstAuxiliaryButtonText = "Cancel",
                        ColorScheme = MetroDialogColorScheme.Accented
                    };
                    result = await MainWindow.Instance.ShowMessageAsync("Checkout", "Vault contains uncommitted changes. Do you want to force the checkout operation?", MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary, settings);
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
            _areaVM.Area.Checkout(Name, purge, false, false);
            _areaVM.RefreshChildren();
            OperationStatusDialog.Finish();
        }

        private void Log()
        {
            Version headVersion = _areaVM.Area.GetBranchHeadVersion(_branch);
            LogDialog.Show(headVersion, _areaVM.Area);
        }
    }
}
