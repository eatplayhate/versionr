using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Versionr;
using VersionrUI.Commands;

namespace VersionrUI.ViewModels
{
    public class StatusVM : NotifyPropertyChangedBase
    {
        public DelegateCommand CommitCommand { get; private set; }

        private Status _status;
        private AreaVM _areaVM;
        private ObservableCollection<StatusEntryVM> _elements;

        public StatusVM(Status status, AreaVM areaVM)
        {
            _status = status;
            _areaVM = areaVM;
            _elements = new ObservableCollection<StatusEntryVM>();

            CommitCommand = new DelegateCommand(Commit);

            RefreshElements();
        }

        public Status Status { get { return _status; } }

        public bool PushOnCommit { get; set; }

        public string CommitMessage { get; set; }

        public void RefreshElements()
        {
            _elements.Clear();

            foreach (Status.StatusEntry statusEntry in _status.Elements)
            {
                if (statusEntry.Code != StatusCode.Masked && statusEntry.Code != StatusCode.Ignored)
                    _elements.Add(VersionrVMFactory.GetStatusEntryVM(statusEntry, this, _areaVM.Area));
            }

            NotifyPropertyChanged("Elements");
            NotifyPropertyChanged("ModifiedElements");
        }

        public ObservableCollection<StatusEntryVM> Elements
        {
            get { return _elements; }
        }

        public IEnumerable<StatusEntryVM> ModifiedElements
        {
            get { return _elements.Where(x => x.Code != StatusCode.Unchanged); }
        }
        
        private void Commit()
        {
            if (string.IsNullOrEmpty(CommitMessage))
            {
                MessageBox.Show("Please provide a commit message", "Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!_areaVM.Area.Commit(CommitMessage, false))
                MessageBox.Show("Could not commit as it would create a new head.", "Commit failed", MessageBoxButton.OK, MessageBoxImage.Error);

            if (PushOnCommit)
                _areaVM.ExecuteClientCommand((c) => c.Push(), "push", true);


            RefreshElements();
        }
    }
}
