using System.Windows;

namespace VersionrUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
        }
        public static MainWindow Instance { get; private set; }
    }
}
