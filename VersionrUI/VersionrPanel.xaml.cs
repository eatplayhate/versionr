using System.Collections.ObjectModel;
using System.Windows.Controls;
using VersionrUI.ViewModels;

namespace VersionrUI
{
    /// <summary>
    /// Interaction logic for VersionrPanel.xaml
    /// </summary>
    public partial class VersionrPanel : UserControl
    {
        public VersionrPanel()
        {
            InitializeComponent();
            DataContext = mainGrid;
        }

        public ObservableCollection<AreaVM> OpenAreas { get; private set; }
    }
}
