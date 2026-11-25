using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shutdown.Ui;
using Xunit;

namespace Shutdown.Tests
{
    /// <summary>Palette and resources of Theme; Theme.Override is process-wide, so every test restores it.</summary>
    [Collection("window")]
    [Trait("Layer", "Ui")]
    public class ThemeTests
    {
        static void WithOverride(string value, Action<ResourceDictionary> check)
        {
            var before = Theme.Override;
            try
            {
                Theme.Override = value;
                var res = new ResourceDictionary();
                Theme.Apply(res);
                check(res);
            }
            finally
            {
                Theme.Override = before;
                Theme.Apply(Application.Current.Resources);
            }
        }

        static Color C(ResourceDictionary r, string key) => ((SolidColorBrush)r[key]).Color;
        static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s);

        static readonly string[] Keys =
        {
            "Bg", "Card", "CardBorder", "Text", "TextSub", "Control", "ControlHover", "ControlHoverText", "ControlPressed",
            "DisabledFill", "ControlBorder", "Accent", "AccentHover", "AccentText", "Warn", "Urgent", "Danger", "DangerSoft",
            "Focus", "Disabled"
        };

        [Theory]
        [InlineData("dark")]
        [InlineData("light")]
        public void Both_themes_define_every_brush_frozen(string theme) => Sta.Run(() =>
        {
            if (SystemParameters.HighContrast) return;
            WithOverride(theme, r =>
            {
                foreach (var k in Keys)
                {
                    var b = Assert.IsType<SolidColorBrush>(r[k]);
                    Assert.True(b.IsFrozen, k);
                }
            });
        });

        [Fact]
        public void Dark_theme_uses_the_dark_palette() => Sta.Run(() =>
        {
            if (SystemParameters.HighContrast) return;
            WithOverride("dark", r =>
            {
                Assert.True(Theme.IsDark);
                Assert.Equal(Hex("#202020"), C(r, "Bg"));
                Assert.Equal(Hex("#2B2B2B"), C(r, "Card"));
                Assert.Equal(Hex("#FFFFFF"), C(r, "Text"));
                Assert.Equal(Hex("#FCE100"), C(r, "Warn"));
                Assert.Equal(Hex("#FF6B6B"), C(r, "Urgent"));
                Assert.Equal(Hex("#FF99A4"), C(r, "Danger"));
                Assert.Equal(Hex("#442726"), C(r, "DangerSoft"));
                var p = Theme.AccentPalette();
                Assert.Equal(p[4], C(r, "Accent"));
                Assert.Equal(p[3], C(r, "AccentHover"));
            });
        });

        [Fact]
        public void Light_theme_uses_the_light_palette() => Sta.Run(() =>
        {
            if (SystemParameters.HighContrast) return;
            WithOverride("light", r =>
            {
                Assert.False(Theme.IsDark);
                Assert.Equal(Hex("#F3F3F3"), C(r, "Bg"));
                Assert.Equal(Hex("#FFFFFF"), C(r, "Card"));
                Assert.Equal(Hex("#1B1B1B"), C(r, "Text"));
                Assert.Equal(Hex("#9D5D00"), C(r, "Warn"));
                Assert.Equal(Hex("#C42B1C"), C(r, "Urgent"));
                Assert.Equal(Hex("#B3261E"), C(r, "Danger"));
                Assert.Equal(Hex("#FDE7E9"), C(r, "DangerSoft"));
                var p = Theme.AccentPalette();
                Assert.Equal(p[4], C(r, "Accent"));
                Assert.Equal(p[5], C(r, "AccentHover"));
            });
        });

        [Theory]
        [InlineData("system")]
        [InlineData("bogus")]
        [InlineData("")]
        [InlineData(null)]
        public void Any_other_override_follows_windows(string value) => Sta.Run(() =>
        {
            if (SystemParameters.HighContrast) return;
            WithOverride(value, r => Assert.Equal(Theme.SystemPrefersDark(), Theme.IsDark));
        });

        [Fact]
        public void Applying_again_replaces_the_brushes_in_place() => Sta.Run(() =>
        {
            if (SystemParameters.HighContrast) return;
            var before = Theme.Override;
            try
            {
                var r = new ResourceDictionary();
                Theme.Override = "dark";
                Theme.Apply(r);
                int count = r.Count;
                var dark = r["Bg"];
                Theme.Override = "light";
                Theme.Apply(r);
                Assert.Equal(count, r.Count);
                Assert.NotSame(dark, r["Bg"]);
                Assert.Equal(Hex("#F3F3F3"), C(r, "Bg"));
            }
            finally { Theme.Override = before; Theme.Apply(Application.Current.Resources); }
        });

        [Fact]
        public void The_accent_palette_has_seven_opaque_colours() => Sta.Run(() =>
        {
            var p = Theme.AccentPalette();
            Assert.Equal(7, p.Length);
            Assert.All(p, c => Assert.Equal(255, c.A));
        });

        [Theory]
        [InlineData("#005FB8", "#004275", "#FFFFFF")] // Windows blue, light mode: white text
        [InlineData("#005FB8", "#0078D4", "#FFFFFF")] // Windows blue, dark mode hover
        [InlineData("#E3A600", "#FFB900", "#000000")] // yellow accent: white would read at about 2:1
        [InlineData("#00B7C3", "#2DD7E0", "#000000")] // light teal
        [InlineData("#881798", "#B146C2", "#FFFFFF")] // purple
        public void Accent_text_is_white_or_black_whichever_reads_better(string fill, string hover, string expected) =>
            Assert.Equal(Hex(expected), Theme.TextOn(Hex(fill), Hex(hover)));

        [Fact]
        public void Contrast_matches_the_wcag_formula()
        {
            Assert.Equal(21.0, Theme.Contrast(Colors.White, Colors.Black), 3);
            Assert.Equal(1.0, Theme.Contrast(Hex("#777777"), Hex("#777777")), 3);
            // #767676 on white is the classic 4.54:1 threshold grey.
            Assert.Equal(4.54, Theme.Contrast(Hex("#767676"), Colors.White), 2);
        }

        [Theory]
        [InlineData("light")]
        [InlineData("dark")]
        public void Text_colours_reach_aa_contrast_on_their_surfaces(string theme) => Sta.Run(() =>
        {
            if (SystemParameters.HighContrast) return;
            var before = Theme.Override;
            var res = new ResourceDictionary();
            try
            {
                Theme.Override = theme;
                Theme.Apply(res);
                Color C(string key) => ((SolidColorBrush)res[key]).Color;
                foreach (var (text, surface) in new[]
                {
                    ("Text", "Bg"), ("Text", "Card"), ("Text", "Control"), ("Text", "ControlHover"), ("Text", "DangerSoft"),
                    ("TextSub", "Bg"), ("TextSub", "Card"), ("Warn", "Card"), ("Urgent", "Card"),
                    ("Danger", "Card"), ("Danger", "DangerSoft"), ("CaptionCloseText", "CaptionCloseHover"),
                })
                    Assert.True(Theme.Contrast(C(text), C(surface)) >= 4.5, $"{theme}: {text} on {surface} below 4.5:1");
                // Control outlines only need 3:1, on every surface a control sits on.
                foreach (var surface in new[] { "Control", "Card", "Bg" })
                    Assert.True(Theme.Contrast(C("ControlBorder"), C(surface)) >= 3.0, $"{theme}: ControlBorder on {surface} below 3:1");
            }
            finally { Theme.Override = before; }
        });

        [Fact]
        public void Title_bar_styling_on_a_window_without_a_handle_does_nothing() => Sta.Run(() =>
        {
            var w = new Window();
            Theme.ApplyTitleBar(w);
            Theme.ApplyTitleBar(null);
        });

        [Fact]
        public void Watching_returns_an_unsubscribe_that_can_run_twice() => Sta.Run(() =>
        {
            int calls = 0;
            var stop = Theme.Watch(() => calls++);
            Assert.NotNull(stop);
            stop();
            stop();
            Assert.Equal(0, calls);
        });
    }
}
