using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using Shutdown.Core;

namespace Shutdown
{
    public partial class MainWindow
    {
        bool _updatingCountdown;

        int MaxCountdownMinutes => Math.Min(DurHours.Maximum * 60 + DurMinutes.Maximum, Schedule.MaxSeconds / 60);

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

        string _appTimedReason;

        /// <summary>Why the app counts down itself, then on its own line what the user has to do.</summary>
        void SetAppTimedNote(string reason)
        {
            if (reason == _appTimedReason) return;
            _appTimedReason = reason;
            AppTimedText.Inlines.Clear();
            if (reason == null) return;
            AppTimedText.Inlines.Add(new Run(reason));
            AppTimedText.Inlines.Add(new LineBreak());
            AppTimedText.Inlines.Add(new Run(Loc.I["PendingKeepOpen"]) { FontWeight = FontWeights.SemiBold });
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
                // Screen readers read the full words, not the "h" or "min" abbreviation on the chip.
                AutomationProperties.SetName(chip, hours == 0 ? Loc.I.F("ChipMinutesName", minutes)
                    : hours == 1 ? Loc.I["ChipOneHourName"] : Loc.I.F("ChipHoursName", hours));
                Chips.Children.Add(chip);
            }
        }

        void UpdatePresetChips()
        {
            int duration = DurHours.Value * 60 + DurMinutes.PendingValue;
            foreach (PresetChip chip in Chips.Children)
                chip.IsChecked = chip.Minutes == duration;
        }

        /// <summary>Lets the hour field take every countdown the command line accepts (up to the Windows timer limit).</summary>
        void AllowLongCountdown()
        {
            DurHours.Maximum = CommandLine.MaxCountdownMinutes / 60;
            DurHours.MaxLength = 5;
        }

        void InitializeCountdown(Settings settings)
        {
            // A countdown set from the command line can exceed the field's usual hour limit; keep it on the next start.
            if (settings.Hours > DurHours.Maximum) AllowLongCountdown();
            DurHours.Value = settings.Hours;
            long total = DurHours.Value * 60L + Math.Max(DurMinutes.Minimum, settings.Minutes);
            SetCountdown(0, (int)Math.Min(total, MaxCountdownMinutes));
            if (total > MaxCountdownMinutes) ShowInfo("ErrTooLong");

            DurHours.ValueChanged += OnCountdownChanged;
            DurMinutes.ValueChanged += OnCountdownChanged;
            DurMinutes.PendingValueChanged += (s, e) => UpdateUi();
            DurMinutes.Carry += (s, delta) => TryCarryMinutes(delta);
            DurMinutes.TryCommitCarry = TryCarryMinutes;
        }

        void OnCountdownChanged(object sender, EventArgs e)
        {
            if (_updatingCountdown) return;
            DropTooLongNote();
            OnInputChanged(sender, null);
        }

        // The note explains a duration that was cut or refused; once the user picks another one, the summary line says the rest.
        void DropTooLongNote()
        {
            if (!_loading && _msgKind == MessageKind.Info && _msgKey == "ErrTooLong") HideMessage();
        }

        bool TryCarryMinutes(int delta)
        {
            int total = DurHours.Value * 60 + DurMinutes.Value + delta;
            if (total > MaxCountdownMinutes)
            {
                UpdateUi();
                if (_msgKind != MessageKind.Info || _msgKey != "ErrTooLong") ShowInfo("ErrTooLong");
                return false;
            }
            SetCountdown(0, Math.Max(0, total));
            return true;
        }

        void SetCountdown(int hours, int minutes)
        {
            hours += Math.DivRem(minutes, 60, out int remainingMinutes);
            // Refresh and save once, after both fields show the new duration.
            _updatingCountdown = true;
            try
            {
                DurHours.Value = hours;
                DurMinutes.Value = remainingMinutes;
            }
            finally { _updatingCountdown = false; }

            DropTooLongNote();
            OnInputChanged(this, null);
        }

        ScheduleResult ComputeCountdown(DateTime now)
        {
            int minutes = DurMinutes.PendingValue;
            return DurHours.Value * 60 + minutes > MaxCountdownMinutes
                ? ScheduleResult.Invalid("ErrTooLong")
                : Schedule.FromCountdown(DurHours.Value, minutes, now);
        }
    }
}
