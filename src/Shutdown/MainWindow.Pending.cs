using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Shell;
using Shutdown.Core;
using Shutdown.Ui;

namespace Shutdown
{
    public partial class MainWindow
    {
        internal static string ClockBrushKey(TimeSpan left) => TaskbarState.StateFor(left) switch
        {
            TaskbarItemProgressState.Error => "Urgent",
            TaskbarItemProgressState.Paused => "Warn",
            _ => "Text"
        };

        void UpdatePending(DateTime now)
        {
            var pending = _pending;
            TimeSpan? left = pending == null ? null : pending.Due - Uptime();
            var (progressValue, progressState) = TaskbarState.For(left, pending?.Total ?? TimeSpan.Zero);
            TaskbarItemInfo.ProgressValue = progressValue;
            TaskbarItemInfo.ProgressState = progressState;

            if (pending == null)
            {
                Title = "Shutdown";
                // Move focus before the card collapses so Tab can continue from Start.
                if (PendingCard.IsKeyboardFocusWithin && !StartButton.Focus())
                    (ActRestart.IsChecked == true ? ActRestart : ActHibernate.IsChecked == true ? ActHibernate : ActShutdown).Focus();
                PendingCard.Visibility = Visibility.Collapsed;
                return;
            }

            var L = Loc.I;
            var clock = L.Clock(left.Value);
            var (titleKey, pendingKey) = pending.Action switch
            {
                PowerAction.Restart => ("TitlePendingRestart", "PendingRestart"),
                PowerAction.Hibernate => ("TitlePendingHibernate", "PendingHibernate"),
                _ => ("TitlePendingShutdown", "PendingShutdown")
            };
            Title = L.F(titleKey, clock);
            PendingCard.Visibility = Visibility.Visible;
            PendingTitle.Text = L[pendingKey];
            PendingWhen.Text = L.F("SummaryAt", L.RelativeDateText(pending.Target, now), L.TimeText(pending.Target));
            PendingClock.Text = clock;
            SetBrush(PendingClock, ClockBrushKey(left.Value));
            AutomationProperties.SetName(PendingClock, L.F("PendingRemaining", clock));
            PendingKeepOpen.Visibility = pending.AppTimed ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
