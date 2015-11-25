using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VersionrUI.Commands
{
    public class ControlDoubleClick : DependencyObject
    {
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.RegisterAttached("Command", typeof(ICommand), typeof(ControlDoubleClick), new PropertyMetadata(OnChangedCommand));
        public static readonly DependencyProperty ParameterProperty =
            DependencyProperty.RegisterAttached("Parameter", typeof(object), typeof(ControlDoubleClick));

        public static ICommand GetCommand(Control target)
        {
            return (ICommand)target.GetValue(CommandProperty);
        }

        public static void SetCommand(Control target, ICommand value)
        {
            target.SetValue(CommandProperty, value);
        }

        public static object GetParameter(Control target)
        {
            return target.GetValue(ParameterProperty);
        }

        public static void SetParameter(Control target, object value)
        {
            target.SetValue(ParameterProperty, value);
        }

        private static void OnChangedCommand(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            Control control = d as Control;
            control.PreviewMouseDoubleClick += new MouseButtonEventHandler(Element_PreviewMouseDoubleClick);
        }

        private static void Element_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Control control = sender as Control;
            ICommand command = GetCommand(control);
            object parameter = GetParameter(control);

            if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
                e.Handled = true;
            }
        }
    }
}
