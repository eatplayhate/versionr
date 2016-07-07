using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    public static class Misc
    {
        static Random s_Rand = new Random();
        public static string FormatSizeFriendly(long size)
        {
            if (size < 1024)
                return string.Format("{0} bytes", size);
            if (size < 1024 * 1024)
                return string.Format("{0:N2} KiB", size / 1024.0);
            if (size < 1024 * 1024 * 1024)
                return string.Format("{0:N2} MiB", size / (1024.0 * 1024.0));
            if (size < 1024L * 1024 * 1024 * 1024)
                return string.Format("{0:N2} GiB", size / (1024.0 * 1024.0 * 1024.0));
            return string.Format("{0:N2} TiB", size / (1024.0 * 1024.0 * 1024.0 * 1024.0));
        }
        public static long RandomLongNonZero()
        {
            long result = 0;
            while (result == 0)
            {
                byte[] temp = new byte[8];
                lock (s_Rand)
                    s_Rand.NextBytes(temp);
                result = BitConverter.ToInt64(temp, 0);
            }
            return result;
        }
    }
}
