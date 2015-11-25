using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
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

            CloseCommand = new DelegateCommand(() => MainWindow.Instance.Close());
            NewAreaCommand = new DelegateCommand(AddArea);
            RemoveAreaCommand = new DelegateCommand<AreaVM>(RemoveArea);
            RefreshCommand = new DelegateCommand(Refresh);

            OpenAreas = new ObservableCollection<AreaVM>();

            // Load previously opened areas
            if (Properties.Settings.Default.OpenAreas != null)
            {
                foreach(string areaString in Properties.Settings.Default.OpenAreas)
                {
                    string[] parts = areaString.Split(';');
                    DirectoryInfo dir = new DirectoryInfo(parts[0]);
                    if (dir.Exists)
                        OpenAreas.Add(VersionrVMFactory.GetAreaVM(Versionr.Area.Load(dir), parts[1]));
                }
            }
            OpenAreas.CollectionChanged += OpenAreas_CollectionChanged;
            SelectedArea = OpenAreas.FirstOrDefault();
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
        public DelegateCommand CloseCommand { get; private set; }
        public DelegateCommand NewAreaCommand { get; private set; }
        public DelegateCommand<AreaVM> RemoveAreaCommand { get; private set; }
        public DelegateCommand RefreshCommand { get; private set; }

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
                    OpenAreas.Add(VersionrVMFactory.GetAreaVM(Versionr.Area.Init(new DirectoryInfo(cloneNewDlg.PathString), cloneNewDlg.NameString), cloneNewDlg.NameString));
                    SelectedArea = OpenAreas.LastOrDefault();
                    break;
                case CloneNewDialog.ResultEnum.UseExisting:
                    // Add it to settings and refresh UI, get status etc.
                    OpenAreas.Add(VersionrVMFactory.GetAreaVM(Versionr.Area.Load(new DirectoryInfo(cloneNewDlg.PathString)), cloneNewDlg.NameString));
                    SelectedArea = OpenAreas.LastOrDefault();
                    break;
                case CloneNewDialog.ResultEnum.Cancelled:
                default:
                    break;
            }
        }

        private void RemoveArea(AreaVM area)
        {
            OpenAreas.Remove(area);
        }

        private void Refresh()
        {
            // TODO: refresh all open areas
            MessageBox.Show("// TODO: refresh all open areas");
        }
        #endregion

        private void SaveOpenAreas()
        {
            Properties.Settings.Default.OpenAreas = new StringCollection();
            foreach (AreaVM area in OpenAreas)
            {
                string areaString = String.Format("{0};{1}", area.Directory.FullName, area.Name);
                Properties.Settings.Default.OpenAreas.Add(areaString);
            }
            Properties.Settings.Default.Save();
        }
        private void OpenAreas_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SaveOpenAreas();
        }

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
