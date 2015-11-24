using System;
using System.Collections.ObjectModel;
using Versionr;

namespace VersionrUI.ViewModels
{
    public class AreaVM
    {
        private Area _area;
        private string _name;
        private ObservableCollection<BranchVM> _branches;

        public AreaVM(Area area, string name)
        {
            _area = area;
            _name = name;
            _branches = new ObservableCollection<BranchVM>();

            RefreshBranches();
        }

        private void RefreshBranches()
        {
            _branches.Clear();

            // TODO consider retaining BranchVM instances
            foreach (Versionr.Objects.Branch branch in _area.Branches)
            {
                _branches.Add(new BranchVM(_area, branch));
            }
        }

        public string Name
        {
            // TODO:
            get { return _name; }
        }

        public ObservableCollection<BranchVM> Branches
        {
            get
            {
                return _branches;
            }
        }
    }
}
