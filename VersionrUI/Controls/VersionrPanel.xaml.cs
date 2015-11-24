using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VersionrUI.Commands;
using VersionrUI.Dialogs;
using VersionrUI.ViewModels;

namespace VersionrUI.Controls
{
    /// <summary>
    /// Interaction logic for VersionrPanel.xaml
    /// </summary>
    public partial class VersionrPanel : UserControl
    {
        public VersionrPanel()
        {
            InitializeComponent();
            mainGrid.DataContext = this;

            NewAreaCommand = new DelegateCommand(AddArea);

            OpenAreas = new ObservableCollection<AreaVM>();
        }

        public ObservableCollection<AreaVM> OpenAreas { get; private set; }

        public AreaVM SelectedArea { get; set; }

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
                    OpenAreas.Add(new AreaVM(Versionr.Area.Init(new System.IO.DirectoryInfo(cloneNewDlg.PathString), cloneNewDlg.NameString), cloneNewDlg.NameString));
                    break;
                case CloneNewDialog.ResultEnum.UseExisting:
                    // Add it to settings and refresh UI, get status etc.
                    OpenAreas.Add(new AreaVM(Versionr.Area.Load(new System.IO.DirectoryInfo(cloneNewDlg.PathString)), cloneNewDlg.NameString));
                    break;
                case CloneNewDialog.ResultEnum.Cancelled:
                default:
                    break;
            }
        }
        #endregion

        private void BranchesTreeView_SelectedItemChanged(object sender, RoutedEventArgs e)
        {
            TreeView treeView = sender as TreeView;
            if (treeView != null)
            {
                SelectedArea.SelectedBranch = treeView.SelectedItem as BranchVM;
            }
        }
    }
}
