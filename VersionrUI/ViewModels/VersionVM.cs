using System;
using System.Collections.Generic;
using System.Linq;
using Versionr;
using Versionr.Objects;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class VersionVM : NotifyPropertyChangedBase
    {
        private Version _version;
        private Area _area;
        private List<AlterationVM> _alterations;
        
        public VersionVM(Version version, Area area)
        {
            _version = version;
            _area = area;
        }

        public Guid ID
        {
            get { return _version.ID; }
        }

        public string ShortName
        {
            get { return _version.ShortName; }
        }

        public string Author
        {
            get { return _version.Author; }
        }

        public string Message
        {
            get { return _version.Message; }
        }

        public DateTime Timestamp
        {
            get { return _version.Timestamp.ToLocalTime(); }
        }

        public bool IsCurrent
        {
            get { return _version.ID == _area.Version.ID; }
        }

        public uint Revision
        {
            get { return _version.Revision; }
        }

        public List<AlterationVM> Alterations
        {
            get
            {
                if (_alterations == null)
                    Load(Refresh);
                return _alterations;
            }
        }

        private object refreshLock = new object();
        private void Refresh()
        {
            lock (refreshLock)
            {
                List<Alteration> alterations = _area.GetAlterations(_version);
                _alterations = new List<AlterationVM>();
                
                List<AlterationVM> unordered = new List<AlterationVM>(alterations.Count);
                foreach (Alteration alteration in alterations)
                    unordered.Add(new AlterationVM(alteration, _area, _version));

                foreach (AlterationVM vm in unordered.OrderBy(x => x.Name))
                    _alterations.Add(vm);

                NotifyPropertyChanged("Alterations");
            }
        }
    }
}
