using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
    public class FSHandle
    {
        public FSHandle(string path)
        {
            _path = path;
            _fullPath = FileSystem.NormalizePath(path);
            FileSystem.ReadData(_fullPath, out _timeCreated, out _timeAccessed, out _timeWritten, out _size);
            _attribs = FileSystem.GetAttributes(_fullPath);
        }

        

        public string Path => _path;
        public string FullPath => _fullPath;
        public bool Exists => _attribs != INVALID_FILE_HANDLE;
        public bool IsDirectory => (_attribs & (UInt32)FileAttributes.Directory) != 0;
        public bool HasReparsePoint => (_attribs & (UInt32)FileAttributes.ReparsePoint) != 0;
        public DateTime CreationTimeUtc => _timeCreated;
        public DateTime LastAccessedTimeUtf => _timeAccessed;
        public DateTime LastWriteTime => _timeWritten;

        private string _path;
        private string _fullPath;
        private UInt32 _attribs = 0;
        private DateTime _timeCreated;
        private DateTime _timeAccessed;
        private DateTime _timeWritten;
        private ulong _size;

        private static UInt32 INVALID_FILE_HANDLE = 0xffffffff;
    }


    internal abstract class FileSystemImpl
    {
        public abstract string NormalizePath(string path);
        public abstract bool Exists(string path);
        public abstract UInt32 GetAttributes(string path);
        public abstract void ReadData(string path, out DateTime created, out DateTime accessed, out DateTime written, out UInt64 size);
        public abstract void CreateDirectory(string path);
    }

    internal class FileSystemDotNet : FileSystemImpl
    {
        public override string NormalizePath(string path)
        {
            return System.IO.Path.GetFullPath(path);
        }

        public override bool Exists(string path)
        {
            return new DirectoryInfo(path).Exists;
        }

        public override UInt32 GetAttributes(string path)
        {
            return (UInt32)new DirectoryInfo(path).Attributes;
        }

        public override void ReadData(string path, out DateTime created, out DateTime accessed, out DateTime written, out UInt64 size)
        {
            var d = new System.IO.DirectoryInfo(path);
            var f = new System.IO.FileInfo(path);
            FileSystemInfo fi = d.Exists ? (FileSystemInfo)d : (FileSystemInfo)f;
            created = fi.CreationTimeUtc;
            accessed = fi.LastAccessTimeUtc;
            written = fi.LastWriteTimeUtc;
            size = (fi is DirectoryInfo) ? 0 : (ulong)f.Length;
        }

        public override void CreateDirectory(string path)
        {
            new DirectoryInfo(path).Create();
        }
    }

    internal class FileSystemWin32 : FileSystemImpl
    {
        private delegate void GetTimeDataDelegate(string p, out DateTime c, out DateTime a, out DateTime w, out UInt64 size);

        private Func<string, string> _getFullPath = null;
        private Func<string, bool> _exists = null;
        private Func<string, UInt32> _getAttributes = null;
        GetTimeDataDelegate _getTimeData;

        private Action<string> _createDirectory = null;
        public FileSystemWin32()
        {
            var asm = System.Reflection.Assembly.LoadFrom(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "/x64/VersionrCore.Win32.dll");
            var fs = asm.GetType("Versionr.Win32.FileSystem");
            _getFullPath = Delegize<string, string>(fs.GetMethod("GetFullPath"));
            _exists = Delegize<string, bool>(fs.GetMethod("Exists"));
            _getAttributes = Delegize<string, UInt32>(fs.GetMethod("GetAttributes"));
            _getTimeData = (GetTimeDataDelegate)fs.GetMethod("ReadData").CreateDelegate(typeof(GetTimeDataDelegate));
            _createDirectory = Actionize<string>(fs.GetMethod("CreateDirectory"));
        }

        public override string NormalizePath(string path)
        {
            return _getFullPath(path);
        }

        public override bool Exists(string path)
        {
            return _exists(path);
        }

        public override uint GetAttributes(string path)
        {
            return _getAttributes(path);
        }

        public override void ReadData(string path, out DateTime created, out DateTime accessed, out DateTime written, out ulong size)
        {
            _getTimeData(path, out created, out accessed, out written, out size);
        }

        public override void CreateDirectory(string path)
        {
            _createDirectory(path);
        }

        

        private Func<R> Delegize<R>(MethodInfo m) { return m.CreateDelegate(typeof(Func<R>)) as Func<R>; }
        private Func<A, R> Delegize<A, R>(MethodInfo m) { return m.CreateDelegate(typeof(Func<A, R>)) as Func<A, R>; }
        private Action Actionize(MethodInfo m) { return m.CreateDelegate(typeof(Action)) as Action; }
        private Action<A> Actionize<A>(MethodInfo m) { return m.CreateDelegate(typeof(Action<A>)) as Action<A>; }
    }

    static class FileSystem
    {
        static FileSystemImpl _impl = null;

        static FileSystem()
        {
            if (Utilities.MultiArchPInvoke.IsRunningOnMono)
            {
                _impl = new FileSystemDotNet();
            }
            else
            {
                _impl = new FileSystemWin32();
            }
        }

        public static string NormalizePath(string path)
        {
            return _impl.NormalizePath(path);
        }

        public static UInt32 GetAttributes(string path)
        {
            return _impl.GetAttributes(path);
        }

        public static void CreateDirectory(string path)
        {
            _impl.CreateDirectory(path);
        }

        internal static void ReadData(string path, out DateTime created, out DateTime accessed, out DateTime written, out ulong size)
        {
            _impl.ReadData(path, out created, out accessed, out written, out size);
        }
    }
}
