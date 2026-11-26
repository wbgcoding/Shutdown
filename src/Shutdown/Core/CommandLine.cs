using System;
using System.Collections.Generic;
using System.Globalization;

namespace Shutdown.Core
{
    internal sealed class Command
    {
        public PowerAction? Action { get; }
        public int? CountdownMinutes { get; }
        public TimeSpan? At { get; }
        public bool Abort { get; }
        public string Error { get; }

        public Command(PowerAction? action = null, int? countdownMinutes = null, TimeSpan? at = null,
            bool abort = false, string error = null)
        {
            Action = action;
            CountdownMinutes = countdownMinutes;
            At = at;
            Abort = abort;
            Error = error;
        }
    }

    internal static class CommandLine
    {
        internal const string Usage = "Shutdown.exe [--shutdown|--restart|--hibernate|--sleep] [--in <minutes>|--in <h:mm>|--at <hh:mm>] | --abort";
        internal static readonly Command Invalid = new Command(error: Usage);
        internal const int MaxCountdownMinutes = Schedule.MaxSeconds / 60;

        public static Command Parse(IReadOnlyList<string> args)
        {
            PowerAction? action = null;
            int? countdown = null;
            TimeSpan? at = null;
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                int prefix = arg.StartsWith("--", StringComparison.Ordinal) ? 2 :
                    arg.StartsWith("/", StringComparison.Ordinal) ? 1 : 0;
                if (prefix == 0) return Invalid;

                string name = arg.Substring(prefix).ToLowerInvariant();
                switch (name)
                {
                    case "abort":
                        return args.Count == 1 ? new Command(abort: true) : Invalid;
                    case "in":
                    case "at":
                        if (countdown.HasValue || at.HasValue || ++i == args.Count) return Invalid;
                        if (name == "in")
                        {
                            if (!TryParseCountdown(args[i], out int minutes)) return Invalid;
                            countdown = minutes;
                        }
                        else
                        {
                            if (args[i].Length != 5 ||
                                !TryParseClock(args[i], out int hours, out int minutes) || hours > 23) return Invalid;
                            at = new TimeSpan(hours, minutes, 0);
                        }
                        break;
                    default:
                        if (action.HasValue) return Invalid;
                        action = name switch
                        {
                            "shutdown" => PowerAction.Shutdown,
                            "restart" => PowerAction.Restart,
                            "hibernate" => PowerAction.Hibernate,
                            "sleep" => PowerAction.Sleep,
                            _ => (PowerAction?)null
                        };
                        if (!action.HasValue) return Invalid;
                        break;
                }
            }
            return new Command(action, countdown, at);
        }

        static bool TryParseCountdown(string value, out int minutes)
        {
            if (value.IndexOf(':') >= 0)
            {
                if (!TryParseClock(value, out int hours, out minutes) || hours > MaxCountdownMinutes / 60) return false;
                minutes += hours * 60;
            }
            else if (!TryParseNumber(value, out minutes)) return false;
            return minutes >= Schedule.MinCountdownSeconds / 60 && minutes <= MaxCountdownMinutes;
        }

        static bool TryParseClock(string value, out int hours, out int minutes)
        {
            hours = minutes = 0;
            int separator = value.IndexOf(':');
            return separator > 0 && value.Length == separator + 3 &&
                TryParseNumber(value.Substring(0, separator), out hours) &&
                TryParseNumber(value.Substring(separator + 1), out minutes) && minutes < 60;
        }

        static bool TryParseNumber(string value, out int number) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);

        public static DateTime TargetDate(TimeSpan at, DateTime now)
        {
            var result = Schedule.FromTarget(now.Date, at.Hours, at.Minutes, now);
            // In a repeated hour, use today's second occurrence if it is still ahead.
            bool passed = result.ErrorKey == "ErrPast" ||
                (result.ErrorKey == "ErrInvalidTime" && at <= now.TimeOfDay);
            return passed ? now.Date.AddDays(1) : now.Date;
        }
    }
}
