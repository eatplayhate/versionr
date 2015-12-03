using System;
using System.ComponentModel;
using System.Windows;

namespace VersionrUI.ViewModels
{
    public class NotifyPropertyChangedBase : INotifyPropertyChanged
    {
        private bool _isLoading = false;

        public bool IsLoading
        {
            get { return _isLoading; }
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    NotifyPropertyChanged("IsLoading");
                    NotifyPropertyChanged("Visibility");
                }
            }
        }

        public Visibility Visibility
        {
            get { return IsLoading ? Visibility.Collapsed : Visibility.Visible; }
        }

        protected void Load(Action action)
        {
            IsLoading = true;

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler((obj, args) =>
            {
                action.Invoke();
                IsLoading = false;
            });
            worker.RunWorkerAsync();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(info));
        }
    }
}
