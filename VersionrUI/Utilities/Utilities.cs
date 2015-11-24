using System.IO;

namespace VersionrUI
{
    internal class Utilities
    {
        public static bool IsVersionrPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            if (directoryInfo != null)
            {
                bool loaded = false;

                using (var ws = Versionr.Area.Load(directoryInfo))
                {
                    loaded = ws != null;
                }

                return loaded;
            }
            return false;
        }
    }
}
