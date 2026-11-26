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
                pending.Total += span;
                if (pending.Due - Uptime() > LastMinuteThreshold) ResetLastMinuteWarning();
                UpdateUi();
                return;
            }

            ReplaceSchedule(pending.Action, ComputeExtension, pending.Total + span);
        }

        void ReplaceSchedule(PowerAction action, Func<ScheduleResult> compute, TimeSpan? total = null)
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

            bool appTimed = Schedule.IsAppTimed(action);
            if (appTimed || Execute(Schedule.ArgsFor(action, result.Seconds, Loc.I["ShutdownComment"]), out bool maybeScheduled) || maybeScheduled)
            {
                var uptime = Uptime();
                var remaining = result.TargetUtc - NowUtc();
                _pending = new Pending
                {
                    Action = action,
                    TargetUtc = result.TargetUtc,
                    Due = uptime + remaining,
                    Total = total ?? remaining,
                    AppTimed = appTimed
                };
                if (appTimed) SyncKeepAwake();
                else
                {
                    _settings.PendingAction = action.ToString();
                    _settings.PendingDue = (long)_pending.Due.TotalMilliseconds;
                    _settings.PendingBoot = BootMarker() ?? UnknownBoot;
                }
            }
            bool saved = Persist();
            if (!saved && _pending?.AppTimed == false && _msgKind == MessageKind.None) ShowInfo("MsgNotSaved");
            UpdateUi();
        }

        Pending RestorePending(Settings settings)
        {
            // Only Windows-timed actions are stored.
            if (!settings.PendingDue.HasValue || !SameBoot(settings.PendingBoot) ||
                !Enum.TryParse(settings.PendingAction, out PowerAction action) ||
                !Enum.IsDefined(typeof(PowerAction), action) || Schedule.IsAppTimed(action)) return null;

            var uptime = Uptime();
            double remainingMs = settings.PendingDue.Value - uptime.TotalMilliseconds;
            if (remainingMs <= 0 || remainingMs > Schedule.MaxSeconds * 1000d) return null;

            var due = TimeSpan.FromMilliseconds(settings.PendingDue.Value);
            var remaining = due - uptime;
            return new Pending { Action = action, Due = due, Total = remaining, TargetUtc = NowUtc() + remaining };
        }

        static bool SameBoot(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return false;
            string current = BootMarker();
            // Keep the Abort button available when the boot marker cannot be verified.
            if (stored == UnknownBoot || current == null) return true;
            return stored == current;
        }
    }
}
