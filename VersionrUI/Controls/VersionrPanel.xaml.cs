using System.Collections.ObjectModel;
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
        }
        
        public ObservableCollection<AreaVM> OpenAreas { get; private set; }

        #region Commands
        public DelegateCommand NewAreaCommand {get; private set; }

        private void AddArea()
        {
            CloneNewDialog cloneNewDlg = new CloneNewDialog(MainWindow.Instance);
            cloneNewDlg.ShowDialog();
            switch (cloneNewDlg.Result)
            {
                case CloneNewDialog.ResultEnum.Cancelled:
                    return;
                case CloneNewDialog.ResultEnum.Clone:
                    // Spawn another dialog for the source (or put it in the Clone New button)
                    return;
                case CloneNewDialog.ResultEnum.InitNew:
                    // Tell versionr to initialize at path
                    return;
                case CloneNewDialog.ResultEnum.UseExisting:
                    // Add it to settings and refresh UI, get status etc.
                    return;
            }
        }
        #endregion
    }
}
