using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WebToNdi
{
    internal sealed class NdiSender : IDisposable
    {
        private readonly IntPtr _sendInstance;
        private bool _disposed;

        public NdiSender(string sourceName)
        {
            if (!NdiInterop.NDIlib_initialize())
                throw new InvalidOperationException(
                    "NDIlib_initialize failed. Is the NDI Runtime installed, and is this " +
                    "process built/running as x64?");

            var nameBytes = Encoding.UTF8.GetBytes(sourceName + "\0");
            var namePtr = Marshal.AllocHGlobal(nameBytes.Length);
            Marshal.Copy(nameBytes, 0, namePtr, nameBytes.Length);

            var create = new NdiInterop.SendCreate
            {
                NdiName = namePtr,
                Groups = IntPtr.Zero,
                ClockVideo = true,
                ClockAudio = false
            };

            _sendInstance = NdiInterop.NDIlib_send_create(ref create);
            Marshal.FreeHGlobal(namePtr);

            if (_sendInstance == IntPtr.Zero)
                throw new InvalidOperationException("NDIlib_send_create returned null.");
        }

        /// <summary>
        /// Sends one BGRA frame. <paramref name="bgra"/> must be tightly packed
        /// (stride == width * 4 bytes).
        /// </summary>
        public void SendBgraFrame(byte[] bgra, int width, int height, int fpsN, int fpsD)
        {
            var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                var frame = new NdiInterop.VideoFrameV2
                {
                    Xres = width,
                    Yres = height,
                    FourCC = NdiInterop.FourCcBgra,
                    FrameRateN = fpsN,
                    FrameRateD = fpsD,
                    PictureAspectRatio = width / (float)height,
                    FrameFormatType = NdiInterop.FrameFormatProgressive,
                    Timecode = long.MinValue, // let NDI synthesize the timecode
                    PData = handle.AddrOfPinnedObject(),
                    LineStrideInBytes = width * 4,
                    PMetadata = IntPtr.Zero,
                    Timestamp = long.MinValue
                };

                NdiInterop.NDIlib_send_send_video_v2(_sendInstance, ref frame);
            }
            finally
            {
                handle.Free();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_sendInstance != IntPtr.Zero)
                NdiInterop.NDIlib_send_destroy(_sendInstance);
            NdiInterop.NDIlib_destroy();
        }
    }
}
