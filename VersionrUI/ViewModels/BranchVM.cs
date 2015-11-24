using System;
using Versionr;

namespace VersionrUI.ViewModels
{
    public class BranchVM
    {
        private Area _area;
        private Versionr.Objects.Branch _branch;

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
    }
}
