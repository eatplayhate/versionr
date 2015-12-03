using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace VersionrUI.Controls
{
    /// <summary>
    /// Interaction logic for LoadingPanel.xaml
    /// </summary>
    public partial class LoadingPanel : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register("IsLoading", typeof(bool), typeof(LoadingPanel), new UIPropertyMetadata(false, new PropertyChangedCallback(IsLoadingChanged)));
        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register("Message", typeof(string), typeof(LoadingPanel), new UIPropertyMetadata("Loading..."));

        public LoadingPanel()
        {
            InitializeComponent();
            mainGrid.DataContext = this;
        }

        public bool IsLoading
        {
            get { return (bool)GetValue(IsLoadingProperty); }
            set
            {
                SetValue(IsLoadingProperty, value);
                NotifyPropertyChanged("IsLoading");
                NotifyPropertyChanged("GridVisibility");
            }
        }

        public string Message
        {
            get { return (string)GetValue(MessageProperty); }
            set { SetValue(MessageProperty, value); }
        }

        public Visibility GridVisibility
        {
            get { return IsLoading ? Visibility.Visible : Visibility.Collapsed; }
        }

        public static void IsLoadingChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            LoadingPanel panel = obj as LoadingPanel;
            if (panel != null)
                panel.Visibility = panel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }
    }
}
