using System;
using System.Runtime.InteropServices;

namespace Shutdown.Core
{
    internal interface ISuspendRunner
    {
        void Run();
    }

    internal sealed class SuspendRunner : ISuspendRunner
    {
        static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);
        const uint WmSysCommand = 0x0112;
        static readonly IntPtr ScMonitorPower = new IntPtr(0xf170);
        static readonly IntPtr MonitorOff = new IntPtr(2);

        [DllImport("powrprof.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool IsPwrSuspendAllowed();

        [DllImport("powrprof.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
            [MarshalAs(UnmanagedType.U1)] bool forceCritical, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        internal static bool BlockedForTests { get; set; }

        public void Run()
        {
            if (BlockedForTests) throw new InvalidOperationException("Sleep is blocked in tests.");
            if (SetSuspendState(false, false, false)) return;

            // Turning the display off lets Modern Standby devices enter their idle state.
            Log.Write("SetSuspendState returned false. Turning off the display.");
            SendMessage(HwndBroadcast, WmSysCommand, ScMonitorPower, MonitorOff);
        }
    }
}
