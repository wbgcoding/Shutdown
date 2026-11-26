using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using Shutdown.Core;

namespace Shutdown
{
    public partial class MainWindow
    {
        bool _updatingCountdown;
        bool _countdownOverflow;

        sealed class PresetChip : ToggleButton
        {
            readonly Action _select;

            public int Minutes { get; }

            public PresetChip(int minutes, Action select)
            {
                Minutes = minutes;
                _select = select;
            }

            // Activating a preset always selects its duration, including through UI Automation.
            protected override void OnToggle() => _select();
        }

        void RefreshPresetChips()
        {
            Chips.Children.Clear();
            var style = (Style)FindResource("ChipToggle");
            foreach (int duration in new[] { 15, 30, 60, 120, 180 })
            {
                int hours = Math.DivRem(duration, 60, out int minutes);
                var chip = new PresetChip(duration, () => SetCountdown(hours, minutes))
                {
                    Style = style,
                    Content = hours > 0 ? Loc.I.F("ChipHours", hours) : Loc.I.F("ChipMinutes", minutes),
                };
                AutomationProperties.SetAutomationId(chip, "Chip" + duration);
                Chips.Children.Add(chip);
            }
        }

        void UpdatePresetChips()
        {
            int duration = DurHours.Value * 60 + DurMinutes.Value;
            foreach (PresetChip chip in Chips.Children)
                chip.IsChecked = chip.Minutes == duration;
        }

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
