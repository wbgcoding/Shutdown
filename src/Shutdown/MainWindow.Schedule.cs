using System;
using System.Windows;
using Shutdown.Core;

namespace Shutdown
{
    public partial class MainWindow
    {
        void OnExtend15(object sender, RoutedEventArgs e) => Extend(TimeSpan.FromMinutes(15));

        void OnExtend60(object sender, RoutedEventArgs e) => Extend(TimeSpan.FromHours(1));

        void Extend(TimeSpan span)
        {
            var pending = _pending;
            if (pending == null) return;

            var targetUtc = pending.TargetUtc + span;
            ScheduleResult ComputeExtension()
            {
                double seconds = (targetUtc - NowUtc()).TotalSeconds;
                if (seconds > Schedule.MaxSeconds) return ScheduleResult.Invalid("ErrTooLong");
                // Windows can leave a pending card visible past its target.
                if (!pending.AppTimed && seconds <= 0) return ScheduleResult.Invalid("ErrPast");
                return ScheduleResult.Valid(targetUtc, (int)Math.Ceiling(seconds));
            }

            var result = ComputeExtension();
            if (!result.Ok)
            {
                ShowError(result.ErrorKey, null, false);
                return;
            }

            if (pending.AppTimed)
            {
                HideMessage();
                pending.TargetUtc = targetUtc;
                pending.Due += span;
                UpdateUi();
                return;
            }

            ReplaceSchedule(pending.Action, ComputeExtension);
        }

        void ReplaceSchedule(PowerAction action, Func<ScheduleResult> compute)
        {
            HideMessage();
            if (!TryRun(Schedule.AbortArgs, out int abortCode)) return;
            if (abortCode != 0 && abortCode != ShutdownRunner.ErrorNoShutdownInProgress)
            {
                ShowRunError(Schedule.AbortArgs, abortCode);
                return;
            }
            bool replaced = _pending != null;
            ClearPending();

            // Recompute after the abort so its runtime does not count toward the new timer.
            var result = compute();
            if (!result.Ok)
            {
                if (replaced) ShowInfo("MsgOldCancelled");
                UpdateUi();
                return;
            }

            if (Schedule.IsAppTimed(action))
            {
                _pending = new Pending { Action = action, TargetUtc = result.TargetUtc, Due = DueOf(result.TargetUtc), AppTimed = true };
                SyncKeepAwake();
            }
            else if (Execute(Schedule.ArgsFor(action, result.Seconds), out bool maybeScheduled) || maybeScheduled)
            {
                // Keep Abort available when Windows may still hold the timer.
                _pending = new Pending { Action = action, TargetUtc = result.TargetUtc, Due = DueOf(result.TargetUtc), AppTimed = false };
                _settings.PendingAction = action.ToString();
                _settings.PendingDue = (long)_pending.Due.TotalMilliseconds;
                _settings.PendingBoot = BootMarker() ?? UnknownBoot;
            }
            bool saved = Persist();
            if (!saved && _pending?.AppTimed == false && _msgKind == MessageKind.None) ShowInfo("MsgNotSaved");
            UpdateUi();
        }
    }
}
