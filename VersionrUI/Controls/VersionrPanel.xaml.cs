using System;
using System.Collections;
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

using System.Windows.Media;

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

         public static IList SelectedItems
         {
            get
            {
                ListView lv = FindChild<ListView>(Application.Current.MainWindow, "listView");
                if (lv != null)
                    return lv.SelectedItems;

                return null;
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
            if (sortBy == "Name")
            {
                foreach (object obj in dataView.SourceCollection)
                {
                    if (obj.GetType().GetProperty("CanonicalName") != null)
                        sortBy = "CanonicalName";
                    break; // Only test one item
                }
            }
            
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

        #region Utility Methods
        public static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null)
                return null;

            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindParent<T>(parentObject);
        }

        public static T FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            // Confirm parent and childName are valid. 
            if (parent == null)
                return null;

            T foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                // If the child is not of the request child type child
                T childType = child as T;
                if (childType == null)
                {
                    // recursively drill down the tree
                    foundChild = FindChild<T>(child, childName);

                    // If the child is found, break so we do not overwrite the found child. 
                    if (foundChild != null)
                        break;
                }
                else if (!string.IsNullOrEmpty(childName))
                {
                    var frameworkElement = child as FrameworkElement;
                    // If the child's name is set for search
                    if (frameworkElement != null && frameworkElement.Name == childName)
                    {
                        // if the child's name is of the request name
                        foundChild = (T)child;
                        break;
                    }
                }
                else
                {
                    // child element found.
                    foundChild = (T)child;
                    break;
                }
            }

            return foundChild;
        }
        #endregion

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _selectedArea.Status.SetStaged(SelectedItems.OfType<StatusEntryVM>().ToList(), true);
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _selectedArea.Status.SetStaged(SelectedItems.OfType<StatusEntryVM>().ToList(), false);
        }
    }
}
