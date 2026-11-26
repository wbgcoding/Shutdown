using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Shutdown
{
    public partial class MainWindow
    {
        // The native function returns a one-byte BOOLEAN.
        [DllImport("powrprof.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool IsPwrHibernateAllowed();

        internal static Func<bool> HibernateAllowed = () =>
        {
            try { return IsPwrHibernateAllowed(); } catch { return true; }
        };

        void OnHibernateInfoClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var tip = (ToolTip)button.ToolTip;
            tip.PlacementTarget = button;
            tip.IsOpen = true;
        }

        void OnHibernateInfoDismiss(object sender, RoutedEventArgs e) =>
            ((ToolTip)((Button)sender).ToolTip).IsOpen = false;

        void OnHibernateInfoKeyDown(object sender, KeyEventArgs e)
        {
            var tip = (ToolTip)((Button)sender).ToolTip;
            if (e.Key != Key.Escape || !tip.IsOpen) return;
            tip.IsOpen = false;
            e.Handled = true;
        }
    }
}
