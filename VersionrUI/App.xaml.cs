using System.Globalization;
using System.Windows;
using System.Windows.Markup;
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
            // From http://www.codeproject.com/Articles/31837/Creating-an-Internationalized-Wizard-in-WPF
            // Ensure the current culture passed into bindings 
            // is the OS culture. By default, WPF uses en-US 
            // as the culture, regardless of the system settings.
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

            base.OnStartup(e);

            MultiArchPInvoke.BindDLLs();
        }
    }
}
