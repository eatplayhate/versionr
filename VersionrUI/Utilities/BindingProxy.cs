using System.Windows;

namespace VersionrUI
{
    public class BindingProxy : Freezable
    {

        #region Freezable Members

        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }

        #endregion

        /// <summary>
        /// Using a DependencyProperty as the backing store for Data.
        /// This enables animation, styling, binding, etc...
        /// </summary>
        public object Data
        {
            get { return (object)GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
    }
}
