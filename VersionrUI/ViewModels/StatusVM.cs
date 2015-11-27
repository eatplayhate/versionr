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
        private bool _pushOnCommit;
        private string _commitMessage;

        public StatusVM(AreaVM areaVM)
        {
            _areaVM = areaVM;

            CommitCommand = new DelegateCommand(Commit);
        }

        public Status Status
        {
            get
            {
                if (_status == null)
                    Load(() => Refresh());
                return _status;
            }
        }

        public bool PushOnCommit
        {
            get { return _pushOnCommit; }
            set
            {
                if (_pushOnCommit != value)
                {
                    _pushOnCommit = value;
                    NotifyPropertyChanged("PushOnCommit");
                }
            }
        }

        public string CommitMessage
        {
            get { return _commitMessage; }
            set
            {
                if (_commitMessage != value)
                {
                    _commitMessage = value;
                    NotifyPropertyChanged("CommitMessage");
                }
            }
        }

        public void Refresh()
        {
            _status = _areaVM.Area.GetStatus(_areaVM.Area.Root);

            if (_elements == null)
                _elements = new ObservableCollection<StatusEntryVM>();
            else
                _elements.Clear();

            foreach (Status.StatusEntry statusEntry in Status.Elements)
            {
                if (statusEntry.Code != StatusCode.Masked && statusEntry.Code != StatusCode.Ignored)
                    _elements.Add(VersionrVMFactory.GetStatusEntryVM(statusEntry, this, _areaVM.Area));
            }

            NotifyPropertyChanged("Status");
            NotifyPropertyChanged("Elements");
            NotifyPropertyChanged("ModifiedElements");
        }

        public ObservableCollection<StatusEntryVM> Elements
        {
            get
            {
                if (_elements == null)
                    Load(() => Refresh());
                return _elements;
            }
        }

        public IEnumerable<StatusEntryVM> ModifiedElements
        {
            get { return Elements?.Where(x => x.Code != StatusCode.Unchanged); }
        }

        private void Commit()
        {
            if (string.IsNullOrEmpty(CommitMessage))
            {
                MessageBox.Show("Please provide a commit message", "Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!_areaVM.Area.Commit(CommitMessage, false))
            {
                MessageBox.Show("Could not commit as it would create a new head.", "Commit failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (PushOnCommit)
                _areaVM.ExecuteClientCommand((c) => c.Push(), "push", true);

            CommitMessage = string.Empty;
            Refresh();
        }
    }
}
