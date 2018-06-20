using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
        private AreaVM m_SelectedArea = null;
        private GridViewColumnHeader m_LastHeaderClicked = null;
        private ListSortDirection m_LastDirection = ListSortDirection.Ascending;
        private static readonly Dictionary<int, string> m_RevisionLimitOptions = new Dictionary<int, string>()
        {
            { 50, "50" },
            { 100, "100" },
            { 150, "150" },
            { 200, "200" },
            { 500, "500" },
            { 1000, "1000" },
            { 2000, "2000" },
            { -1, "All" },
        };

        private Point m_mouseDownStartPos;
        private readonly List<string> m_SelectedFilesForDragDropCopy = new List<string>();

        public VersionrPanel()
        {
            InitializeComponent();
            mainGrid.DataContext = this;

            NewAreaCommand = new DelegateCommand(AddArea);

            OpenAreas = new ObservableCollection<AreaVM>();

            // Load previously opened areas
            if (Properties.Settings.Default.OpenAreas != null)
            {
                foreach (string areaString in Properties.Settings.Default.OpenAreas)
                {
                    string[] parts = areaString.Split(';');
                    AreaVM areaVM = AreaVM.Create(parts[1], parts[0],
                        (x, title, message) =>
                        {
                            if (!x.IsValid)
                            {
                                // TODO: notify area has been removed. Can't call this while initializing MainWindow...
                                // MainWindow.ShowMessage(title, message);
                                OpenAreas.Remove(x);
                            }
                            SaveOpenAreas(OpenAreas);
                        },
                        AreaInitMode.UseExisting);
                    OpenAreas.Add(areaVM);
                }
            }
            SelectedArea = OpenAreas.FirstOrDefault();
        }

        public ObservableCollection<AreaVM> OpenAreas { get; private set; }

        public AreaVM SelectedArea
        {
            get => m_SelectedArea;
            set
            {
                if (m_SelectedArea == value)
                    return;
                m_SelectedArea = value;
                NotifyPropertyChanged("SelectedArea");
            }
        }

        public static IList SelectedItems
        {
            get
            {
                ListView lv = FindChild<ListView>(Application.Current.MainWindow, "listView");
                return lv?.SelectedItems;
            }
        }

        public Dictionary<int, string> RevisionLimitOptions => m_RevisionLimitOptions;

        public void SetSelectedItem(object item)
        {
            ListView lv = FindChild<ListView>(Application.Current.MainWindow, "listView");
            if (lv != null)
                lv.SelectedItem = item;
        }

        public static void SaveOpenAreas(ObservableCollection<AreaVM> openAreas)
        {
            Properties.Settings.Default.OpenAreas = new StringCollection();
            foreach (AreaVM area in openAreas.Where(x => x.IsValid))
            {
                string areaString = $"{area.Directory.FullName};{area.Name}";
                Properties.Settings.Default.OpenAreas.Add(areaString);
            }
            Properties.Settings.Default.Save();
        }

        #region Commands
        public DelegateCommand NewAreaCommand { get; private set; }

        private async void AddArea()
        {
            CloneNewDialog cloneNewDlg = new CloneNewDialog();
            await MainWindow.Instance.ShowMetroDialogAsync(cloneNewDlg);
            await cloneNewDlg.WaitUntilUnloadedAsync();
            if (cloneNewDlg.DialogResult != true)
                return;
            int.TryParse(cloneNewDlg.Port, out var port);
            AreaVM areaVM = AreaVM.Create(cloneNewDlg.NameString, cloneNewDlg.PathString,
                (x, title, message) =>
                {
                    if (!x.IsValid)
                    {
                        MainWindow.ShowMessage(title, message);
                        OpenAreas.Remove(x);
                    }
                    SaveOpenAreas(OpenAreas);
                },
                cloneNewDlg.Result, cloneNewDlg.Host, port);
            OpenAreas.Add(areaVM);
            SelectedArea = OpenAreas.LastOrDefault();
        }
        #endregion

        private void listViewHeader_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is ListView))
                return;

            if (!(e.OriginalSource is GridViewColumnHeader headerClicked))
                return;
            if (headerClicked.Role == GridViewColumnHeaderRole.Padding)
                return;
            ListSortDirection direction;
            if (headerClicked != m_LastHeaderClicked)
            {
                direction = ListSortDirection.Ascending;
            }
            else
            {
                direction = m_LastDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            }

            string header = headerClicked.Column.Header as string;
            Sort(CollectionViewSource.GetDefaultView(((ListView)sender).ItemsSource), header, direction);

            if (direction == ListSortDirection.Ascending)
                headerClicked.Column.HeaderTemplate = Resources["HeaderTemplateArrowUp"] as DataTemplate;
            else
                headerClicked.Column.HeaderTemplate = Resources["HeaderTemplateArrowDown"] as DataTemplate;

            // Remove arrow from previously sorted header
            if (m_LastHeaderClicked != null && m_LastHeaderClicked != headerClicked)
                m_LastHeaderClicked.Column.HeaderTemplate = null;

            m_LastHeaderClicked = headerClicked;
            m_LastDirection = direction;
        }

        internal static void Sort(ICollectionView dataView, string sortBy, ListSortDirection direction)
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
            else if (sortBy == "Type")
            {
                foreach (object obj in dataView.SourceCollection)
                {
                    if (obj.GetType().GetProperty("AlterationType") != null)
                        sortBy = "AlterationType";
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

        private void CheckBox_Clicked(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox checkbox))
                return;
            // The checked item may not be one of the already selected items, so update the selections
            if (checkbox.DataContext is StatusEntryVM && !SelectedItems.Contains((StatusEntryVM)checkbox.DataContext))
                SetSelectedItem(checkbox.DataContext);
                
            m_SelectedArea.SetStaged(SelectedItems.OfType<StatusEntryVM>().ToList(), checkbox.IsChecked == true);
        }

        private void newTagText_TextChanged(object sender, TextChangedEventArgs e)
        {
            SelectedArea?.Status.AddTagCommand.RaiseCanExecuteChanged();
        }

        private void newTagText_KeyUp(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textbox && (e.Key == Key.Return || e.Key == Key.Enter))
            {
                SelectedArea?.Status.AddTagCommand.Execute(textbox);
            }
        }

        #region Drag Drop File Copy

        private void listView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is ListView) || SelectedArea == null)
                return;
            m_mouseDownStartPos = e.GetPosition(null);
            m_SelectedFilesForDragDropCopy.Clear();

            // Drag drop copy from the status view
            foreach (var item in SelectedItems.OfType<StatusEntryVM>())
            {
                if (!item.StatusEntry.IsFile || item.StatusEntry.FilesystemEntry == null) continue;
                m_SelectedFilesForDragDropCopy.Add(item.StatusEntry.FilesystemEntry.FullName);
            }
            
            // Drag drop copy a single change from a changelist
            if ((!(((ListView) sender).SelectedItem is AlterationVM alteration)))
                return;
            m_SelectedFilesForDragDropCopy.Add(Path.Combine(SelectedArea.Area.Root.FullName, alteration.Name));
        }

        private void listView_MouseMove(object sender, MouseEventArgs e)
        {
            if (!(sender is ListView))
                return;
            Point mpos = e.GetPosition(null);
            Vector diff = m_mouseDownStartPos - mpos;

            if (e.LeftButton != MouseButtonState.Pressed ||
                !(Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance) ||
                !(Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                return;
            }

            if (m_SelectedFilesForDragDropCopy.Count == 0)
            {
                return;
            }

            DataObject dataObject = new DataObject(DataFormats.FileDrop, m_SelectedFilesForDragDropCopy.ToArray());
            DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy);
        }

        #endregion
    }
}
