namespace VersionrUI.ViewModels
{
    public class CustomTagVM
    {
        public CustomTagVM(string tag)
        {
            Tag = tag;
        }

        public string Tag { get; private set; }
    }
}
