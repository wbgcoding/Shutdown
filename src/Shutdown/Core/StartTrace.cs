using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Shutdown.Core
{
    /// <summary>
    /// Times the phases of a start and logs them only when the start was slow, so a report of a slow
    /// start can be traced to its phase without filling the log on every normal start.
    /// </summary>
    public sealed class StartTrace
    {
        /// <summary>Starts at or below this stay out of the log.</summary>
        public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(1);

        readonly Func<TimeSpan> _elapsed;
        readonly Action<string> _write;
        readonly List<(string Phase, TimeSpan At)> _marks = new List<(string, TimeSpan)>();
        bool _done;

        public StartTrace() : this(SinceProcessStart(), s => Log.Write(s)) { }

        internal StartTrace(Func<TimeSpan> elapsed, Action<string> write)
        {
            _elapsed = elapsed;
            _write = write;
            Mark("runtime");
        }

        // Counted from the process start, so the time the runtime and WPF need before any app code runs shows up too.
        // The start time is read once; a stopwatch measures from there, so a clock change during the start cannot skew it.
        static Func<TimeSpan> SinceProcessStart()
        {
            var before = TimeSpan.Zero;
            try
            {
                using (var p = Process.GetCurrentProcess())
                    before = DateTime.Now - p.StartTime;
            }
            catch (Exception) { }
            var clock = Stopwatch.StartNew();
            return () => before + clock.Elapsed;
        }

        public void Mark(string phase)
        {
            if (!_done) _marks.Add((phase, _elapsed()));
        }

        /// <summary>Closes the trace; only the first call counts, later ones do nothing.</summary>
        public void Done(string phase)
        {
            if (_done) return;
            Mark(phase);
            _done = true;
            var total = _marks[_marks.Count - 1].At;
            if (total <= SlowAfter) return;
            var prev = TimeSpan.Zero;
            var parts = _marks.Select(m =>
            {
                var part = m.At - prev;
                prev = m.At;
                return m.Phase + " " + Ms(part);
            });
            _write("slow start " + Ms(total) + ": " + string.Join(", ", parts));
        }

        static string Ms(TimeSpan t) => Math.Max(0, t.TotalMilliseconds).ToString("0", CultureInfo.InvariantCulture) + " ms";
    }
}
