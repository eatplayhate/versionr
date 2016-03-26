using MahApps.Metro.Controls.Dialogs;
using System.ComponentModel;
using Versionr;
using VersionrUI.Commands;

namespace VersionrUI.Dialogs
{
    /// <summary>
    /// Interaction logic for OperationStatusDialog.xaml
    /// </summary>
    public partial class OperationStatusDialog : INotifyPropertyChanged
    {
        private static OperationStatusDialog _operationStatusDialog;

        public static void Start(string title)
        {
            Printer.MessagePrinted += Printer_MessagePrinted;
            MainWindow.Instance.Dispatcher.Invoke(() =>
            {
                if (_operationStatusDialog == null)
                    _operationStatusDialog = new OperationStatusDialog();
                _operationStatusDialog.OperationStarted(title);
            });
        }

        public static void Finish()
        {
            MainWindow.Instance.Dispatcher.Invoke(() =>
            {
                if (_operationStatusDialog != null)
                    _operationStatusDialog.OperationFinished();
            });
        }

        public static void Write(string message)
        {
            if (_operationStatusDialog != null)
                _operationStatusDialog.WriteMessage(message);
        }

        private static void Printer_MessagePrinted(object sender, Printer.MessagePrintedEventArgs e)
        {
            Write(e.Message);
        }


        public DelegateCommand CloseCommand { get; private set; }

        private string _text;
        private string _lastMessage;
        private bool _isOperationFinished = false;

        public OperationStatusDialog()
        {
            DialogSettings.ColorScheme = MetroDialogColorScheme.Accented;

            CloseCommand = new DelegateCommand(Close, CanClose);
            InitializeComponent();
            
            DataContext = this;
        }

        public string Text
        {
            get { return _text + _lastMessage; }
        }

        public bool IsOperationFinished
        {
            get { return _isOperationFinished; }
            private set
            {
                _isOperationFinished = value;
                CloseCommand.RaiseCanExecuteChanged();
                NotifyPropertyChanged("IsOperationFinished");
            }
        }

        private void WriteMessage(string text)
        {
            if (!text.StartsWith("\r"))
            {
                // Commit last message
                if (!string.IsNullOrEmpty(_lastMessage))
                    _text += _lastMessage;
            }
            _lastMessage = text;
            NotifyPropertyChanged("Text");
        }

        private bool CanClose()
        {
            return IsOperationFinished;
        }

        private void Close()
        {
            MainWindow.Instance.HideMetroDialogAsync(this);
        }

        private void OperationStarted(string title)
        {
            Title = title;
            IsOperationFinished = false;
            _text = _lastMessage = null;
            NotifyPropertyChanged("Text");

            MainWindow.Instance.ShowMetroDialogAsync(this);
        }

        private void OperationFinished()
        {
            IsOperationFinished = true;
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
