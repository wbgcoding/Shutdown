using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    public class ScheduleTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Local);

        [Theory]
        [InlineData(0, 0, "ErrTooShort")]
        [InlineData(0, 1, null)]
        [InlineData(1, 30, null)]
        [InlineData(999, 59, null)]
        [InlineData(87601, 0, "ErrTooLong")]
        public void Countdown_validates_range(int h, int m, string error)
        {
            var r = Schedule.FromCountdown(h, m, Now);
            Assert.Equal(error == null, r.Ok);
            Assert.Equal(error, r.ErrorKey);
            if (r.Ok)
            {
                Assert.Equal(h * 3600 + m * 60, r.Seconds);
                Assert.Equal(Now.AddSeconds(r.Seconds), r.Target);
            }
        }

        [Fact]
        public void Target_in_the_past_or_now_is_rejected()
        {
            Assert.Equal("ErrPast", Schedule.FromTarget(Now.Date, 11, 59, Now).ErrorKey);
            Assert.Equal("ErrPast", Schedule.FromTarget(Now.Date, 12, 0, Now).ErrorKey);
        }

        [Fact]
        public void Target_seconds_round_up()
        {
            var now = Now.AddSeconds(0.4);
            var r = Schedule.FromTarget(Now.Date, 12, 1, now);
            Assert.True(r.Ok);
            Assert.Equal(60, r.Seconds);
            Assert.Equal(new DateTime(2026, 10, 16, 12, 1, 0), r.Target);
        }

        [Fact]
        public void Target_tomorrow_counts_a_full_day()
        {
            var r = Schedule.FromTarget(Now.Date.AddDays(1), 12, 0, Now);
            Assert.Equal(86400, r.Seconds);
        }

        [Fact]
        public void Target_beyond_ten_years_is_rejected()
        {
            Assert.Equal("ErrTooLong", Schedule.FromTarget(Now.Date.AddYears(11), 0, 0, Now).ErrorKey);
        }

        [Fact]
        public void Target_across_dst_end_counts_real_seconds()
        {
            // Only meaningful where the clock falls back on 2026-10-25 (Central Europe).
            if (TimeZoneInfo.Local.Id != "W. Europe Standard Time") return;
            var now = new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Local);
            var r = Schedule.FromTarget(now.Date, 4, 0, now);
            Assert.Equal(4 * 3600, r.Seconds);
        }

        [Theory]
        [InlineData(PowerAction.Shutdown, true, 90, "/s /f /t 90")]
        [InlineData(PowerAction.Restart, true, 90, "/r /f /t 90")]
        [InlineData(PowerAction.Shutdown, false, 90, "/s /t 0")]
        [InlineData(PowerAction.Restart, false, 90, "/r /t 0")]
        [InlineData(PowerAction.Hibernate, true, 90, "/h /f")]
        [InlineData(PowerAction.Hibernate, false, 90, "/h")]
        public void Args_match_the_action(PowerAction a, bool force, int seconds, string expected)
        {
            Assert.Equal(expected, Schedule.ArgsFor(a, force, seconds));
        }

        [Fact]
        public void Only_forced_shutdown_and_restart_use_the_windows_timer()
        {
            Assert.False(Schedule.IsAppTimed(PowerAction.Shutdown, true));
            Assert.False(Schedule.IsAppTimed(PowerAction.Restart, true));
            Assert.True(Schedule.IsAppTimed(PowerAction.Shutdown, false));
            Assert.True(Schedule.IsAppTimed(PowerAction.Hibernate, true));
        }
    }

    public class LocTests
    {
        [Fact]
        public void Every_locale_has_the_same_keys_both_ways()
        {
            var en = Loc.LoadTable("en");
            var de = Loc.LoadTable("de");
            Assert.True(en.Count > 40);
            Assert.Empty(en.Keys.Except(de.Keys));
            Assert.Empty(de.Keys.Except(en.Keys));
        }

        [Fact]
        public void Values_are_filled_and_placeholders_match()
        {
            var en = Loc.LoadTable("en");
            var de = Loc.LoadTable("de");
            foreach (var key in en.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(en[key]), "en " + key);
                Assert.False(string.IsNullOrWhiteSpace(de[key]), "de " + key);
                var pe = Regex.Matches(en[key], @"\{\d\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x);
                var pd = Regex.Matches(de[key], @"\{\d\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x);
                Assert.Equal(pe, pd);
            }
        }

        [Fact]
        public void No_dashes_in_ui_text()
        {
            foreach (var lang in Loc.Languages)
                foreach (var v in Loc.LoadTable(lang).Values)
                {
                    Assert.DoesNotContain((char)0x2014, v);
                    Assert.DoesNotContain((char)0x2013, v);
                }
        }
    }

    public class SettingsTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownTests-" + Guid.NewGuid().ToString("N"));
        string File_ => Path.Combine(_dir, SettingsStore.FileName);

        public SettingsTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void Roundtrip_keeps_values()
        {
            var store = new SettingsStore(File_);
            var s = new Settings { Language = "de", Force = false, Hours = 3, Minutes = 15, Action = "Restart",
                                   PendingAction = "Shutdown", PendingTarget = "2026-10-16T20:00:00.0000000+02:00" };
            store.Save(s);
            store.Save(s);
            var back = store.Load();
            Assert.Equal("de", back.Language);
            Assert.False(back.Force);
            Assert.Equal(3, back.Hours);
            Assert.Equal(15, back.Minutes);
            Assert.Equal("Restart", back.Action);
            Assert.Equal("Shutdown", back.PendingAction);
            Assert.False(File.Exists(File_ + ".new"));
        }

        [Fact]
        public void Missing_fields_get_defaults()
        {
            File.WriteAllText(File_, "{\"Version\":1,\"Language\":\"en\"}");
            var s = new SettingsStore(File_).Load();
            Assert.True(s.Force);
            Assert.Equal(1, s.Hours);
            Assert.Equal("system", s.Theme);
        }

        [Fact]
        public void Corrupt_or_future_file_falls_back_to_defaults()
        {
            File.WriteAllText(File_, "{ not json");
            Assert.Equal("system", new SettingsStore(File_).Load().Language);
            File.WriteAllText(File_, "{\"Version\":99,\"Language\":\"de\"}");
            Assert.Equal("system", new SettingsStore(File_).Load().Language);
        }
    }
}
