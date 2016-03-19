namespace Versionr
{
    public interface ICheckoutOrderable
    {
        bool IsDirective { get; }
        bool IsDirectory { get; }
        string CanonicalName { get; }
        bool IsFile { get; }
        bool IsSymlink { get; }
    }
}
