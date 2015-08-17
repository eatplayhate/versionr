using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;

namespace Versionr.Utilities
{
	public class Symlink
	{
		public static bool Exists(string path)
		{
			if (SvnIntegration.ApliesTo(path))
				return SvnIntegration.IsSymlink(path);

			path = path.EndsWith("/") ? path.Remove(path.Length - 1) : path;
			FileInfo file = new FileInfo(path);
			if (file.Exists)
				return file.Attributes.HasFlag(FileAttributes.ReparsePoint);
			DirectoryInfo dir = new DirectoryInfo(path);
			return dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        public static bool Exists(FileSystemInfo info, string hintpath = null)
        {
			if (SvnIntegration.ApliesTo(info, hintpath))
				return SvnIntegration.IsSymlink(info.FullName);

			if (info.Exists)
                return info.Attributes.HasFlag(FileAttributes.ReparsePoint);
            return false;
        }

        public static void Delete(string path)
		{
			if (!Exists(path))
				return;

			if (SvnIntegration.ApliesTo(path))
			{
				SvnIntegration.DeleteSymlink(path);
				return;
			}

			if (MultiArchPInvoke.IsRunningOnMono)
				SymlinkMono.Delete(path);
			else
				SymlinkWin32.Delete(path);
		}

		public static bool Create(string path, string target, bool clearExisting = false)
		{
			if (clearExisting)
			{
				try
				{
					if (Exists(path))
						Delete(path);
					else if (File.Exists(path))
						File.Delete(path);
					else if (Directory.Exists(path))
						Directory.Delete(path);
				}
				catch
				{
					Printer.PrintError("Could not create symlink {0}, it is obstructed!", path);
					return false;
				}
			}

			if (SvnIntegration.ApliesTo(path))
				return SvnIntegration.CreateSymlink(path, target);

			if (MultiArchPInvoke.IsRunningOnMono)
				return SymlinkMono.CreateSymlink(path, target);
			else
				return SymlinkWin32.CreateSymlink(path, target);
		}

		public static string GetTarget(string path)
		{
			if (!Exists(path))
				return null;

			if (SvnIntegration.ApliesTo(path))
				return SvnIntegration.GetSymlinkTarget(path);

			if (MultiArchPInvoke.IsRunningOnMono)
				return SymlinkMono.GetTarget(path);
			else
				return SymlinkWin32.GetTarget(path);
		}

		public class TargetNotFoundException : Exception
		{
		}

		private class SymlinkWin32
		{
			[DllImport("kernel32.dll", SetLastError = true)]
			static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

			public static bool CreateSymlink(string path, string target)
			{
				// Work out what the symlink is pointing to. Is it a file or directory?
				string targetPath = Path.Combine(Path.GetDirectoryName(path), target);
				bool asDirectory = Directory.Exists(targetPath);
				if (!asDirectory && !File.Exists(targetPath))
				{
					throw new TargetNotFoundException();
                }

				target = target.Replace('/', '\\');

				if (!CreateSymbolicLink(path, target, asDirectory ? targetIsADirectory : targetIsAFile) || Marshal.GetLastWin32Error() != 0)
				{
					try
					{
						Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
					}
					catch (COMException e)
					{
						return CreateSymlinkFallback(path, target, asDirectory);
					}
					return false;
				}
				return true;
			}

			private static bool CreateSymlinkFallback(string path, string target, bool asDirectory)
			{
				// launch mklink as administrator
				System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
				{
					FileName = "cmd.exe",
					Arguments = string.Format("/c mklink {0} \"{1}\" \"{2}\"", asDirectory ? "/D" : "", path, target),
					WorkingDirectory = Environment.CurrentDirectory,
					UseShellExecute = true,
					Verb = "runas",
					CreateNoWindow = true
				};

				try
				{
					var proc = System.Diagnostics.Process.Start(psi);
					proc.WaitForExit();
					return (proc.ExitCode == 0);
				}
				catch
				{
					// The user refused to allow privileges elevation.
					// Do nothing and return directly ...
					Printer.PrintError("Administrator priviledges are required to create symlinks. Either run as administrator, or accept priveledges when prompted");
					return false;
				}
			}

			[DllImport("kernel32.dll", SetLastError = true)]
			private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
			string lpFileName,
			uint dwDesiredAccess,
			uint dwShareMode,
			IntPtr lpSecurityAttributes,
			uint dwCreationDisposition,
			uint dwFlagsAndAttributes,
			IntPtr hTemplateFile);

			private const uint genericReadAccess = 0x80000000;
			private const uint fileFlagsForOpenReparsePointAndBackupSemantics = 0x02200000;
			private const int ioctlCommandGetReparsePoint = 0x000900A8;
			private const uint openExisting = 0x3;
			private const uint pathNotAReparsePointError = 0x80071126;
			private const uint shareModeAll = 0x7; // Read, Write, Delete
			private const uint symLinkTag = 0xA000000C;
			private const int targetIsAFile = 0;
			private const int targetIsADirectory = 1;

			[StructLayout(LayoutKind.Sequential)]
			private struct SymbolicLinkReparseData
			{
				// Not certain about this!
				private const int maxUnicodePathLength = 260 * 2;

				public uint ReparseTag;
				public ushort ReparseDataLength;
				public ushort Reserved;
				public ushort SubstituteNameOffset;
				public ushort SubstituteNameLength;
				public ushort PrintNameOffset;
				public ushort PrintNameLength;
				public uint Flags;
				[MarshalAs(UnmanagedType.ByValArray, SizeConst = maxUnicodePathLength)]
				public byte[] PathBuffer;
			}

			[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
			private static extern bool DeviceIoControl(
				IntPtr hDevice,
				uint dwIoControlCode,
				IntPtr lpInBuffer,
				int nInBufferSize,
				IntPtr lpOutBuffer,
				int nOutBufferSize,
				out int lpBytesReturned,
				IntPtr lpOverlapped);

			public static string GetTarget(string v)
			{
				SymbolicLinkReparseData reparseDataBuffer;

				using (var filehandle = CreateFile(v, genericReadAccess, shareModeAll, IntPtr.Zero, openExisting,
						fileFlagsForOpenReparsePointAndBackupSemantics, IntPtr.Zero))
				{
					if (filehandle.IsInvalid)
						return null;

					int outBufferSize = Marshal.SizeOf(typeof(SymbolicLinkReparseData));
					IntPtr outBuffer = IntPtr.Zero;
					try
					{
						outBuffer = Marshal.AllocHGlobal(outBufferSize);
						int bytesReturned;
						bool success = DeviceIoControl(
							filehandle.DangerousGetHandle(), ioctlCommandGetReparsePoint, IntPtr.Zero, 0,
							outBuffer, outBufferSize, out bytesReturned, IntPtr.Zero);

						filehandle.Close();

						if (!success)
						{
							if (((uint)Marshal.GetHRForLastWin32Error()) == pathNotAReparsePointError)
								return null;

							Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
						}

						reparseDataBuffer = (SymbolicLinkReparseData)Marshal.PtrToStructure(
							outBuffer, typeof(SymbolicLinkReparseData));
					}
					finally
					{
						Marshal.FreeHGlobal(outBuffer);
					}
				}

				if (reparseDataBuffer.ReparseTag != symLinkTag)
					return null;

				string target = Encoding.Unicode.GetString(reparseDataBuffer.PathBuffer,
					reparseDataBuffer.PrintNameOffset, reparseDataBuffer.PrintNameLength);

				target = target.Replace('\\', '/');

				return target;
			}

			public static void Delete(string path)
			{
				if (File.Exists(path))
					File.Delete(path);
				else if (Directory.Exists(path))
					Directory.Delete(path);
			}

		}

		private class SymlinkMono
		{
			private static Type MonoType { get; set; }
			static SymlinkMono()
			{
				Assembly.Load("mono/4.5/Mono.Posix.dll");
				foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
				{
					MonoType = a.GetType("Mono.Unix.UnixSymbolicLinkInfo");
					if (MonoType != null)
						break;
				}
			}

			private string Path { get; set; }
			private object MonoObj { get; set; }
			private SymlinkMono(string path)
			{
				Path = path;
				MonoObj = Activator.CreateInstance(MonoType, new object[] { Path });
			}

			public static bool CreateSymlink(string path, string target)
			{
				target = target.Replace('\\', '/');

				//var link = new Mono.Unix.UnixSymbolicLinkInfo(path);
				var link = new SymlinkMono(path);

				//link.CreateSymbolicLinkTo(target);
				MethodInfo method = MonoType.GetMethod("CreateSymbolicLinkTo", new Type[] { typeof(string) });
				method.Invoke(link.MonoObj, new object[] { target });

				// return link.HasContents;
				return (bool)MonoType.GetProperty("HasContents").GetValue(link.MonoObj);
			}

			public static string GetTarget(string path)
			{
				//var link = new Mono.Unix.UnixSymbolicLinkInfo(path);
				var link = new SymlinkMono(path);

				//return link.ContentsPath;
				return (string)MonoType.GetProperty("ContentsPath").GetValue(link.MonoObj);
			}

			public static void Delete(string path)
			{
				//var link = new Mono.Unix.UnixSymbolicLinkInfo(path);
				var link = new SymlinkMono(path);

				// link.Delete();
				MethodInfo method = MonoType.GetMethod("Delete");
				method.Invoke(link.MonoObj, null);
			}

		}


	}
}
