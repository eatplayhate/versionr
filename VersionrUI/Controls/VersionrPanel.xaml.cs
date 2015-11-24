using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using VersionrUI.Commands;
using VersionrUI.Dialogs;
using VersionrUI.ViewModels;

namespace VersionrUI.Controls
{
    /// <summary>
    /// Interaction logic for VersionrPanel.xaml
    /// </summary>
    public partial class VersionrPanel : UserControl, INotifyPropertyChanged
    {
        private AreaVM _selectedArea = null;

        public VersionrPanel()
        {
            InitializeComponent();
            mainGrid.DataContext = this;

            NewAreaCommand = new DelegateCommand(AddArea);

            OpenAreas = new ObservableCollection<AreaVM>();
        }

        public ObservableCollection<AreaVM> OpenAreas { get; private set; }

        public AreaVM SelectedArea
        {
            get { return _selectedArea; }
            set
            {
                if (_selectedArea != value)
                {
                    _selectedArea = value;
                    NotifyPropertyChanged("SelectedArea");
                }
            }
        }

        #region Commands
        public DelegateCommand NewAreaCommand {get; private set; }

        private void AddArea()
        {
            CloneNewDialog cloneNewDlg = new CloneNewDialog(MainWindow.Instance);
            cloneNewDlg.ShowDialog();
            switch (cloneNewDlg.Result)
            {
                case CloneNewDialog.ResultEnum.Clone:
                    // Spawn another dialog for the source (or put it in the Clone New button)
                    break;
                case CloneNewDialog.ResultEnum.InitNew:
                    // Tell versionr to initialize at path
                    OpenAreas.Add(VersionrVMFactory.GetAreaVM(Versionr.Area.Init(new System.IO.DirectoryInfo(cloneNewDlg.PathString), cloneNewDlg.NameString), cloneNewDlg.NameString));
                    SelectedArea = OpenAreas.LastOrDefault();
                    break;
                case CloneNewDialog.ResultEnum.UseExisting:
                    // Add it to settings and refresh UI, get status etc.
                    OpenAreas.Add(VersionrVMFactory.GetAreaVM(Versionr.Area.Load(new System.IO.DirectoryInfo(cloneNewDlg.PathString)), cloneNewDlg.NameString));
                    SelectedArea = OpenAreas.LastOrDefault();
                    break;
                case CloneNewDialog.ResultEnum.Cancelled:
                default:
                    break;
            }
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }
        #endregion
    }
}
