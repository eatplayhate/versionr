using System;
using System.Collections.ObjectModel;
using VersionrUI.Commands;
using VersionrUI.Dialogs;

namespace VersionrUI.ViewModels
{
    public class BranchVM : NotifyPropertyChangedBase
    {
        public DelegateCommand CheckoutCommand { get; private set; }

        private AreaVM _areaVM;
        private Versionr.Objects.Branch _branch;
        private ObservableCollection<VersionVM> _history = null;

        public BranchVM(AreaVM areaVM, Versionr.Objects.Branch branch)
        {
            _areaVM = areaVM;
            _branch = branch;
            CheckoutCommand = new DelegateCommand(Checkout);
        }

        public string Name
        {
            get { return _branch.Name; }
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

        private void RefreshHistory()
        {
            if (_history == null)
                _history = new ObservableCollection<VersionVM>();
            else
                _history.Clear();

            var headVersion = _areaVM.Area.GetBranchHeadVersion(_branch);
            int limit = 50; // TODO: setting?
            foreach (var version in _areaVM.Area.GetHistory(headVersion, limit))
                _history.Add(VersionrVMFactory.GetVersionVM(version, _areaVM.Area));

            NotifyPropertyChanged("History");
        }

        private void Checkout()
        {
            Load(() =>
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
                            _areaVM.Area.Checkout(Name, false, true, false);
                            break;
                        case 1:
                            _areaVM.Area.Checkout(Name, true, true, false);
                            break;
                        case 2:
                        default:
                            return;
                    }
                }
                else
                {
                    _areaVM.Area.Checkout(Name, false, true, false);
                }

                _areaVM.RefreshStatusAndBranches();
            });
        }
    }
}
