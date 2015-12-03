using System;
using System.Windows;

namespace VersionrUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _userSettingsLoaded = false;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
        }

        public static MainWindow Instance { get; private set; }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            System.Drawing.Size windowSize = Properties.Settings.Default.WindowSize;
            this.Width = windowSize.Width;
            this.Height = windowSize.Height;

            System.Drawing.Point windowLocation = Properties.Settings.Default.WindowLocation;
            this.Left = windowLocation.X;
            this.Top = windowLocation.Y;

            WindowState state = (WindowState)Properties.Settings.Default.WindowState;
            if (state != WindowState.Minimized)
                this.WindowState = state;

            _userSettingsLoaded = true;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_userSettingsLoaded && this.WindowState == WindowState.Normal)
            {
                Properties.Settings.Default.WindowSize = new System.Drawing.Size((int)e.NewSize.Width, (int)e.NewSize.Height);
                Properties.Settings.Default.Save();
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (_userSettingsLoaded && this.WindowState == WindowState.Normal)
            {
                Properties.Settings.Default.WindowLocation = new System.Drawing.Point((int)this.Left, (int)this.Top);
                Properties.Settings.Default.Save();
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (_userSettingsLoaded && this.WindowState != WindowState.Minimized)
            {
                Properties.Settings.Default.WindowState = (int)this.WindowState;
                Properties.Settings.Default.Save();
            }
        }
    }
}
