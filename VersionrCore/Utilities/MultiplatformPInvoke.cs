using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    public static class MultiArchPInvoke
    {
        static bool? m_IsRunningOnMono;
        public static bool IsRunningOnMono
        {
            get
            {
                if (!m_IsRunningOnMono.HasValue)
                    m_IsRunningOnMono = Type.GetType("Mono.Runtime") != null;
                return m_IsRunningOnMono.Value;
            }
        }
        public static bool IsX64
        {
            get
            {
                return IntPtr.Size == 8;
            }
        }
        public static void BindDLLs()
        {
            if (!IsRunningOnMono)
                BindArchSpecific(new string[] { "sqlite3", "lzhamwrapper", "lzhl" });
        }

        private static void BindArchSpecific(string[] v)
        {
            string executingPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            if (Environment.Is64BitProcess)
                executingPath = System.IO.Path.Combine(executingPath, "x64");
            else
                executingPath = System.IO.Path.Combine(executingPath, "x86");
            string assemblyExt = ".dll";
            foreach (var assembly in v)
            {
                string fn = System.IO.Path.Combine(executingPath, assembly) + assemblyExt;
                if (System.IO.File.Exists(fn))
                    LoadLibrary(fn);
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern IntPtr LoadLibrary(string dllToLoad);
    }
}
