using System.Linq;
using System.Collections.ObjectModel;
using Versionr;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace VersionrUI.ViewModels
{
    public class StatusVM : NotifyPropertyChangedBase
    {
        private Status _status;
        private Area _area;
        private ObservableCollection<StatusEntryVM> _elements;

        public StatusVM(Status status, Area area)
        {
            _status = status;
            _area = area;
            _elements = new ObservableCollection<StatusEntryVM>();
            _elements.CollectionChanged += elements_CollectionChanged;

            RefreshElements();
        }
        
        private void RefreshElements()
        {
            _elements.Clear();

            foreach (Status.StatusEntry statusEntry in _status.Elements)
            {
                _elements.Add(VersionrVMFactory.GetStatusEntryVM(statusEntry, _area));
            }
        }

        public ObservableCollection<StatusEntryVM> Elements
        {
            get { return _elements; }
        }

        public IEnumerable<StatusEntryVM> ModifiedElements
        {
            get { return _elements.Where(x => x.Code != StatusCode.Unchanged); }
        }
        private void elements_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            NotifyPropertyChanged("ModifiedElements");
        }
    }
}
