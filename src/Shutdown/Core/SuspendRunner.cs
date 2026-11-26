using System;
using System.Runtime.InteropServices;

namespace Shutdown.Core
{
    internal interface ISuspendRunner
    {
        // Returns a resource key on failure or null on success.
        string Run();
    }

    internal sealed class SuspendRunner : ISuspendRunner
    {
        static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);
        const uint WmSysCommand = 0x0112;
        static readonly IntPtr ScMonitorPower = new IntPtr(0xf170);
        static readonly IntPtr MonitorOff = new IntPtr(2);
        const uint SmtoAbortIfHung = 0x0002;
        const uint DisplayOffTimeoutMs = 2000;

        // SYSTEM_POWER_CAPABILITIES uses one-byte fields through AoAc.
        [StructLayout(LayoutKind.Explicit, Size = 76)]
        struct PowerCapabilities
        {
            [FieldOffset(20)] public byte AoAc;
        }

        [DllImport("powrprof.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool IsPwrSuspendAllowed();

        [DllImport("powrprof.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool GetPwrCapabilities(out PowerCapabilities capabilities);

        [DllImport("powrprof.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
            [MarshalAs(UnmanagedType.U1)] bool forceCritical, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out UIntPtr result);

        internal static bool BlockedForTests { get; set; }

        internal static bool IsSleepAllowed() => IsSleepAllowed(IsPwrSuspendAllowed, IsModernStandby);

        internal static bool IsSleepAllowed(Func<bool> suspendAllowed, Func<bool> modernStandby)
        {
            try { return suspendAllowed() || modernStandby(); } catch { return true; }
        }

        static bool IsModernStandby() => GetPwrCapabilities(out var capabilities) && capabilities.AoAc != 0;

        public string Run()
        {
            if (BlockedForTests) throw new InvalidOperationException("Sleep is blocked in tests.");
            bool modernStandby;
            try { modernStandby = IsModernStandby(); }
            catch (Exception ex)
            {
                Log.Write("power capabilities unavailable", ex);
                modernStandby = false;
            }
            return Run(modernStandby, () => SetSuspendState(false, false, false), TurnDisplayOff);
        }

        internal static string Run(bool modernStandby, Func<bool> suspend, Action turnDisplayOff)
        {
            if (!modernStandby)
            {
                if (suspend()) return null;
                Log.Write("SetSuspendState returned false. Turning off the display.");
            }
            turnDisplayOff();
            return modernStandby ? null : "ErrSleepFailed";
        }

        static void TurnDisplayOff() => SendMessageTimeout(HwndBroadcast, WmSysCommand, ScMonitorPower,
            MonitorOff, SmtoAbortIfHung, DisplayOffTimeoutMs, out _);
    }
}
