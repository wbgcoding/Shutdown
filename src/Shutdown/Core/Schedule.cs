using System;

namespace Shutdown.Core
{
    public enum PowerAction { Shutdown, Restart, Hibernate }

    public enum TimeMode { Countdown, At }

    public sealed class ScheduleResult
    {
        public bool Ok { get; private set; }
        public string ErrorKey { get; private set; }
        public DateTime Target { get; private set; }
        public int Seconds { get; private set; }

        public static ScheduleResult Valid(DateTime target, int seconds) =>
            new ScheduleResult { Ok = true, Target = target, Seconds = seconds };

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
            return ScheduleResult.Valid(now.AddSeconds(total), (int)total);
        }

        public static ScheduleResult FromTarget(DateTime date, int hour, int minute, DateTime now)
        {
            var target = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Local);
            // UTC difference so a daylight saving switch in between is counted correctly.
            double diff = (target.ToUniversalTime() - now.ToUniversalTime()).TotalSeconds;
            if (diff <= 0) return ScheduleResult.Invalid("ErrPast");
            if (diff > MaxSeconds) return ScheduleResult.Invalid("ErrTooLong");
            return ScheduleResult.Valid(target, (int)Math.Ceiling(diff));
        }

        /// <summary>
        /// shutdown.exe implies /f for any /t above 0, so a countdown without force
        /// has to be run by the app itself and end with /t 0. Hibernate has no timer at all.
        /// </summary>
        public static bool IsAppTimed(PowerAction action, bool force) =>
            action == PowerAction.Hibernate || !force;

        public static string BuildArgs(PowerAction action, bool force, int seconds)
        {
            string verb = action == PowerAction.Restart ? "/r" : action == PowerAction.Hibernate ? "/h" : "/s";
            string f = force ? " /f" : "";
            if (action == PowerAction.Hibernate) return verb + f;
            return verb + f + " /t " + seconds;
        }

        /// <summary>Arguments for the system-timed schedule, or for the immediate run of an app-timed one.</summary>
        public static string ArgsFor(PowerAction action, bool force, int seconds) =>
            IsAppTimed(action, force) ? BuildArgs(action, force, 0) : BuildArgs(action, force, seconds);

        public const string AbortArgs = "/a";
    }
}
