using System.Linq;
using System.Collections.ObjectModel;
using Versionr;
using System.Collections.Generic;
using System.Collections.Specialized;
using VersionrUI.Commands;
using System.Windows;

namespace VersionrUI.ViewModels
{
    public class StatusVM : NotifyPropertyChangedBase
    {
        public DelegateCommand<string> CommitCommand { get; private set; }

        private Status _status;
        private Area _area;
        private ObservableCollection<StatusEntryVM> _elements;

        public StatusVM(Status status, Area area)
        {
            _status = status;
            _area = area;
            _elements = new ObservableCollection<StatusEntryVM>();
            _elements.CollectionChanged += elements_CollectionChanged;

            CommitCommand = new DelegateCommand<string>(Commit);

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

        private void Commit(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                MessageBox.Show("Please provide a commit message", "Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // TODO: commit staged changes
            MessageBox.Show("// TODO: commit staged changes");
        }
    }
}
