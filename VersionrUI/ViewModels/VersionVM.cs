using System;

namespace VersionrUI.ViewModels
{
    public class VersionVM
    {
        private Versionr.Objects.Version _version;

        public VersionVM(Versionr.Objects.Version version)
        {
            _version = version;
        }

        public Guid ID
        {
            get { return _version.ID; }
        }

        public string Author
        {
            get { return _version.Author; }
        }

        public string Message
        {
            get { return _version.Message; }
        }

        public BranchVM Branch
        {
            get { return null; }    // TODO share the same branchVM as those coming from AreaVM
        }

        public DateTime Timestamp
        {
            get { return _version.Timestamp; }
        }

        public uint Revision
        {
            get { return _version.Revision; }
        }

        // TODO list of alterations
    }
}
