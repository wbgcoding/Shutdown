using System;
using System.Collections.Generic;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    [Trait("Layer", "Logic")]
    public class StartTraceTests
    {
        readonly List<string> _lines = new List<string>();
        TimeSpan _now;

        StartTrace NewTrace(double startMs)
        {
            _now = TimeSpan.FromMilliseconds(startMs);
            return new StartTrace(() => _now, _lines.Add);
        }

        [Fact]
        public void A_fast_start_writes_nothing()
        {
            var t = NewTrace(300);
            _now = TimeSpan.FromMilliseconds(700);
            t.Done("first frame");
            Assert.Empty(_lines);
        }

        [Fact]
        public void A_slow_start_logs_each_phase_once()
        {
            var t = NewTrace(400);
            _now = TimeSpan.FromMilliseconds(5400);
            t.Mark("instance");
            _now = TimeSpan.FromMilliseconds(5500);
            t.Mark("settings");
            _now = TimeSpan.FromMilliseconds(6100);
            t.Done("first frame");
            _now = TimeSpan.FromMilliseconds(9000);
            t.Done("first frame");
            t.Mark("late");
            var line = Assert.Single(_lines);
            Assert.Equal("slow start 6100 ms: runtime 400 ms, instance 5000 ms, settings 100 ms, first frame 600 ms", line);
        }

        [Fact]
        public void Exactly_the_threshold_still_counts_as_fast()
        {
            var t = NewTrace(0);
            _now = StartTrace.SlowAfter;
            t.Done("first frame");
            Assert.Empty(_lines);
        }
    }
}
