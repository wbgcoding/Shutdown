using System;
using Shutdown.Core;

namespace Shutdown
{
    public partial class MainWindow
    {
        bool _updatingCountdown;
        bool _countdownOverflow;

        void InitializeCountdown(Settings settings)
        {
            DurHours.Value = settings.Hours;
            SetCountdown(DurHours.Value, Math.Max(DurMinutes.Minimum, settings.Minutes));
            DurHours.ValueChanged += OnCountdownChanged;
            DurMinutes.ValueChanged += OnCountdownChanged;
            DurMinutes.Carry += OnMinuteCarry;
        }

        void OnCountdownChanged(object sender, EventArgs e)
        {
            if (!_updatingCountdown) SetCountdown(DurHours.Value, DurMinutes.Value);
        }

        void OnMinuteCarry(object sender, int direction)
        {
            if (direction > 0) SetCountdown(DurHours.Value, DurMinutes.Maximum + 1);
            else if (DurHours.Value > DurHours.Minimum) SetCountdown(DurHours.Value - 1, DurMinutes.Maximum);
        }

        void SetCountdown(int hours, int minutes)
        {
            hours += Math.DivRem(minutes, 60, out int remainingMinutes);
            _countdownOverflow = hours > DurHours.Maximum;

            // Refresh and save once, after both fields show the new duration.
            _updatingCountdown = true;
            try
            {
                if (DurHours.Value != hours) DurHours.Value = hours;
                if (DurMinutes.Value != remainingMinutes) DurMinutes.Value = remainingMinutes;
            }
            finally { _updatingCountdown = false; }

            OnInputChanged(this, null);
        }

        ScheduleResult ComputeCountdown(DateTime now) => _countdownOverflow
            ? ScheduleResult.Invalid("ErrTooLong")
            : Schedule.FromCountdown(DurHours.Value, DurMinutes.Value, now);
    }
}
