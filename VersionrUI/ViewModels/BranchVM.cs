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
                if (_history == null)
                    Load(() => RefreshHistory());
                return _history;
            }
        }

        private object refreshLock = new object();
        private void RefreshHistory()
        {
            lock (refreshLock)
            {
                var headVersion = _areaVM.Area.GetBranchHeadVersion(_branch);
                int limit = 50; // TODO: setting?
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
