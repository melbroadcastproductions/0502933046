using System;
using System.Runtime.InteropServices;

namespace NdiWorker
{
    public static class NdiNative
    {
        private const string NdiLibName = "ndi";

        static NdiNative()
        {
            NativeLibrary.SetDllImportResolver(typeof(NdiNative).Assembly, ResolveNdiLibrary);
        }

        private static IntPtr ResolveNdiLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == NdiLibName || libraryName.Contains("ndi", StringComparison.OrdinalIgnoreCase))
            {
                var candidates = new System.Collections.Generic.List<string>();

                if (OperatingSystem.IsWindows())
                {
                    string appDir = AppDomain.CurrentDomain.BaseDirectory;
                    candidates.Add(System.IO.Path.Combine(appDir, "Processing_NDI_Lib_Advanced_x64.dll"));
                    candidates.Add(System.IO.Path.Combine(appDir, "Processing.NDI.Lib.x64.dll"));
                    candidates.Add(System.IO.Path.Combine(appDir, "Processing_NDI_Lib_Advanced_x86.dll"));
                    candidates.Add(System.IO.Path.Combine(appDir, "Processing.NDI.Lib.x86.dll"));
                    candidates.Add(System.IO.Path.Combine(appDir, "Processing.NDI.Lib.dll"));

                    string? ndiEnv = Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V5")
                                  ?? Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V4");
                    if (!string.IsNullOrEmpty(ndiEnv))
                    {
                        candidates.Add(System.IO.Path.Combine(ndiEnv, "Processing_NDI_Lib_Advanced_x64.dll"));
                        candidates.Add(System.IO.Path.Combine(ndiEnv, "Processing.NDI.Lib.x64.dll"));
                    }

                    candidates.Add(@"C:\Program Files\NDI\NDI 5 SDK\v5\lib\x64\Processing.NDI.Lib.x64.dll");
                    candidates.Add(@"C:\Program Files\NDI\NDI 5 Advanced SDK\v5\lib\x64\Processing_NDI_Lib_Advanced_x64.dll");
                    candidates.Add("Processing_NDI_Lib_Advanced_x64.dll");
                    candidates.Add("Processing.NDI.Lib.x64.dll");
                    candidates.Add("Processing.NDI.Lib.dll");
                }
                else
                {
                    candidates.Add("libndi.so.5");
                    candidates.Add("libndi.so.4");
                    candidates.Add("libndi.so");
                    candidates.Add("/usr/lib/libndi.so.5");
                    candidates.Add("/usr/local/lib/libndi.so.5");
                }

                foreach (var candidate in candidates)
                {
                    try
                    {
                        if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                        {
                            Console.WriteLine($"[NDI Native] SUCCESS: Successfully loaded native NDI library from '{candidate}'");
                            return handle;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[NDI Native] Load attempt for '{candidate}' threw exception: {ex.Message}");
                    }
                }

                Console.WriteLine($"[NDI Native] WARNING: Failed to load native NDI library from any candidate path. Ensure Processing_NDI_Lib_Advanced_x64.dll or Processing.NDI.Lib.x64.dll and Visual C++ Redistributable are installed.");
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NDIlib_send_create_t
        {
            public IntPtr p_ndi_name;
            public IntPtr p_groups;
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

        [DllImport(NdiLibName, EntryPoint = "NDIlib_initialize")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NDIlib_initialize_native();

        public static bool NDIlib_initialize()
        {
            try
            {
                return NDIlib_initialize_native();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NDI Native] Exception calling NDIlib_initialize: {ex.Message}");
                return false;
            }
        }

        [DllImport(NdiLibName, EntryPoint = "NDIlib_send_create")]
        private static extern IntPtr NDIlib_send_create_native(ref NDIlib_send_create_t p_create_settings);

        public static IntPtr NDIlib_send_create(ref NDIlib_send_create_t settings)
        {
            try
            {
                return NDIlib_send_create_native(ref settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NDI Native] Exception calling NDIlib_send_create: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        [DllImport(NdiLibName, EntryPoint = "NDIlib_send_send_video_v2")]
        private static extern void NDIlib_send_send_video_v2_native(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data);

        public static void NDIlib_send_send_video_v2(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data)
        {
            try
            {
                NDIlib_send_send_video_v2_native(p_instance, ref p_video_data);
            }
            catch { }
        }

        [DllImport(NdiLibName, EntryPoint = "NDIlib_send_destroy")]
        private static extern void NDIlib_send_destroy_native(IntPtr p_instance);

        public static void NDIlib_send_destroy(IntPtr p_instance)
        {
            try
            {
                NDIlib_send_destroy_native(p_instance);
            }
            catch { }
        }
    }
}
