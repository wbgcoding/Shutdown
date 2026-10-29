using System;
using System.Linq;

namespace Shutdown.Core
{
    public enum PowerAction { Shutdown, Restart, Hibernate }

    public enum TimeMode { Countdown, At }

    public sealed class ScheduleResult
    {
        public bool Ok { get; private set; }
        public string ErrorKey { get; private set; }
        /// <summary>Local wall-clock time, for display only.</summary>
        public DateTime Target => TargetUtc.ToLocalTime();
        /// <summary>The real instant; all comparisons use this.</summary>
        public DateTime TargetUtc { get; private set; }
        public int Seconds { get; private set; }

        public static ScheduleResult Valid(DateTime targetUtc, int seconds) =>
            new ScheduleResult { Ok = true, TargetUtc = targetUtc, Seconds = seconds };

        public static ScheduleResult Invalid(string errorKey) =>
            new ScheduleResult { Ok = false, ErrorKey = errorKey };
    }

    public static class Schedule
    {
        // Upper bound of shutdown.exe /t (10 years).
        public const int MaxSeconds = 315360000;
        public const int MinCountdownSeconds = 60;

        public static ScheduleResult FromCountdown(int hours, int minutes, DateTime now)
        {
            long total = (long)hours * 3600 + (long)minutes * 60;
            if (total < MinCountdownSeconds) return ScheduleResult.Invalid("ErrTooShort");
            if (total > MaxSeconds) return ScheduleResult.Invalid("ErrTooLong");
            // Via UTC so the shown end time is right across a daylight saving switch.
            return ScheduleResult.Valid(now.ToUniversalTime().AddSeconds(total), (int)total);
        }

        public static ScheduleResult FromTarget(DateTime date, int hour, int minute, DateTime now) =>
            FromTarget(date, hour, minute, now, TimeZoneInfo.Local);

        public static ScheduleResult FromTarget(DateTime date, int hour, int minute, DateTime now, TimeZoneInfo zone)
        {
            var local = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Unspecified);
            // Skipped by the spring clock change: this wall-clock time never happens that day.
            if (zone.IsInvalidTime(local)) return ScheduleResult.Invalid("ErrInvalidTime");
            // A repeated autumn hour resolves to its first occurrence (still summer time).
            var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
            var targetUtc = DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
            double diff = (targetUtc - now.ToUniversalTime()).TotalSeconds;
            if (diff <= 0) return ScheduleResult.Invalid("ErrPast");
            // Same lead time as a countdown: a forced shutdown seconds after the click leaves no chance to cancel.
            if (diff < MinCountdownSeconds) return ScheduleResult.Invalid("ErrTooSoon");
            if (diff > MaxSeconds) return ScheduleResult.Invalid("ErrTooLong");
            return ScheduleResult.Valid(targetUtc, (int)Math.Ceiling(diff));
        }

        /// <summary>Hibernate has no Windows timer, so the app counts down itself and runs it at the target.</summary>
        public static bool IsAppTimed(PowerAction action) => action == PowerAction.Hibernate;

        /// <summary>
        /// Always forced: shutdown.exe implies /f for any /t above 0 anyway. Shutdown and restart hand
        /// the timer to Windows; hibernate takes no /t and runs when the app's countdown ends.
        /// </summary>
        public static string ArgsFor(PowerAction action, int seconds) =>
            action == PowerAction.Hibernate ? "/h /f" :
            (action == PowerAction.Restart ? "/r" : "/s") + " /f /t " + seconds;

        public const string AbortArgs = "/a";
    }
}
