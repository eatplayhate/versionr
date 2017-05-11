using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace VersionrUI
{
    internal class MultilineStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            IEnumerable<string> input = value as IEnumerable<string>;
            if (input != null)
                return String.Join(Environment.NewLine, input);
            else
                return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string input = value as string;
            if (input != null)
                return input.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            else
                return value;
        }
    }
}
