using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using VersionrUI.Commands;

namespace VersionrUI.Dialogs
{
    /// <summary>
    /// Interaction logic for CheckoutConfirmDialog.xaml
    /// </summary>
    public partial class CustomMessageBox : Window, INotifyPropertyChanged
    {
        public DelegateCommand<int> ButtonClickCommand { get; private set; }

        private int _cancelOption = -1;

        public static int Show(string message, string title, string[] options, int cancelOption = -1, MessageBoxImage icon = MessageBoxImage.None)
        {
            CustomMessageBox box = new CustomMessageBox(message, title, options, cancelOption, icon);
            box.ShowDialog();
            return box.Result;
        }

        private CustomMessageBox(string message, string title, string[] options, int cancelOption, MessageBoxImage icon)
        {
            ButtonClickCommand = new DelegateCommand<int>(ButtonClick);

            Result = cancelOption;
            Message = message;
            Title = title;
            Options = options;

            Icon messageBoxIcon;
            if (icon == MessageBoxImage.Error)
                messageBoxIcon = SystemIcons.Error;
            else if (icon == MessageBoxImage.Hand)
                messageBoxIcon = SystemIcons.Hand;
            else if (icon == MessageBoxImage.Stop)
                messageBoxIcon = SystemIcons.Shield; // No stop icon?
            else if (icon == MessageBoxImage.Question)
                messageBoxIcon = SystemIcons.Question;
            else if (icon == MessageBoxImage.Exclamation)
                messageBoxIcon = SystemIcons.Exclamation;
            else if (icon == MessageBoxImage.Warning)
                messageBoxIcon = SystemIcons.Warning;
            else if (icon == MessageBoxImage.Information)
                messageBoxIcon = SystemIcons.Information;
            else if (icon == MessageBoxImage.Asterisk)
                messageBoxIcon = SystemIcons.Asterisk;
            else // if(icon == MessageBoxImage.None)
                messageBoxIcon = null;

            if (messageBoxIcon != null)
                Image = Imaging.CreateBitmapSourceFromHIcon(messageBoxIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            InitializeComponent();

            NotifyPropertyChanged("Title");
            NotifyPropertyChanged("Options");

            this.DataContext = this;
        }

        public int Result { get; private set; }
        public string Message { get; private set; }
        public string[] Options { get; private set; }
        public BitmapSource Image { get; private set; }

        public Visibility ImageVisibility { get { return (Image != null) ? Visibility.Visible : Visibility.Collapsed; } }
        
        private void ButtonClick(int option)
        {
            Result = option;
            DialogResult = (option != _cancelOption);
        }

        private void MessageBox_Loaded(object sender, RoutedEventArgs e)
        {
            this.Width = grid.DesiredSize.Width + SystemParameters.ResizeFrameVerticalBorderWidth * 2 + 20;
            this.Height = grid.DesiredSize.Height + SystemParameters.WindowCaptionHeight + SystemParameters.ResizeFrameHorizontalBorderHeight * 2 + 20;
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
