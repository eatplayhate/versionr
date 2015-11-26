using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    public enum Platform
    {
        Windows,
        Linux,
        Mac
    }
    public static class MultiArchPInvoke
    {
        static Platform? m_Platform;
        public static Platform RunningPlatform
        {
            get
            {
                if (!m_Platform.HasValue)
                {
                    switch (Environment.OSVersion.Platform)
                    {
                        case PlatformID.Unix:
                            // Well, there are chances MacOSX is reported as Unix instead of MacOSX.
                            // Instead of platform check, we'll do a feature checks (Mac specific root folders)
                            if (System.IO.Directory.Exists("/Applications")
                                & System.IO.Directory.Exists("/System")
                                & System.IO.Directory.Exists("/Users")
                                & System.IO.Directory.Exists("/Volumes"))
                                m_Platform = Platform.Mac;
                            else
                                m_Platform = Platform.Linux;
                            break;
                        case PlatformID.MacOSX:
                            m_Platform = Platform.Mac;
                            break;
                        default:
                            m_Platform = Platform.Windows;
                            break;
                    }
                }
                return m_Platform.Value;
            }
        }
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
