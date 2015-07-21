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
		public static bool Exists(string v)
		{
			FileInfo info = new FileInfo(v);
			return info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint);
		}

		public static bool Create(string path, string target)
		{
			bool asDirectory = Directory.Exists(target);
			if (!asDirectory && !File.Exists(target))
			{
				// Don't know what kind of link to make in this case.
				return false;
			}

			if (MultiArchPInvoke.IsRunningOnMono)
				return CreateSymlinkInternalMono(path, target, asDirectory);
			else
				return CreateSymlinkInternalWin32(path, target, asDirectory);
		}


		[DllImport("kernel32.dll", SetLastError = true)]
		static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

		private static bool CreateSymlinkInternalWin32(string path, string target, bool asDirectory)
		{
			if (!CreateSymbolicLink(path, target, asDirectory ? targetIsADirectory : targetIsAFile) || Marshal.GetLastWin32Error() != 0)
			{
				try
				{
					Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
				}
				catch (COMException e)
				{
					return CreateSymlinkInternalWin32Fallback(path, target, asDirectory);
				}
				return false;
			}
			return true;
		}

		private static bool CreateSymlinkInternalWin32Fallback(string path, string target, bool asDirectory)
		{
			// launch mklink as administrator
			System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
			{
				FileName = "cmd.exe",
				Arguments = string.Format("/c mklink \"{0}\" \"{1}\"", path, target),
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
				Printer.PrintError("Administrator priviledges are required to create symlinks. Either runas administrator, or accept priveledges when prompted");
				return false;
			}
		}

		private static bool CreateSymlinkInternalMono(string path, string target, bool asDirectory)
		{
			Printer.PrintMessage("CreateSymlinkInternalMono({0}, {1}, {2}", path, target, asDirectory);
			//var link = new Mono.Unix.UnixSymbolicLinkInfo(path);
			//link.CreateSymbolicLinkTo(target);

			Assembly.Load("mono/4.5/Mono.Posix.dll");

			var unixSymbolicLinkInfoType = Type.GetType("Mono.Unix.UnixSymbolicLinkInfo");
			if (unixSymbolicLinkInfoType == null)
				return false;

			Printer.PrintMessage("Got UnixSymbolicLinkInfo type");

			MethodInfo createSymbolicLinkMethod = unixSymbolicLinkInfoType.GetMethod("CreateSymbolicLinkTo");
			if (createSymbolicLinkMethod == null)
				return false;

			Printer.PrintMessage("Got CreateSymbolicLinkTo method");

			object link = Activator.CreateInstance(unixSymbolicLinkInfoType, new object[] { path });
			createSymbolicLinkMethod.Invoke(link, new object[] { target });

			return true;
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
			if (!Exists(v))
				return null;

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

			return target;
		}
	}
}
