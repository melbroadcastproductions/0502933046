using System;
using System.Runtime.InteropServices;

namespace NdiWorker
{
    public static class NdiNative
    {
        private const string LibNameLinux = "libndi.so.5";
        private const string LibNameWin = "Processing.NDI.Lib.x64.dll";

        static NdiNative()
        {
            NativeLibrary.SetDllImportResolver(typeof(NdiNative).Assembly, ResolveNdiLibrary);
        }

        private static IntPtr ResolveNdiLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == LibNameLinux || libraryName == LibNameWin || libraryName.Contains("ndi"))
            {
                string[] candidates = OperatingSystem.IsWindows()
                    ? new[] { "Processing.NDI.Lib.x64.dll", "Processing.NDI.Lib.x86.dll", "Processing.NDI.Lib.dll" }
                    : new[] { "libndi.so.5", "libndi.so.4", "libndi.so", "/usr/lib/libndi.so.5", "/usr/local/lib/libndi.so.5" };

                foreach (var candidate in candidates)
                {
                    if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                    {
                        return handle;
                    }
                }
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NDIlib_send_create_t
        {
            [MarshalAs(UnmanagedType.LPUTF8Str)]
            public string p_ndi_name;
            [MarshalAs(UnmanagedType.LPUTF8Str)]
            public string p_groups;
            [MarshalAs(UnmanagedType.U1)]
            public bool clock_video;
            [MarshalAs(UnmanagedType.U1)]
            public bool clock_audio;
        }

        public enum NDIlib_FourCC_video_type_e : uint
        {
            NDIlib_FourCC_video_type_UYVY = 0x59565955,
            NDIlib_FourCC_video_type_BGRA = 0x41524742,
            NDIlib_FourCC_video_type_BGRX = 0x58524742,
            NDIlib_FourCC_video_type_RGBA = 0x41474252,
            NDIlib_FourCC_video_type_RGBX = 0x58474252
        }

        public enum NDIlib_frame_format_type_e : int
        {
            NDIlib_frame_format_type_progressive = 1,
            NDIlib_frame_format_type_interleaved = 0,
            NDIlib_frame_format_type_field_0 = 2,
            NDIlib_frame_format_type_field_1 = 3
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NDIlib_video_frame_v2_t
        {
            public int xres;
            public int yres;
            public NDIlib_FourCC_video_type_e FourCC;
            public int frame_rate_N;
            public int frame_rate_D;
            public float picture_aspect_ratio;
            public NDIlib_frame_format_type_e frame_format_type;
            public long timecode;
            public IntPtr p_data;
            public int line_stride_in_bytes;
            public IntPtr p_metadata;
            public long timestamp;
        }

        [DllImport(LibNameLinux, EntryPoint = "NDIlib_initialize")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_initialize_linux();

        [DllImport(LibNameWin, EntryPoint = "NDIlib_initialize")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_initialize_win();

        public static bool NDIlib_initialize()
        {
            try
            {
                return OperatingSystem.IsWindows() ? NDIlib_initialize_win() : NDIlib_initialize_linux();
            }
            catch
            {
                return false;
            }
        }

        [DllImport(LibNameLinux, EntryPoint = "NDIlib_send_create")]
        private static extern IntPtr NDIlib_send_create_linux(ref NDIlib_send_create_t p_create_settings);

        [DllImport(LibNameWin, EntryPoint = "NDIlib_send_create")]
        private static extern IntPtr NDIlib_send_create_win(ref NDIlib_send_create_t p_create_settings);

        public static IntPtr NDIlib_send_create(ref NDIlib_send_create_t settings)
        {
            try
            {
                return OperatingSystem.IsWindows() ? NDIlib_send_create_win(ref settings) : NDIlib_send_create_linux(ref settings);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        [DllImport(LibNameLinux, EntryPoint = "NDIlib_send_send_video_v2")]
        private static extern void NDIlib_send_send_video_v2_linux(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data);

        [DllImport(LibNameWin, EntryPoint = "NDIlib_send_send_video_v2")]
        private static extern void NDIlib_send_send_video_v2_win(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data);

        public static void NDIlib_send_send_video_v2(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data)
        {
            try
            {
                if (OperatingSystem.IsWindows()) NDIlib_send_send_video_v2_win(p_instance, ref p_video_data);
                else NDIlib_send_send_video_v2_linux(p_instance, ref p_video_data);
            }
            catch { }
        }

        [DllImport(LibNameLinux, EntryPoint = "NDIlib_send_destroy")]
        private static extern void NDIlib_send_destroy_linux(IntPtr p_instance);

        [DllImport(LibNameWin, EntryPoint = "NDIlib_send_destroy")]
        private static extern void NDIlib_send_destroy_win(IntPtr p_instance);

        public static void NDIlib_send_destroy(IntPtr p_instance)
        {
            try
            {
                if (OperatingSystem.IsWindows()) NDIlib_send_destroy_win(p_instance);
                else NDIlib_send_destroy_linux(p_instance);
            }
            catch { }
        }
    }
}
