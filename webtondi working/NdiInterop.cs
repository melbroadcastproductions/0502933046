using System;
using System.Runtime.InteropServices;

namespace WebToNdi
{
    /// <summary>
    /// Minimal P/Invoke surface for the NDI SDK.
    /// NOTE: the DLL name below matches the NDI Advanced SDK
    /// (Processing_NDI_Lib_Advanced_x64.dll). If you install the standard
    /// NDI SDK/Runtime instead, change this back to "Processing.NDI.Lib.x64.dll".
    /// Requires the matching SDK/Runtime to be installed on this machine,
    /// and this process to be built/run as x64 to match the DLL's bitness.
    /// </summary>
    internal static class NdiInterop
    {
        private const string NdiDll = "Processing_NDI_Lib_Advanced_x64.dll";

        [StructLayout(LayoutKind.Sequential)]
        public struct SendCreate
        {
            public IntPtr NdiName;   // UTF-8, null-terminated
            public IntPtr Groups;    // UTF-8, null-terminated, or IntPtr.Zero
            [MarshalAs(UnmanagedType.I1)] public bool ClockVideo;
            [MarshalAs(UnmanagedType.I1)] public bool ClockAudio;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VideoFrameV2
        {
            public int Xres;
            public int Yres;
            public int FourCC;
            public int FrameRateN;
            public int FrameRateD;
            public float PictureAspectRatio;
            public int FrameFormatType;
            public long Timecode;
            public IntPtr PData;
            public int LineStrideInBytes; // union with data_size_in_bytes in the SDK; unused for uncompressed BGRA
            public IntPtr PMetadata;
            public long Timestamp;
        }

        public const int FrameFormatProgressive = 1;

        public static int FourCcBgra => MakeFourCc('B', 'G', 'R', 'A');

        private static int MakeFourCc(char a, char b, char c, char d) =>
            a | (b << 8) | (c << 16) | (d << 24);

        [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool NDIlib_initialize();

        [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_destroy();

        [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr NDIlib_send_create(ref SendCreate createSettings);

        [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_send_destroy(IntPtr instance);

        [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_send_send_video_v2(IntPtr instance, ref VideoFrameV2 videoData);
    }
}
