using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using Shutdown.Core;
using Shutdown.Ui;

namespace Shutdown
{
    public partial class MainWindow
    {
        static readonly TimeSpan LastMinuteThreshold = TimeSpan.FromMinutes(1);

        internal static Action<Window> Flash = WindowFlash.Flash;
        internal static Action<Window> ShowNoActivate = ShowWithoutFocus;

        internal bool IsInTray { get; set; }

        void WarnLastMinute()
        {
            var pending = _pending;
            if (pending == null || pending.WarnedLastMinute || pending.Due - Uptime() > LastMinuteThreshold) return;

            pending.WarnedLastMinute = true;
            if (WindowState == WindowState.Minimized || IsInTray) ShowNoActivate(this);
            Topmost = true;
            Flash(this);

            AutomationProperties.SetName(PendingClock, Loc.I["LastMinute"]);
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(PendingClock)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
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

        static void ShowWithoutFocus(Window window)
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
