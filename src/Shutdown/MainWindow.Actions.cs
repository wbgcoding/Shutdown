using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shutdown.Core;

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

        internal static Func<bool> SleepAllowed = SuspendRunner.IsSleepAllowed;

        void InitializeAction(string stored)
        {
            ActHibernate.IsEnabled = HibernateAllowed();
            ActSleep.IsEnabled = SleepAllowed();

            var button = Enum.TryParse(stored, out PowerAction action) && Enum.IsDefined(typeof(PowerAction), stored.Trim())
                ? ActionButton(action) : ActShutdown;
            (button.IsEnabled ? button : ActShutdown).IsChecked = true;
        }

        PowerAction SelectedAction =>
            ActRestart.IsChecked == true ? PowerAction.Restart :
            ActHibernate.IsChecked == true ? PowerAction.Hibernate :
            ActSleep.IsChecked == true ? PowerAction.Sleep : PowerAction.Shutdown;

        RadioButton ActionButton(PowerAction action) => action switch
        {
            PowerAction.Restart => ActRestart,
            PowerAction.Hibernate => ActHibernate,
            PowerAction.Sleep => ActSleep,
            _ => ActShutdown
        };

        void OnActionInfoClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var tip = (ToolTip)button.ToolTip;
            tip.PlacementTarget = button;
            tip.IsOpen = true;
        }

        void OnActionInfoDismiss(object sender, RoutedEventArgs e) =>
            ((ToolTip)((Button)sender).ToolTip).IsOpen = false;

        void OnActionInfoKeyDown(object sender, KeyEventArgs e)
        {
            var tip = (ToolTip)((Button)sender).ToolTip;
            if (e.Key != Key.Escape || !tip.IsOpen) return;
            tip.IsOpen = false;
            e.Handled = true;
        }
    }
}
