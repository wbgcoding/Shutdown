using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    /// <summary>Boundary and regression tests for Schedule; clocks are fixed UTC instants and zones explicit.</summary>
    public class ScheduleEdgeTests
    {
        static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        static DateTime Utc(int y, int mo, int d, int h, int mi, int s = 0, int ms = 0) =>
            new DateTime(y, mo, d, h, mi, s, ms, DateTimeKind.Utc);

        // 12:00 in Berlin (summer time, UTC+2).
        static readonly DateTime SummerNoonUtc = Utc(2026, 6, 1, 10, 0);

        [Fact]
        public void Constants_match_the_shutdown_exe_limits()
        {
            Assert.Equal(60, Schedule.MinCountdownSeconds);
            Assert.Equal(315360000, Schedule.MaxSeconds);
            Assert.Equal(3650L * 86400, Schedule.MaxSeconds);
            Assert.Equal("/a", Schedule.AbortArgs);
        }

        [Theory]
        [InlineData(0, 0, "ErrTooShort")]
        [InlineData(0, 1, null)]
        [InlineData(0, 59, null)]
        [InlineData(0, 60, null)]
        [InlineData(0, -1, "ErrTooShort")]
        [InlineData(-1, 0, "ErrTooShort")]
        [InlineData(-1, 60, "ErrTooShort")]
        [InlineData(-1, 59, "ErrTooShort")]
        [InlineData(-1, 61, null)]
        [InlineData(1, -30, null)]
        [InlineData(1, -60, "ErrTooShort")]
        [InlineData(87600, 0, null)]
        [InlineData(87599, 60, null)]
        [InlineData(87600, 1, "ErrTooLong")]
        [InlineData(87601, -59, "ErrTooLong")]
        [InlineData(int.MaxValue, 0, "ErrTooLong")]
        [InlineData(int.MaxValue, int.MaxValue, "ErrTooLong")]
        [InlineData(int.MinValue, 0, "ErrTooShort")]
        [InlineData(int.MinValue, int.MinValue, "ErrTooShort")]
        public void Countdown_bounds(int h, int m, string error)
        {
            var r = Schedule.FromCountdown(h, m, SummerNoonUtc);
            Assert.Equal(error == null, r.Ok);
            Assert.Equal(error, r.ErrorKey);
        }

        [Fact]
        public void Countdown_exact_lower_bound_is_sixty_seconds()
        {
            var r = Schedule.FromCountdown(0, 1, SummerNoonUtc);
            Assert.True(r.Ok);
            Assert.Equal(60, r.Seconds);
            Assert.Equal(Utc(2026, 6, 1, 10, 1), r.TargetUtc);
        }

        [Fact]
        public void Countdown_exact_upper_bound_is_ten_years()
        {
            var r = Schedule.FromCountdown(87600, 0, SummerNoonUtc);
            Assert.True(r.Ok);
            Assert.Equal(Schedule.MaxSeconds, r.Seconds);
            Assert.Equal(SummerNoonUtc.AddSeconds(Schedule.MaxSeconds), r.TargetUtc);
        }

        [Fact]
        public void Countdown_result_is_utc_and_target_is_its_local_time()
        {
            var r = Schedule.FromCountdown(2, 15, SummerNoonUtc);
            Assert.Equal(8100, r.Seconds);
            Assert.Equal(DateTimeKind.Utc, r.TargetUtc.Kind);
            Assert.Equal(SummerNoonUtc.AddSeconds(8100), r.TargetUtc);
            Assert.Equal(DateTimeKind.Local, r.Target.Kind);
            Assert.Equal(r.TargetUtc.ToLocalTime(), r.Target);
        }

        [Fact]
        public void Countdown_counts_from_the_instant_not_from_the_wall_clock_kind()
        {
            // The same instant given as UTC and as local must give the same target.
            var local = SummerNoonUtc.ToLocalTime();
            Assert.Equal(Schedule.FromCountdown(1, 0, SummerNoonUtc).TargetUtc, Schedule.FromCountdown(1, 0, local).TargetUtc);
        }

        [Fact]
        public void Invalid_results_carry_no_target_and_no_seconds()
        {
            var r = Schedule.FromCountdown(0, 0, SummerNoonUtc);
            Assert.False(r.Ok);
            Assert.Equal(0, r.Seconds);
            Assert.Equal(default(DateTime), r.TargetUtc);
        }

        [Fact]
        public void Valid_results_have_no_error_key()
        {
            Assert.Null(Schedule.FromCountdown(0, 5, SummerNoonUtc).ErrorKey);
            Assert.Null(Schedule.FromTarget(new DateTime(2026, 6, 1), 13, 0, SummerNoonUtc, Berlin).ErrorKey);
        }

        [Fact]
        public void Target_exactly_sixty_seconds_ahead_is_accepted_and_59_is_too_soon()
        {
            // 12:01 Berlin = 10:01 UTC.
            var ok = Schedule.FromTarget(new DateTime(2026, 6, 1), 12, 1, SummerNoonUtc, Berlin);
            Assert.True(ok.Ok);
            Assert.Equal(60, ok.Seconds);
            Assert.Equal(Utc(2026, 6, 1, 10, 1), ok.TargetUtc);

            var soon = Schedule.FromTarget(new DateTime(2026, 6, 1), 12, 1, SummerNoonUtc.AddSeconds(1), Berlin);
            Assert.False(soon.Ok);
            Assert.Equal("ErrTooSoon", soon.ErrorKey);
            Assert.Equal("ErrTooSoon", Schedule.FromTarget(new DateTime(2026, 6, 1), 12, 1, SummerNoonUtc.AddMilliseconds(1), Berlin).ErrorKey);
        }

        [Fact]
        public void Target_seconds_use_the_ceiling()
        {
            // 60.5 s ahead -> 61.
            var r = Schedule.FromTarget(new DateTime(2026, 6, 1), 12, 1, SummerNoonUtc.AddMilliseconds(-500), Berlin);
            Assert.Equal(61, r.Seconds);
            // 119.001 s ahead -> 120.
            r = Schedule.FromTarget(new DateTime(2026, 6, 1), 12, 2, SummerNoonUtc.AddMilliseconds(999), Berlin);
            Assert.Equal(120, r.Seconds);
            // Exactly whole seconds stay as they are.
            r = Schedule.FromTarget(new DateTime(2026, 6, 1), 12, 2, SummerNoonUtc, Berlin);
            Assert.Equal(120, r.Seconds);
        }

        [Fact]
        public void Target_in_the_past_and_now_are_past_not_too_soon()
        {
            Assert.Equal("ErrPast", Schedule.FromTarget(new DateTime(2026, 6, 1), 12, 0, SummerNoonUtc, Berlin).ErrorKey);
            Assert.Equal("ErrPast", Schedule.FromTarget(new DateTime(2026, 6, 1), 11, 59, SummerNoonUtc, Berlin).ErrorKey);
            Assert.Equal("ErrPast", Schedule.FromTarget(new DateTime(2026, 5, 31), 23, 59, SummerNoonUtc, Berlin).ErrorKey);
            Assert.Equal("ErrPast", Schedule.FromTarget(new DateTime(2020, 1, 1), 0, 0, SummerNoonUtc, Berlin).ErrorKey);
        }

        [Fact]
        public void Target_ignores_the_time_of_day_of_the_date_argument()
        {
            var r = Schedule.FromTarget(new DateTime(2026, 6, 1, 23, 59, 59), 13, 0, SummerNoonUtc, Berlin);
            Assert.True(r.Ok);
            Assert.Equal(3600, r.Seconds);
        }

        [Fact]
        public void Target_exact_ten_years_ok_and_one_minute_more_too_long()
        {
            // 2026-06-01 12:00 Berlin plus 3650 days is 2036-05-29 12:00 Berlin, also summer time.
            var date = new DateTime(2026, 6, 1).AddDays(3650);
            var ok = Schedule.FromTarget(date, 12, 0, SummerNoonUtc, Berlin);
            Assert.True(ok.Ok);
            Assert.Equal(Schedule.MaxSeconds, ok.Seconds);
            var far = Schedule.FromTarget(date, 12, 1, SummerNoonUtc, Berlin);
            Assert.Equal("ErrTooLong", far.ErrorKey);
        }

        [Fact]
        public void Target_result_is_utc_and_target_is_its_local_time()
        {
            var r = Schedule.FromTarget(new DateTime(2026, 6, 1), 14, 30, SummerNoonUtc, Berlin);
            Assert.Equal(Utc(2026, 6, 1, 12, 30), r.TargetUtc);
            Assert.Equal(DateTimeKind.Utc, r.TargetUtc.Kind);
            Assert.Equal(DateTimeKind.Local, r.Target.Kind);
            Assert.Equal(r.TargetUtc.ToLocalTime(), r.Target);
        }

        [Fact]
        public void Target_uses_the_offset_of_the_target_day_not_of_now()
        {
            // Now is winter time (UTC+1); the target day is in summer time (UTC+2).
            var now = Utc(2027, 3, 20, 11, 0);
            var r = Schedule.FromTarget(new DateTime(2027, 4, 1), 12, 0, now, Berlin);
            Assert.Equal(Utc(2027, 4, 1, 10, 0), r.TargetUtc);
        }

        [Fact]
        public void Target_in_a_zone_without_daylight_saving_is_plain_offset_math()
        {
            var now = Utc(2026, 6, 1, 10, 0);
            var r = Schedule.FromTarget(new DateTime(2026, 6, 1), 11, 0, now, TimeZoneInfo.Utc);
            Assert.Equal(3600, r.Seconds);
            Assert.Equal(Utc(2026, 6, 1, 11, 0), r.TargetUtc);
        }

        [Fact]
        public void Skipped_spring_time_is_reported_even_when_it_would_be_in_the_past()
        {
            // The invalid-time check comes first; the user must hear that the time does not exist.
            var now = Utc(2027, 3, 28, 12, 0);
            Assert.Equal("ErrInvalidTime", Schedule.FromTarget(new DateTime(2027, 3, 28), 2, 30, now, Berlin).ErrorKey);
            Assert.Equal("ErrInvalidTime", Schedule.FromTarget(new DateTime(2027, 3, 28), 2, 0, now, Berlin).ErrorKey);
            Assert.Equal("ErrInvalidTime", Schedule.FromTarget(new DateTime(2027, 3, 28), 2, 59, now, Berlin).ErrorKey);
        }

        [Fact]
        public void Spring_gap_edges_are_valid()
        {
            var now = Utc(2027, 3, 27, 12, 0);
            // 01:59 is the last minute before the gap, 03:00 the first after it.
            var before = Schedule.FromTarget(new DateTime(2027, 3, 28), 1, 59, now, Berlin);
            var after = Schedule.FromTarget(new DateTime(2027, 3, 28), 3, 0, now, Berlin);
            Assert.True(before.Ok);
            Assert.True(after.Ok);
            Assert.Equal(Utc(2027, 3, 28, 0, 59), before.TargetUtc);
            Assert.Equal(Utc(2027, 3, 28, 1, 0), after.TargetUtc);
            // The two are one minute apart in real time.
            Assert.Equal(60, (after.TargetUtc - before.TargetUtc).TotalSeconds);
        }

        [Fact]
        public void Repeated_hour_takes_the_second_pass_when_the_first_is_exactly_now()
        {
            // 02:30 happens at 00:30 UTC (summer) and 01:30 UTC (winter).
            var date = new DateTime(2026, 10, 25);
            var atFirst = Schedule.FromTarget(date, 2, 30, Utc(2026, 10, 25, 0, 30), Berlin);
            Assert.True(atFirst.Ok);
            Assert.Equal(Utc(2026, 10, 25, 1, 30), atFirst.TargetUtc);
            Assert.Equal(3600, atFirst.Seconds);
        }

        [Fact]
        public void Repeated_hour_first_pass_still_wins_with_exactly_a_minute_left()
        {
            var r = Schedule.FromTarget(new DateTime(2026, 10, 25), 2, 30, Utc(2026, 10, 25, 0, 29), Berlin);
            Assert.True(r.Ok);
            Assert.Equal(Utc(2026, 10, 25, 0, 30), r.TargetUtc);
            Assert.Equal(60, r.Seconds);
        }

        [Fact]
        public void Repeated_hour_is_past_once_both_passes_are_over()
        {
            // 01:31 UTC is after the second 02:30.
            var r = Schedule.FromTarget(new DateTime(2026, 10, 25), 2, 30, Utc(2026, 10, 25, 1, 31), Berlin);
            Assert.Equal("ErrPast", r.ErrorKey);
        }

        [Fact]
        public void Times_around_the_repeated_hour_are_not_ambiguous()
        {
            // 01:59 (summer, once) and 03:00 (winter, once) around the repeated 02:00 hour.
            var now = Utc(2026, 10, 24, 12, 0);
            Assert.Equal(Utc(2026, 10, 24, 23, 59), Schedule.FromTarget(new DateTime(2026, 10, 25), 1, 59, now, Berlin).TargetUtc);
            Assert.Equal(Utc(2026, 10, 25, 2, 0), Schedule.FromTarget(new DateTime(2026, 10, 25), 3, 0, now, Berlin).TargetUtc);
        }

        [Fact]
        public void Target_across_the_autumn_change_counts_the_extra_hour()
        {
            // 00:00 summer (22:00 UTC) to 04:00 winter (03:00 UTC) is five real hours.
            var r = Schedule.FromTarget(new DateTime(2026, 10, 25), 4, 0, Utc(2026, 10, 24, 22, 0), Berlin);
            Assert.Equal(5 * 3600, r.Seconds);
        }

        [Fact]
        public void Target_across_the_spring_change_loses_the_skipped_hour()
        {
            // 00:00 winter (23:00 UTC) to 04:00 summer (02:00 UTC) is three real hours.
            var r = Schedule.FromTarget(new DateTime(2027, 3, 28), 4, 0, Utc(2027, 3, 27, 23, 0), Berlin);
            Assert.Equal(3 * 3600, r.Seconds);
        }

        [Theory]
        [InlineData(PowerAction.Shutdown, 60, "/s /f /t 60")]
        [InlineData(PowerAction.Shutdown, 0, "/s /f /t 0")]
        [InlineData(PowerAction.Shutdown, 315360000, "/s /f /t 315360000")]
        [InlineData(PowerAction.Restart, 61, "/r /f /t 61")]
        [InlineData(PowerAction.Restart, 315360000, "/r /f /t 315360000")]
        [InlineData(PowerAction.Hibernate, 0, "/h /f")]
        [InlineData(PowerAction.Hibernate, 3600, "/h /f")]
        [InlineData(PowerAction.Hibernate, 315360000, "/h /f")]
        public void Args_for_every_action(PowerAction a, int seconds, string expected)
        {
            Assert.Equal(expected, Schedule.ArgsFor(a, seconds));
        }

        [Fact]
        public void Args_are_always_forced_and_carry_no_comment_or_reason()
        {
            foreach (PowerAction a in Enum.GetValues(typeof(PowerAction)))
            {
                var args = Schedule.ArgsFor(a, 90);
                Assert.Contains("/f", args.Split(' '));
                Assert.DoesNotContain("/c", args);
                Assert.DoesNotContain("/d", args);
            }
        }

        [Fact]
        public void Args_use_the_invariant_number_format()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Assert.Equal("/s /f /t 1234567", Schedule.ArgsFor(PowerAction.Shutdown, 1234567));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        [Fact]
        public void App_timing_is_defined_for_every_action()
        {
            foreach (PowerAction a in Enum.GetValues(typeof(PowerAction)))
                Assert.Equal(a == PowerAction.Hibernate, Schedule.IsAppTimed(a));
        }

        [Fact]
        public void Enum_names_match_what_settings_store()
        {
            // Settings persist these names; renaming one would orphan stored schedules.
            Assert.Equal(new[] { "Shutdown", "Restart", "Hibernate" }, Enum.GetNames(typeof(PowerAction)));
            Assert.Equal(new[] { "Countdown", "At" }, Enum.GetNames(typeof(TimeMode)));
        }
    }

    /// <summary>Loc is process-wide state, so these share the collection with the window tests.</summary>
    [Collection("window")]
    public class LocEdgeTests : IDisposable
    {
        readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        readonly CultureInfo _ui = CultureInfo.CurrentUICulture;

        public LocEdgeTests() { Loc.I.Apply("en"); }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _ui;
            Loc.I.Apply("en");
        }

        static string Span(string lang, TimeSpan t) { Loc.I.Apply(lang); return Loc.I.Span(t); }
        static string Clock(string lang, TimeSpan t) { Loc.I.Apply(lang); return Loc.I.Clock(t); }

        [Fact]
        public void Languages_are_en_and_de_in_that_order()
        {
            Assert.Equal(new[] { "en", "de" }, Loc.Languages);
        }

        [Theory]
        [InlineData(0, "0 s")]
        [InlineData(-1, "0 s")]
        [InlineData(-86400 * 3, "0 s")]
        [InlineData(1, "1 s")]
        [InlineData(45, "45 s")]
        [InlineData(59, "59 s")]
        [InlineData(60, "1 min")]
        [InlineData(119, "1 min")]
        [InlineData(3599, "59 min")]
        [InlineData(3600, "1 h")]
        [InlineData(3630, "1 h")]
        [InlineData(3660, "1 h 1 min")]
        [InlineData(5100, "1 h 25 min")]
        [InlineData(86399, "23 h 59 min")]
        [InlineData(86400, "1 d")]
        [InlineData(86400 + 59 * 60, "1 d")]
        [InlineData(86400 + 3600, "1 d 1 h")]
        [InlineData(86400 + 3 * 3600 + 20 * 60, "1 d 3 h")]
        [InlineData(2 * 86400, "2 d")]
        [InlineData(2 * 86400 + 30, "2 d")]
        [InlineData(400 * 86400, "400 d")]
        public void Span_in_english(int seconds, string expected)
        {
            Assert.Equal(expected, Span("en", TimeSpan.FromSeconds(seconds)));
        }

        [Theory]
        [InlineData(0, "0 Sek.")]
        [InlineData(-5, "0 Sek.")]
        [InlineData(45, "45 Sek.")]
        [InlineData(60, "1 Min.")]
        [InlineData(5100, "1 Std. 25 Min.")]
        [InlineData(86400, "1 Tag")]
        [InlineData(86400 + 3600, "1 Tag 1 Std.")]
        [InlineData(2 * 86400, "2 Tagen")]
        [InlineData(3 * 86400 + 4 * 3600 + 5 * 60, "3 Tagen 4 Std.")]
        public void Span_in_german_uses_singular_and_plural_day_forms(int seconds, string expected)
        {
            Assert.Equal(expected, Span("de", TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public void Span_drops_fractions_of_a_second()
        {
            Assert.Equal("0 s", Span("en", TimeSpan.FromMilliseconds(999)));
            Assert.Equal("59 s", Span("en", TimeSpan.FromMilliseconds(59999)));
        }

        [Theory]
        [InlineData(0, "0:00:00")]
        [InlineData(-1, "0:00:00")]
        [InlineData(-100000, "0:00:00")]
        [InlineData(1, "0:00:01")]
        [InlineData(59, "0:00:59")]
        [InlineData(60, "0:01:00")]
        [InlineData(3600, "1:00:00")]
        [InlineData(5071, "1:24:31")]
        [InlineData(86399, "23:59:59")]
        [InlineData(86400, "1 d 00:00:00")]
        [InlineData(86401, "1 d 00:00:01")]
        [InlineData(2 * 86400 + 3 * 3600 + 10 * 60, "2 d 03:10:00")]
        [InlineData(100 * 86400, "100 d 00:00:00")]
        public void Clock_in_english(int seconds, string expected)
        {
            Assert.Equal(expected, Clock("en", TimeSpan.FromSeconds(seconds)));
        }

        [Theory]
        [InlineData(0, "0:00:00")]
        [InlineData(-9, "0:00:00")]
        [InlineData(5071, "1:24:31")]
        [InlineData(86400, "1 Tag 00:00:00")]
        [InlineData(2 * 86400 + 3 * 3600 + 10 * 60, "2 Tage 03:10:00")]
        public void Clock_in_german(int seconds, string expected)
        {
            Assert.Equal(expected, Clock("de", TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public void Clock_truncates_fractions_and_ignores_the_regional_format()
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("0:00:01", Clock("en", TimeSpan.FromMilliseconds(1999)));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("1:00:00", Clock("de", TimeSpan.FromHours(1)));
        }

        [Fact]
        public void Indexer_returns_the_active_language()
        {
            Loc.I.Apply("en");
            Assert.Equal("Hours", Loc.I["Hours"]);
            Loc.I.Apply("de");
            Assert.Equal("Stunden", Loc.I["Hours"]);
        }

        [Fact]
        public void Unknown_key_returns_the_key_itself_in_every_language()
        {
            foreach (var lang in Loc.Languages)
            {
                Loc.I.Apply(lang);
                Assert.Equal("NoSuchKey", Loc.I["NoSuchKey"]);
                Assert.Equal("", Loc.I[""]);
            }
        }

        [Fact]
        public void Indexer_falls_back_to_english_when_the_active_table_lacks_the_key()
        {
            var tables = (Dictionary<string, Dictionary<string, string>>)typeof(Loc)
                .GetField("_tables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(Loc.I);
            var de = tables["de"];
            string saved = de["Hours"];
            try
            {
                de.Remove("Hours");
                Loc.I.Apply("de");
                Assert.Equal("Hours", Loc.I["Hours"]);
            }
            finally { de["Hours"] = saved; }
            Assert.Equal("Stunden", Loc.I["Hours"]);
        }

        [Fact]
        public void LoadTable_of_an_unknown_language_is_empty()
        {
            Assert.Empty(Loc.LoadTable("xx"));
            Assert.Empty(Loc.LoadTable(""));
        }

        [Fact]
        public void LoadTable_returns_a_fresh_copy_each_call()
        {
            var a = Loc.LoadTable("en");
            a["Hours"] = "changed";
            Assert.Equal("Hours", Loc.LoadTable("en")["Hours"]);
            Assert.Equal("Hours", Loc.I["Hours"]);
        }

        [Theory]
        [InlineData("de-DE", "de")]
        [InlineData("de-AT", "de")]
        [InlineData("de-CH", "de")]
        [InlineData("de", "de")]
        [InlineData("en-US", "en")]
        [InlineData("en-GB", "en")]
        [InlineData("fr-FR", "en")]
        [InlineData("zh-CN", "en")]
        [InlineData("ja-JP", "en")]
        [InlineData("", "en")]
        public void Resolve_maps_the_ui_culture_to_a_supported_language(string culture, string expected)
        {
            Assert.Equal(expected, Loc.Resolve(CultureInfo.GetCultureInfo(culture)));
        }

        [Theory]
        [InlineData("de-DE", "de")]
        [InlineData("fr-FR", "en")]
        public void System_setting_resolves_the_ui_culture(string ui, string expected)
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui);
            Loc.I.Apply(expected == "de" ? "en" : "de");
            Loc.I.Apply("system");
            Assert.Equal(expected, Loc.I.Language);
        }

        [Theory]
        [InlineData("xx")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("DE")]
        [InlineData("fr")]
        public void Unknown_setting_falls_back_to_the_ui_culture(string setting)
        {
            // Codes are matched exactly; anything else means "follow the system".
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            Loc.I.Apply("en");
            Loc.I.Apply(setting);
            Assert.Equal("de", Loc.I.Language);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Loc.I.Apply(setting);
            Assert.Equal("en", Loc.I.Language);
        }

        [Fact]
        public void Explicit_language_wins_over_the_ui_culture()
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            Loc.I.Apply("en");
            Assert.Equal("en", Loc.I.Language);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Loc.I.Apply("de");
            Assert.Equal("de", Loc.I.Language);
        }

        [Fact]
        public void Apply_raises_both_events_once_per_real_change_only()
        {
            Loc.I.Apply("en");
            var props = new List<string>();
            int changed = 0;
            PropertyChangedEventHandler ph = (s, e) => props.Add(e.PropertyName);
            EventHandler ch = (s, e) => changed++;
            Loc.I.PropertyChanged += ph;
            Loc.I.Changed += ch;
            try
            {
                Loc.I.Apply("en");
                Assert.Empty(props);
                Assert.Equal(0, changed);

                Loc.I.Apply("de");
                Assert.Equal(new[] { "Item[]" }, props);
                Assert.Equal(1, changed);

                Loc.I.Apply("de");
                Assert.Single(props);
                Assert.Equal(1, changed);

                Loc.I.Apply("en");
                Assert.Equal(2, props.Count);
                Assert.Equal(2, changed);
            }
            finally
            {
                Loc.I.PropertyChanged -= ph;
                Loc.I.Changed -= ch;
            }
        }

        [Fact]
        public void Apply_system_that_resolves_to_the_same_language_raises_nothing()
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Loc.I.Apply("en");
            int changed = 0;
            EventHandler ch = (s, e) => changed++;
            Loc.I.Changed += ch;
            try
            {
                Loc.I.Apply("system");
                Loc.I.Apply("xx");
                Assert.Equal(0, changed);
            }
            finally { Loc.I.Changed -= ch; }
        }

        [Fact]
        public void Handlers_see_the_new_language_when_called()
        {
            Loc.I.Apply("en");
            string seen = null;
            EventHandler ch = (s, e) => seen = Loc.I["Hours"];
            Loc.I.Changed += ch;
            try { Loc.I.Apply("de"); }
            finally { Loc.I.Changed -= ch; }
            Assert.Equal("Stunden", seen);
        }

        [Fact]
        public void Culture_matches_the_language_unless_the_regional_format_already_does()
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            Loc.I.Apply("de");
            Assert.Equal("de-DE", Loc.I.Culture.Name);
            Loc.I.Apply("en");
            Assert.Equal("en-GB", Loc.I.Culture.Name);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            Loc.I.Apply("de");
            Assert.Equal("de-AT", Loc.I.Culture.Name);
            Loc.I.Apply("en");
            Assert.Equal("en-US", Loc.I.Culture.Name);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("en-US", Loc.I.Culture.Name);
            Loc.I.Apply("de");
            Assert.Equal("de-DE", Loc.I.Culture.Name);
        }

        [Fact]
        public void Format_fills_placeholders_for_the_active_language()
        {
            Loc.I.Apply("en");
            Assert.Equal("Increase Hours", Loc.I.F("IncreaseFmt", "Hours"));
            Assert.Equal("exit code 5", Loc.I.F("ExitCodeFmt", 5));
            Loc.I.Apply("de");
            Assert.Equal("Stunden erhöhen", Loc.I.F("IncreaseFmt", "Stunden"));
            Assert.Equal("Rückgabecode 1116", Loc.I.F("ExitCodeFmt", 1116));
        }

        [Fact]
        public void Format_of_an_unknown_key_uses_the_key_as_the_pattern()
        {
            Assert.Equal("Nothing", Loc.I.F("Nothing"));
        }

        [Fact]
        public void Date_and_time_text_split_app_language_and_regional_format()
        {
            // Friday 2026-10-16 15:05.
            var d = new DateTime(2026, 10, 16, 15, 5, 0);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Loc.I.Apply("de");
            Assert.StartsWith("Fr, ", Loc.I.DateText(d));
            Assert.EndsWith("10/16/2026", Loc.I.DateText(d));
            Assert.Equal("3:05 PM", Loc.I.TimeText(d));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Loc.I.Apply("en");
            Assert.Equal("Fri, 16.10.2026", Loc.I.DateText(d));
            Assert.Equal("15:05", Loc.I.TimeText(d));
        }
    }

    /// <summary>Settings store edge cases. Load and Save log failures, so this shares the log folder collection.</summary>
    [Collection("window")]
    public class SettingsEdgeTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownTests-" + Guid.NewGuid().ToString("N"));
        string FilePath => Path.Combine(_dir, SettingsStore.FileName);

        public SettingsEdgeTests() { Directory.CreateDirectory(_dir); }

        public void Dispose()
        {
            try
            {
                foreach (var f in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_dir, true);
            }
            catch { }
        }

        SettingsStore Store() => new SettingsStore(FilePath);

        [Fact]
        public void New_settings_have_the_documented_defaults()
        {
            var s = new Settings();
            Assert.Equal(Settings.CurrentVersion, s.Version);
            Assert.Equal("system", s.Theme);
            Assert.Equal("Shutdown", s.Action);
            Assert.Equal("Countdown", s.Mode);
            Assert.Equal(1, s.Hours);
            Assert.Equal(0, s.Minutes);
            Assert.Null(s.Left);
            Assert.Null(s.Top);
            Assert.Null(s.PendingAction);
            Assert.Null(s.PendingDue);
            Assert.Null(s.PendingBoot);
        }

        [Fact]
        public void Default_actions_and_modes_name_real_enum_values()
        {
            var s = new Settings();
            Assert.True(Enum.IsDefined(typeof(PowerAction), s.Action));
            Assert.True(Enum.IsDefined(typeof(TimeMode), s.Mode));
        }

        [Fact]
        public void Current_version_is_one()
        {
            // Raising it needs a migration for files already out there.
            Assert.Equal(1, Settings.CurrentVersion);
        }

        [Fact]
        public void Missing_file_gives_defaults_and_creates_nothing()
        {
            var store = Store();
            var s = store.Load();
            Assert.Equal(1, s.Hours);
            Assert.False(store.KeepFile);
            Assert.False(File.Exists(FilePath));
            Assert.Empty(Directory.GetFileSystemEntries(_dir));
        }

        [Fact]
        public void Save_writes_the_current_version_whatever_the_object_says()
        {
            var s = new Settings { Version = 0 };
            Assert.True(Store().Save(s));
            Assert.Equal(Settings.CurrentVersion, s.Version);
            Assert.Matches("\"Version\":\\s*1\\b", File.ReadAllText(FilePath));
            Assert.Equal(1, Store().Load().Version);
        }

        [Fact]
        public void Save_creates_missing_folders()
        {
            var path = Path.Combine(_dir, "a", "b", "c", SettingsStore.FileName);
            var store = new SettingsStore(path);
            Assert.True(store.Save(new Settings { Hours = 4 }));
            Assert.True(File.Exists(path));
            Assert.Equal(4, store.Load().Hours);
        }

        [Fact]
        public void Save_leaves_no_temp_file_on_create_or_overwrite()
        {
            var store = Store();
            Assert.True(store.Save(new Settings { Hours = 1 }));
            Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
            Assert.True(store.Save(new Settings { Hours = 2 }));
            Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
            Assert.Equal(2, store.Load().Hours);
        }

        [Fact]
        public void Overwrite_replaces_the_content_completely()
        {
            var store = Store();
            store.Save(new Settings { Hours = 12, Minutes = 30, Theme = "dark", PendingAction = "Restart", PendingDue = 5, PendingBoot = "boot:1" });
            store.Save(new Settings());
            var back = store.Load();
            Assert.Equal(1, back.Hours);
            Assert.Equal(0, back.Minutes);
            Assert.Equal("system", back.Theme);
            Assert.Null(back.PendingAction);
            Assert.Null(back.PendingDue);
            Assert.Null(back.PendingBoot);
        }

        [Fact]
        public void A_stale_temp_file_from_a_crashed_save_is_replaced()
        {
            File.WriteAllText(FilePath + ".new", "garbage");
            Assert.True(Store().Save(new Settings { Hours = 7 }));
            Assert.False(File.Exists(FilePath + ".new"));
            Assert.Equal(7, Store().Load().Hours);
        }

        [Fact]
        public void Saved_file_is_utf8_json_without_a_byte_order_mark()
        {
            Store().Save(new Settings { PendingBoot = "boot:\u00e4\u00f6" });
            var bytes = File.ReadAllBytes(FilePath);
            var text = new UTF8Encoding(false).GetString(bytes);
            Assert.StartsWith("{", text);
            Assert.Equal("boot:\u00e4\u00f6", Store().Load().PendingBoot);

            File.WriteAllText(FilePath, "{\"Version\":1,\"Hours\":9}", new UTF8Encoding(false));
            Assert.Equal(9, Store().Load().Hours);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n")]
        [InlineData("[]")]
        [InlineData("42")]
        [InlineData("\"text\"")]
        [InlineData("{")]
        [InlineData("{\"Version\":1,")]
        [InlineData("{\"Hours\":\"many\"}")]
        [InlineData("not json at all")]
        public void Unusable_file_content_gives_defaults_and_keeps_saving_possible(string content)
        {
            File.WriteAllText(FilePath, content);
            var store = Store();
            var s = store.Load();
            Assert.Equal(1, s.Hours);
            Assert.Equal("system", s.Theme);
            Assert.Equal("Countdown", s.Mode);
            Assert.False(store.KeepFile);
            Assert.True(store.Save(s));
            Assert.Equal(1, Store().Load().Hours);
        }

        [Fact]
        public void Empty_object_gives_defaults_not_zero_values()
        {
            File.WriteAllText(FilePath, "{}");
            var s = Store().Load();
            Assert.Equal(1, s.Version);
            Assert.Equal(1, s.Hours);
            Assert.Equal(0, s.Minutes);
            Assert.Equal("system", s.Theme);
            Assert.Equal("Shutdown", s.Action);
            Assert.Equal("Countdown", s.Mode);
        }

        [Fact]
        public void Unknown_fields_are_ignored_and_known_ones_kept()
        {
            File.WriteAllText(FilePath,
                "{\"Version\":1,\"Hours\":3,\"Extra\":{\"a\":[1,2,3]},\"Minutes\":20,\"Another\":\"x\",\"Theme\":\"dark\"}");
            var store = Store();
            var s = store.Load();
            Assert.Equal(3, s.Hours);
            Assert.Equal(20, s.Minutes);
            Assert.Equal("dark", s.Theme);
            Assert.False(store.KeepFile);
        }

        [Fact]
        public void Optional_values_roundtrip_and_clear_again()
        {
            var store = Store();
            var s = new Settings
            {
                Left = -1234.5678, Top = 0.25,
                PendingAction = "Restart", PendingDue = 7200000, PendingBoot = "boot:7",
            };
            Assert.True(store.Save(s));
            var back = store.Load();
            Assert.Equal(-1234.5678, back.Left);
            Assert.Equal(0.25, back.Top);
            Assert.Equal("Restart", back.PendingAction);
            Assert.Equal(7200000L, back.PendingDue);
            Assert.Equal("boot:7", back.PendingBoot);

            back.Left = null; back.Top = null; back.PendingAction = null; back.PendingDue = null; back.PendingBoot = null;
            Assert.True(store.Save(back));
            var cleared = store.Load();
            Assert.Null(cleared.Left);
            Assert.Null(cleared.Top);
            Assert.Null(cleared.PendingAction);
            Assert.Null(cleared.PendingDue);
            Assert.Null(cleared.PendingBoot);
        }

        [Fact]
        public void Extreme_numbers_roundtrip()
        {
            var store = Store();
            store.Save(new Settings { Hours = int.MaxValue, Minutes = int.MinValue, PendingDue = long.MaxValue, Left = double.MaxValue });
            var back = store.Load();
            Assert.Equal(int.MaxValue, back.Hours);
            Assert.Equal(int.MinValue, back.Minutes);
            Assert.Equal(long.MaxValue, back.PendingDue);
            Assert.Equal(double.MaxValue, back.Left);
        }

        [Fact]
        public void Explicit_json_null_for_optional_fields_loads_as_null()
        {
            File.WriteAllText(FilePath, "{\"Left\":null,\"Top\":null,\"PendingAction\":null,\"PendingDue\":null,\"PendingBoot\":null}");
            var s = Store().Load();
            Assert.Null(s.Left);
            Assert.Null(s.PendingDue);
            Assert.Null(s.PendingAction);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(99)]
        [InlineData(int.MaxValue)]
        public void Newer_file_gives_defaults_sets_keep_file_and_is_never_written(int version)
        {
            string content = "{\"Version\":" + version + ",\"Theme\":\"dark\",\"Hours\":9,\"NewThing\":true}";
            File.WriteAllText(FilePath, content);
            var before = File.ReadAllBytes(FilePath);
            var store = Store();
            var s = store.Load();
            Assert.True(store.KeepFile);
            Assert.Equal("system", s.Theme);
            Assert.Equal(1, s.Hours);
            s.Hours = 5;
            Assert.False(store.Save(s));
            Assert.False(store.Save(s));
            Assert.Equal(before, File.ReadAllBytes(FilePath));
            Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(-5)]
        public void Current_or_older_file_does_not_set_keep_file(int version)
        {
            File.WriteAllText(FilePath, "{\"Version\":" + version + ",\"Hours\":6}");
            var store = Store();
            Assert.Equal(6, store.Load().Hours);
            Assert.False(store.KeepFile);
            Assert.True(store.Save(new Settings { Hours = 8 }));
            Assert.Equal(8, Store().Load().Hours);
        }

        [Fact]
        public void Fresh_store_starts_without_keep_file()
        {
            Assert.False(Store().KeepFile);
        }

        [Fact]
        public void Save_into_a_path_that_is_a_directory_returns_false_without_throwing()
        {
            Directory.CreateDirectory(FilePath);
            var store = Store();
            Assert.False(store.Save(new Settings()));
            Assert.True(Directory.Exists(FilePath));
            // Loading from it is no failure either.
            Assert.Equal(1, store.Load().Hours);
        }

        [Fact]
        public void Save_below_a_file_returns_false_without_throwing()
        {
            string blocker = Path.Combine(_dir, "blocker");
            File.WriteAllText(blocker, "x");
            var store = new SettingsStore(Path.Combine(blocker, "sub", SettingsStore.FileName));
            Assert.False(store.Save(new Settings()));
            Assert.Equal("x", File.ReadAllText(blocker));
        }

        [Theory]
        [InlineData("bad|name?.json")]
        [InlineData("bad\0name.json")]
        public void Save_to_an_invalid_file_name_returns_false_without_throwing(string name)
        {
            // Plain concatenation: Path.Combine itself rejects some of these characters.
            var store = new SettingsStore(_dir + Path.DirectorySeparatorChar + name);
            Assert.False(store.Save(new Settings()));
            Assert.Equal(1, store.Load().Hours);
        }

        [Fact]
        public void Save_of_a_read_only_file_returns_false_and_leaves_it_alone()
        {
            var store = Store();
            Assert.True(store.Save(new Settings { Hours = 2 }));
            File.SetAttributes(FilePath, FileAttributes.ReadOnly);
            Assert.False(store.Save(new Settings { Hours = 9 }));
            Assert.Equal(2, Store().Load().Hours);
        }

        [Fact]
        public void Load_of_a_locked_file_gives_defaults_without_throwing()
        {
            Store().Save(new Settings { Hours = 5 });
            using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var s = Store().Load();
                Assert.Equal(1, s.Hours);
            }
            Assert.Equal(5, Store().Load().Hours);
        }

        [Fact]
        public void Two_loads_give_independent_objects()
        {
            Store().Save(new Settings { Hours = 5 });
            var a = Store().Load();
            var b = Store().Load();
            a.Hours = 99;
            Assert.Equal(5, b.Hours);
        }

        [Fact]
        public void Default_store_lives_under_appdata_shutdown_and_never_beside_the_exe()
        {
            var store = SettingsStore.Default();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.Equal(Path.Combine(appData, "Shutdown", "Shutdown.settings.json"), store.FilePath);
            Assert.Equal("Shutdown.settings.json", SettingsStore.FileName);
            Assert.False(store.FilePath.StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void App_paths_are_below_appdata_shutdown()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.Equal(Path.Combine(appData, "Shutdown"), AppPaths.AppData);
            Assert.Equal(Path.Combine(appData, "Shutdown", "logs"), AppPaths.LogDir);
        }

        [Fact]
        public void File_path_is_kept_as_given()
        {
            Assert.Equal(FilePath, Store().FilePath);
        }
    }

    [Collection("window")]
    public class LogEdgeTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownTests-" + Guid.NewGuid().ToString("N"));
        readonly string _before = Log.Dir;

        public LogEdgeTests() { Directory.CreateDirectory(_dir); Log.Dir = Path.Combine(_dir, "logs"); }

        public void Dispose()
        {
            Log.Dir = _before;
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Write_creates_the_folder_and_a_timestamped_line()
        {
            Assert.False(Directory.Exists(Log.Dir));
            Log.Write("hello world");
            var lines = File.ReadAllLines(Log.FilePath);
            Assert.Single(lines);
            Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d  hello world$", lines[0]);
            Assert.Equal("shutdown.log", Path.GetFileName(Log.FilePath));
        }

        [Fact]
        public void Write_appends_in_order()
        {
            Log.Write("one");
            Log.Write("two");
            Log.Write("three");
            var lines = File.ReadAllLines(Log.FilePath);
            Assert.Equal(3, lines.Length);
            Assert.EndsWith("one", lines[0]);
            Assert.EndsWith("two", lines[1]);
            Assert.EndsWith("three", lines[2]);
        }

        [Fact]
        public void Write_with_an_exception_adds_its_type_message_and_stack()
        {
            Exception ex;
            try { throw new InvalidOperationException("boom detail"); }
            catch (Exception e) { ex = e; }
            Log.Write("it failed", ex);
            string text = File.ReadAllText(Log.FilePath);
            Assert.Contains("it failed", text);
            Assert.Contains("System.InvalidOperationException", text);
            Assert.Contains("boom detail", text);
            Assert.Contains("Write_with_an_exception_adds_its_type_message_and_stack", text);
            // The message line comes first, the exception on the following lines.
            Assert.True(text.IndexOf("it failed", StringComparison.Ordinal) < text.IndexOf("boom detail", StringComparison.Ordinal));
        }

        [Fact]
        public void Write_with_a_nested_exception_includes_the_inner_one()
        {
            Log.Write("outer", new RunnerException("C:\\x\\shutdown.exe", new System.ComponentModel.Win32Exception(2)));
            string text = File.ReadAllText(Log.FilePath);
            Assert.Contains("shutdown.exe", text);
            Assert.Contains("Win32Exception", text);
        }

        [Fact]
        public void Write_keeps_non_ascii_text()
        {
            Log.Write("pr\u00fcfung \u00e4\u00f6\u00fc");
            Assert.Contains("pr\u00fcfung \u00e4\u00f6\u00fc", File.ReadAllText(Log.FilePath, Encoding.UTF8));
        }

        [Fact]
        public void Write_into_an_unusable_folder_does_not_throw()
        {
            string blocker = Path.Combine(_dir, "blocker");
            File.WriteAllText(blocker, "x");
            // A file where the folder should be, and an illegal path.
            Log.Dir = Path.Combine(blocker, "sub");
            Log.Write("nobody will read this");
            Log.Write("with exception", new Exception("e"));
            Log.Dir = "bad|path?:*";
            Log.Write("still no throw");
            Assert.Equal("x", File.ReadAllText(blocker));
        }

        [Fact]
        public void Write_when_the_log_file_is_locked_does_not_throw()
        {
            Log.Write("first");
            using (new FileStream(Log.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Log.Write("second");
            Assert.Single(File.ReadAllLines(Log.FilePath));
        }

        [Fact]
        public void Dir_defaults_to_the_app_log_folder_when_unset()
        {
            Log.Dir = null;
            Assert.Equal(AppPaths.LogDir, Log.Dir);
            Assert.Equal(Path.Combine(AppPaths.LogDir, "shutdown.log"), Log.FilePath);
        }

        [Fact]
        public void Full_log_moves_to_dot_one_and_replaces_the_older_copy()
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(Log.FilePath + ".1", "OLD");
            File.WriteAllText(Log.FilePath, new string('x', 512 * 1024 + 1));
            Log.Write("fresh");
            Assert.Equal(512 * 1024 + 1, new FileInfo(Log.FilePath + ".1").Length);
            Assert.DoesNotContain("OLD", File.ReadAllText(Log.FilePath + ".1"));
            var lines = File.ReadAllLines(Log.FilePath);
            Assert.Single(lines);
            Assert.EndsWith("fresh", lines[0]);
        }

        [Fact]
        public void Log_of_exactly_the_limit_is_not_rotated_yet()
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(Log.FilePath, new string('x', 512 * 1024));
            Log.Write("more");
            Assert.False(File.Exists(Log.FilePath + ".1"));
            Assert.EndsWith("more" + Environment.NewLine, File.ReadAllText(Log.FilePath));
        }

        [Fact]
        public void Concurrent_writers_lose_no_lines()
        {
            var threads = Enumerable.Range(0, 8).Select(t => new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 25; i++) Log.Write("t" + t + "-" + i);
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            var lines = File.ReadAllLines(Log.FilePath);
            Assert.Equal(200, lines.Length);
            Assert.Equal(200, lines.Select(l => l.Substring(l.IndexOf("  ", StringComparison.Ordinal) + 2)).Distinct().Count());
        }
    }
}
