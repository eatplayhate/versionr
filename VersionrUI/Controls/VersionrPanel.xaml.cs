using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        GridViewColumnHeader _lastHeaderClicked = null;
        ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public VersionrPanel()
        {
            InitializeComponent();
            mainGrid.DataContext = this;

            CloseCommand = new DelegateCommand(() => MainWindow.Instance.Close());
            NewAreaCommand = new DelegateCommand(AddArea);
            RemoveAreaCommand = new DelegateCommand<AreaVM>(RemoveArea);

            OpenAreas = new ObservableCollection<AreaVM>();

            // Load previously opened areas
            if (Properties.Settings.Default.OpenAreas != null)
            {
                foreach (string areaString in Properties.Settings.Default.OpenAreas)
                {
                    string[] parts = areaString.Split(';');
                    AreaVM areaVM = new AreaVM(parts[0], parts[1], AreaInitMode.UseExisting);
                    if (areaVM != null && areaVM.IsValid)
                        OpenAreas.Add(areaVM);
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

        private void AddArea()
        {
            CloneNewDialog cloneNewDlg = new CloneNewDialog(MainWindow.Instance);
            if (cloneNewDlg.ShowDialog() == true)
            {
                int port = 0;
                int.TryParse(cloneNewDlg.Port, out port);
                AreaVM areaVM = new AreaVM(cloneNewDlg.PathString, cloneNewDlg.NameString, cloneNewDlg.Result, cloneNewDlg.Host, port);
                if (areaVM != null && areaVM.IsValid)
                {
                    OpenAreas.Add(areaVM);
                    SelectedArea = OpenAreas.LastOrDefault();
                }
            }
        }

        private void RemoveArea(AreaVM area)
        {
            OpenAreas.Remove(area);
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

        private void listViewHeader_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is ListView))
                return;

            GridViewColumnHeader headerClicked = e.OriginalSource as GridViewColumnHeader;
            ListSortDirection direction;
            if (headerClicked != null)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    if (headerClicked != _lastHeaderClicked)
                    {
                        direction = ListSortDirection.Ascending;
                    }
                    else
                    {
                        if (_lastDirection == ListSortDirection.Ascending)
                            direction = ListSortDirection.Descending;
                        else
                            direction = ListSortDirection.Ascending;
                    }

                    string header = headerClicked.Column.Header as string;
                    Sort(CollectionViewSource.GetDefaultView(((ListView)sender).ItemsSource), header, direction);

                    if (direction == ListSortDirection.Ascending)
                        headerClicked.Column.HeaderTemplate = Resources["HeaderTemplateArrowUp"] as DataTemplate;
                    else
                        headerClicked.Column.HeaderTemplate = Resources["HeaderTemplateArrowDown"] as DataTemplate;

                    // Remove arrow from previously sorted header
                    if (_lastHeaderClicked != null && _lastHeaderClicked != headerClicked)
                        _lastHeaderClicked.Column.HeaderTemplate = null;

                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }
        }

        private void Sort(ICollectionView dataView, string sortBy, ListSortDirection direction)
        {
            // TODO(Yan): Temp fix for status headers
            if (sortBy == "Name")
                sortBy = "CanonicalName";

            dataView.SortDescriptions.Clear();
            SortDescription sd = new SortDescription(sortBy, direction);
            dataView.SortDescriptions.Add(sd);
            dataView.Refresh();
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
