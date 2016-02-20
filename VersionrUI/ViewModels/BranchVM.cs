using System.Collections.Generic;
using System.Collections.ObjectModel;
using Versionr.Objects;
using VersionrUI.Commands;
using VersionrUI.Dialogs;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class BranchVM : NotifyPropertyChangedBase
    {
        public DelegateCommand CheckoutCommand { get; private set; }
        public DelegateCommand LogCommand { get; private set; }

        private AreaVM _areaVM;
        private Branch _branch;
        private ObservableCollection<VersionVM> _history = null;
        private string _searchText;
        private VersionVM _selectedVersion;
        private bool _historyRefreshing = false;
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
                _historyRefreshing = true;
                NotifyPropertyChanged("History");
                _historyRefreshing = false;
            }
        }

        public BranchVM(AreaVM areaVM, Branch branch)
        {
            _areaVM = areaVM;
            _branch = branch;

            CheckoutCommand = new DelegateCommand(Checkout);
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

        public ObservableCollection<VersionVM> History
        {
            get
            {
                if (!_historyRefreshing)
                    Load(() => RefreshHistory());
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

        private ObservableCollection<VersionVM> FilterHistory(ObservableCollection<VersionVM> history, string searchtext)
        {
            searchtext = searchtext.ToLower();
            ObservableCollection<VersionVM> results = new ObservableCollection<VersionVM>();
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

        private object refreshLock = new object();
        private void RefreshHistory()
        {
            lock (refreshLock)
            {
                _historyRefreshing = true;
                var headVersion = _areaVM.Area.GetBranchHeadVersion(_branch);
                int? limit = (RevisionLimit != -1) ? RevisionLimit : (int?)null;
                List<Version> versions = _areaVM.Area.GetHistory(headVersion, limit);

                MainWindow.Instance.Dispatcher.Invoke(() =>
                {
                    if (_history == null)
                        _history = new ObservableCollection<VersionVM>();
                    else
                        _history.Clear();

                    foreach (Version version in versions)
                        _history.Add(new VersionVM(version, _areaVM.Area));
                    NotifyPropertyChanged("History");
                    _historyRefreshing = false;
                });
            }
        }

        private void Checkout()
        {
            if (_areaVM.Area.Status.HasModifications(false))
            {
                int result = CustomMessageBox.Show("Vault contains uncommitted changes.\nDo you want to force the checkout operation?",
                                                   "Checkout",
                                                   new string[] { "Checkout (keep unversioned files)",
                                                                  "Checkout (purge unversioned files)",
                                                                  "Cancel" },
                                                   2);

                switch (result)
                {
                    case 0:
                        CheckoutAsync(false);
                        break;
                    case 1:
                        CheckoutAsync(true);
                        break;
                    case 2:
                    default:
                        return;
                }
            }
            else
            {
                CheckoutAsync(false);
            }
        }

        private void CheckoutAsync(bool purge)
        {
            Load(() =>
            {
                _areaVM.Area.Checkout(Name, purge, false, false);
                _areaVM.RefreshStatusAndBranches();
            });
        }

        private void Log()
        {
            Version headVersion = _areaVM.Area.GetBranchHeadVersion(_branch);
            new LogDialog(headVersion, _areaVM.Area).ShowDialog();
        }
    }
}
