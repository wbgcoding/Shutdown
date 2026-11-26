using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Shutdown.Ui
{
    internal static class WindowFlash
    {
        const uint FlashAll = 0x00000003;
        const uint FlashTimerNoForeground = 0x0000000C;

        [StructLayout(LayoutKind.Sequential)]
        struct FlashInfo
        {
            public uint Size;
            public IntPtr Hwnd;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FlashWindowEx(ref FlashInfo info);

        internal static void Flash(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Hwnd = handle,
                Flags = FlashAll | FlashTimerNoForeground,
                Count = uint.MaxValue
            };
            FlashWindowEx(ref info);
        }
    }
}
