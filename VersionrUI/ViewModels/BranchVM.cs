using System.Collections.ObjectModel;
using Versionr;

namespace VersionrUI.ViewModels
{
    public class BranchVM : NotifyPropertyChangedBase
    {
        private Area _area;
        private Versionr.Objects.Branch _branch;
        private VersionVM _selectedVersion = null;
        private ObservableCollection<VersionVM> _history;

        public BranchVM(Area area, Versionr.Objects.Branch branch)
        {
            _area = area;
            _branch = branch;
            _history = new ObservableCollection<VersionVM>();

            RefreshHistory();
        }

        private void RefreshHistory()
        {
            _history.Clear();

            var headVersion = _area.GetBranchHeadVersion(_branch);

            int limit = 50; // TODO: setting?
            foreach (var version in _area.GetHistory(headVersion, limit))
            {
                _history.Add(VersionrVMFactory.GetVersionVM(version));
            }
        }

        public string Name
        {
            get { return _branch.Name; }
        }

        public bool IsCurrent
        {
            get { return _area.CurrentBranch == _branch; }
        }

        public VersionVM SelectedVersion
        {
            get { return _selectedVersion; }
            set
            {
                if (_selectedVersion != value)
                {
                    _selectedVersion = value;
                    NotifyPropertyChanged("SelectedVersion");
                }
            }
        }
    }
}
