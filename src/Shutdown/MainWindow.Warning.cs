using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using Shutdown.Core;
using Shutdown.Ui;

namespace Shutdown
{
    public partial class MainWindow
    {
        static readonly TimeSpan LastMinuteThreshold = TaskbarState.LastMinute;

        internal static Action<Window> Flash = WindowFlash.Flash;
        internal static Action<Window> ShowNoActivate = ShowWithoutFocus;

        void WarnLastMinute()
        {
            var pending = _pending;
            if (pending == null) return;
            // A Windows-timed card can outlive its target (aborted elsewhere): stop staying on top once it is due.
            if (pending.WarnedLastMinute && Topmost && Uptime() >= pending.Due) { Topmost = false; return; }
            if (pending.WarnedLastMinute || pending.Due - Uptime() > LastMinuteThreshold) return;

            pending.WarnedLastMinute = true;
            if (WindowState == WindowState.Minimized || IsInTray)
                RestoreWithoutFocus();
            Topmost = true;
            Flash(this);

            AutomationProperties.SetName(PendingClock, Loc.I["LastMinute"]);
            AnnounceChange(PendingClock);
        }

        void ResetLastMinuteWarning()
        {
            if (_pending?.WarnedLastMinute != true) return;
            _pending.WarnedLastMinute = false;
            Topmost = false;
        }

        const int SwShowNoActivate = 4;

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ShowWindow(IntPtr hwnd, int command);

        internal static void ShowWithoutFocus(Window window)
        {
            if (!window.IsVisible)
            {
                bool showActivated = window.ShowActivated;
                window.ShowActivated = false;
                try { window.Show(); }
                finally { window.ShowActivated = showActivated; }
            }

            // Restore a minimized window without the activation that a WPF state change can cause.
            if (window.WindowState == WindowState.Minimized)
                ShowWindow(new WindowInteropHelper(window).Handle, SwShowNoActivate);
        }
    }
}
