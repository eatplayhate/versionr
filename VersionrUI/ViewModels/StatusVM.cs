using System.Collections.ObjectModel;
using Versionr;

namespace VersionrUI.ViewModels
{
    public class StatusVM
    {
        private Status _status;
        private ObservableCollection<StatusEntryVM> _elements;

        public StatusVM(Status status)
        {
            _status = status;
            _elements = new ObservableCollection<StatusEntryVM>();

            RefreshElements();
        }

        private void RefreshElements()
        {
            _elements.Clear();

            foreach (Status.StatusEntry statusEntry in _status.Elements)
            {
                _elements.Add(new StatusEntryVM(statusEntry));
            }
        }

        public ObservableCollection<StatusEntryVM> Elements
        {
            get { return _elements; }
        }
    }
}
