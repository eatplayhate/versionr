using System.Collections.ObjectModel;
using Versionr;

namespace VersionrUI.ViewModels
{
    public class BranchVM : NotifyPropertyChangedBase
    {
        private Area _area;
        private Versionr.Objects.Branch _branch;
        private ObservableCollection<VersionVM> _history = null;

        public BranchVM(Area area, Versionr.Objects.Branch branch)
        {
            _area = area;
            _branch = branch;
        }

        public string Name
        {
            get { return _branch.Name; }
        }

        public bool IsCurrent
        {
            get { return _area.CurrentBranch == _branch; }
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

            var headVersion = _area.GetBranchHeadVersion(_branch);
            int limit = 50; // TODO: setting?
            foreach (var version in _area.GetHistory(headVersion, limit))
                _history.Add(VersionrVMFactory.GetVersionVM(version, _area));

            NotifyPropertyChanged("History");
        }
    }
}
