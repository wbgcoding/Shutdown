using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    /// <summary>Loc is process-wide state, so these share the collection with the window tests.</summary>
    [Collection("window")]
    [Trait("Layer", "Logic")]
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

    [Trait("Layer", "Logic")]
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
}
