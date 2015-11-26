using System;
using System.Collections.Generic;
using Versionr;
using Versionr.Objects;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class VersionVM
    {
        private Version _version;
        private Area _area;

        public VersionVM(Version version, Area area)
        {
            _version = version;
            _area = area;
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

        public List<AlterationVM> Alterations
        {
            get
            {
                List<AlterationVM> alterations = new List<AlterationVM>();

                foreach (Alteration alteration in _area.GetAlterations(_version))
                    alterations.Add(new AlterationVM(alteration, _area));

                return alterations;
            }
        }
    }
}
