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
                Assert.Equal(Now.ToUniversalTime().AddSeconds(r.Seconds), r.TargetUtc);
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
            var r = Schedule.FromTarget(Now.Date, 12, 2, now);
            Assert.True(r.Ok);
            Assert.Equal(120, r.Seconds);
            Assert.Equal(new DateTime(2026, 10, 16, 12, 2, 0), r.Target);
        }

        [Fact]
        public void Target_needs_a_minute_of_lead_time()
        {
            Assert.Equal("ErrTooSoon", Schedule.FromTarget(Now.Date, 12, 1, Now.AddSeconds(1)).ErrorKey);
            Assert.True(Schedule.FromTarget(Now.Date, 12, 1, Now).Ok);
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

        static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        [Fact]
        public void Time_skipped_by_spring_clock_change_is_rejected()
        {
            var now = new DateTime(2027, 3, 27, 12, 0, 0, DateTimeKind.Utc);
            Assert.Equal("ErrInvalidTime", Schedule.FromTarget(new DateTime(2027, 3, 28), 2, 30, now, Berlin).ErrorKey);
            Assert.True(Schedule.FromTarget(new DateTime(2027, 3, 28), 3, 0, now, Berlin).Ok);
        }

        [Fact]
        public void Repeated_autumn_hour_uses_its_first_occurrence()
        {
            // 00:00 summer time = 22:00 UTC; 02:30 summer time = 00:30 UTC.
            var now = new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc);
            var r = Schedule.FromTarget(new DateTime(2026, 10, 25), 2, 30, now, Berlin);
            Assert.Equal(9000, r.Seconds);
            Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), r.TargetUtc);
        }

        [Fact]
        public void Countdown_across_dst_end_shows_real_end_time()
        {
            if (TimeZoneInfo.Local.Id != "W. Europe Standard Time") return;
            var now = new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Local);
            var r = Schedule.FromCountdown(4, 0, now);
            Assert.Equal(new DateTime(2026, 10, 25, 4, 0, 0), r.Target);
        }

        [Theory]
        [InlineData(PowerAction.Shutdown, 90, "/s /f /t 90")]
        [InlineData(PowerAction.Restart, 90, "/r /f /t 90")]
        [InlineData(PowerAction.Hibernate, 0, "/h /f")]
        public void Args_match_the_action(PowerAction a, int seconds, string expected)
        {
            Assert.Equal(expected, Schedule.ArgsFor(a, seconds));
        }

        [Fact]
        public void Only_hibernate_is_timed_by_the_app()
        {
            Assert.False(Schedule.IsAppTimed(PowerAction.Shutdown));
            Assert.False(Schedule.IsAppTimed(PowerAction.Restart));
            Assert.True(Schedule.IsAppTimed(PowerAction.Hibernate));
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
        public void Every_key_is_used_in_the_source()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "src", "Shutdown"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            var src = string.Concat(Directory.GetFiles(Path.Combine(dir, "src", "Shutdown"), "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains(@"\obj\") && !f.Contains(@"\bin\"))
                .Select(File.ReadAllText));
            var unused = Loc.LoadTable("en").Keys.Where(k => !src.Contains("\"" + k + "\"") && !src.Contains("[" + k + "]")).ToList();
            Assert.Empty(unused);
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
            var s = new Settings { Theme = "dark", Hours = 3, Minutes = 15, Action = "Restart",
                                   PendingAction = "Shutdown", PendingDue = 7200000, PendingBoot = "boot:7" };
            store.Save(s);
            store.Save(s);
            var back = store.Load();
            Assert.Equal("dark", back.Theme);
            Assert.Equal(3, back.Hours);
            Assert.Equal(15, back.Minutes);
            Assert.Equal("Restart", back.Action);
            Assert.Equal("Shutdown", back.PendingAction);
            Assert.Equal(7200000L, back.PendingDue);
            Assert.Equal("boot:7", back.PendingBoot);
            Assert.False(File.Exists(File_ + ".new"));
        }

        [Fact]
        public void Missing_fields_get_defaults()
        {
            File.WriteAllText(File_, "{\"Version\":1,\"Theme\":\"light\"}");
            var s = new SettingsStore(File_).Load();
            Assert.Equal(1, s.Hours);
            Assert.Equal("light", s.Theme);
            Assert.Equal("Countdown", s.Mode);
        }

        [Fact]
        public void File_from_1_0_with_the_force_switch_still_loads()
        {
            File.WriteAllText(File_, "{\"Version\":1,\"Force\":false,\"Hours\":2,\"Minutes\":45}");
            var s = new SettingsStore(File_).Load();
            Assert.Equal(2, s.Hours);
            Assert.Equal(45, s.Minutes);
        }

        [Fact]
        public void Corrupt_or_future_file_falls_back_to_defaults()
        {
            File.WriteAllText(File_, "{ not json");
            Assert.Equal("system", new SettingsStore(File_).Load().Theme);
            File.WriteAllText(File_, "{\"Version\":99,\"Theme\":\"dark\"}");
            Assert.Equal("system", new SettingsStore(File_).Load().Theme);
        }

        [Fact]
        public void Future_file_is_not_overwritten()
        {
            const string future = "{\"Version\":99,\"Theme\":\"dark\"}";
            File.WriteAllText(File_, future);
            var store = new SettingsStore(File_);
            var s = store.Load();
            s.Hours = 5;
            store.Save(s);
            Assert.Equal(future, File.ReadAllText(File_));
        }

        [Fact]
        public void Corrupt_file_is_replaced_on_save()
        {
            File.WriteAllText(File_, "{ not json");
            var store = new SettingsStore(File_);
            store.Save(store.Load());
            Assert.Contains("\"Version\"", File.ReadAllText(File_));
        }
    }

    public class BootInfoTests
    {
        [Fact]
        public void Marker_is_read_and_stable_within_one_boot()
        {
            string a = BootInfo.ReadMarker(), b = BootInfo.ReadMarker();
            Assert.StartsWith("boot:", a);
            Assert.Equal(a, b);
        }
    }
}
