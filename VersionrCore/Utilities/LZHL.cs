using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Versionr.Utilities
{
    public class LZHL
    {
        [DllImport("lzhl", EntryPoint = "CreateCompressor", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CreateCompressor();
        [DllImport("lzhl", EntryPoint = "DestroyCompressor", CallingConvention = CallingConvention.Cdecl)]
        public static extern void DestroyCompressor(IntPtr compressor);
        [DllImport("lzhl", EntryPoint = "CreateDecompressor", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CreateDecompressor();
        [DllImport("lzhl", EntryPoint = "DestroyDecompressor", CallingConvention = CallingConvention.Cdecl)]
        public static extern void DestroyDecompressor(IntPtr decompressor);
        [DllImport("lzhl", EntryPoint = "ResetCompressor", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ResetCompressor(IntPtr compressor);
        [DllImport("lzhl", EntryPoint = "ResetDecompressor", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ResetDecompressor(IntPtr decompressor);

        [DllImport("lzhl", EntryPoint = "Compress", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint Compress(IntPtr compressor, byte[] buffer, uint bufferSize, byte[] result);
        [DllImport("lzhl", EntryPoint = "Decompress", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint Decompress(IntPtr decompressor, byte[] buffer, uint bufferSize, byte[] result, uint outputSize);
    }
}
