using Versionr;

namespace VersionrUI.ViewModels
{
    public class BranchVM : NotifyPropertyChangedBase
    {
        private Area _area;
        private Versionr.Objects.Branch _branch;
        private VersionVM _selectedVersion = null;

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
