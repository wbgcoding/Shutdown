using System;
using Shutdown.Core;

namespace Shutdown
{
    public partial class MainWindow
    {
        internal void ApplyCommand(Command command)
        {
            if (command.Error != null)
            {
                ShowError("ErrCommandLine", command.Error, false);
                return;
            }
            if (command.Abort)
            {
                if (_pending == null) ShowInfo("NothingScheduled");
                else OnAbort(this, null);
                return;
            }

            var action = command.Action.HasValue ? ActionButton(command.Action.Value) : null;
            if (action?.IsEnabled == false)
            {
                ShowError(command.Action == PowerAction.Sleep ? "SleepUnavailable" : "HibernateUnavailable", null, false);
                return;
            }

            bool timed = command.CountdownMinutes.HasValue || command.At.HasValue;
            if (action == null && !timed) return;

            // Save only after the action, mode and fields agree.
            _loading = true;
            try
            {
                if (action != null) action.IsChecked = true;
                if (command.CountdownMinutes is int countdown)
                {
                    ModeCountdown.IsChecked = true;
                    int hours = Math.DivRem(countdown, 60, out int minutes);
                    if (hours > DurHours.Maximum) AllowLongCountdown();
                    SetCountdown(hours, minutes);
                }
                else if (command.At is TimeSpan at)
                {
                    ModeAt.IsChecked = true;
                    _date = CommandLine.TargetDate(at, Now());
                    AtHour.Value = at.Hours;
                    AtMinute.Value = at.Minutes;
                }
            }
            finally { _loading = false; }

            OnInputChanged(this, null);
            if (timed) OnStart(this, null);
        }
    }
}
