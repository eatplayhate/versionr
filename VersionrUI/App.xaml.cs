using System.Windows;
using Versionr.Utilities;

namespace VersionrUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            MultiArchPInvoke.BindDLLs();
        }
    }
}
