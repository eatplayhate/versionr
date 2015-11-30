using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Versionr;
using Versionr.Objects;
using Version = Versionr.Objects.Version;

namespace VersionrUI.ViewModels
{
    public class VersionVM : NotifyPropertyChangedBase
    {
        private Version _version;
        private Area _area;
        private ObservableCollection<AlterationVM> _alterations;

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

        public ObservableCollection<AlterationVM> Alterations
        {
            get
            {
                if (_alterations == null)
                    Load(() => Refresh());
                return _alterations;
            }
        }

        private object refreshLock = new object();
        private void Refresh()
        {
            lock (refreshLock)
            {
                List<Alteration> alterations = _area.GetAlterations(_version);
                MainWindow.Instance.Dispatcher.Invoke(() =>
                {
                    if (_alterations == null)
                        _alterations = new ObservableCollection<AlterationVM>();
                    else
                        _alterations.Clear();

                    foreach (Alteration alteration in alterations)
                        _alterations.Add(new AlterationVM(alteration, _area));

                    NotifyPropertyChanged("Alterations");
                });
            }
        }
    }
}
