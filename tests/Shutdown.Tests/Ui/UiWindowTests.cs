using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shutdown.Core;
using Shutdown.Ui;
using Xunit;

namespace Shutdown.Tests
{
    /// <summary>Window behaviour beyond WindowTests: restore, persistence, texts, messages, input errors.</summary>
    [Collection("window")]
    [Trait("Layer", "Ui")]
    public class UiWindowTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownUiTests-" + Guid.NewGuid().ToString("N"));
        DateTime _now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Local);
        readonly FakeRunner _runner = new FakeRunner();
        SettingsStore _store;
        static readonly DateTime Boot = new DateTime(2026, 10, 16, 5, 0, 0, DateTimeKind.Utc);
        const string Marker = "boot:7";
        string _marker = Marker;
        readonly List<MainWindow> _windows = new List<MainWindow>();

        TimeSpan UptimeNow => _now.ToUniversalTime() - Boot;
        long Due(double minutesFromNow) => (long)(UptimeNow + TimeSpan.FromMinutes(minutesFromNow)).TotalMilliseconds;
        string SettingsFile => Path.Combine(_dir, SettingsStore.FileName);

        public UiWindowTests() { Directory.CreateDirectory(_dir); }

        public void Dispose()
        {
            var allowClose = typeof(MainWindow).GetField("_allowClose", BindingFlags.NonPublic | BindingFlags.Instance);
            Sta.Run(() =>
            {
                foreach (var w in _windows)
                {
                    allowClose.SetValue(w, true);
                    w.Close();
                }
                Loc.I.Apply("en");
            });
            try { Directory.Delete(_dir, true); } catch { }
        }

        MainWindow Make(Settings s = null)
        {
            s = s ?? new Settings();
            Loc.I.Apply("en");
            MainWindow.Uptime = () => UptimeNow;
            MainWindow.BootMarker = () => _marker;
            MainWindow.KeepAwake = on => { };
            _store = new SettingsStore(SettingsFile);
            var w = new MainWindow(_store, s, _runner, () => _now);
            _windows.Add(w);
            return w;
        }

        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));

        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            yield return root;
            foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var d in Walk(c)) yield return d;
        }

        static T ById<T>(DependencyObject root, string id) where T : DependencyObject =>
            Walk(root).OfType<T>().First(d => AutomationProperties.GetAutomationId(d) == id);

        // ---- restore from settings ----

        [Fact]
        public void Building_the_window_writes_no_settings_file() => Sta.Run(() =>
        {
            Make();
            Assert.False(File.Exists(SettingsFile));
        });

        [Theory]
        [InlineData("Restart", "Restart")]
        [InlineData("Shutdown", "Shutdown")]
        [InlineData("Foo", "Shutdown")]
        [InlineData("1", "Shutdown")]
        [InlineData("2", "Shutdown")]
        [InlineData("3", "Shutdown")]
        [InlineData("42", "Shutdown")]
        [InlineData("restart", "Shutdown")]
        [InlineData("", "Shutdown")]
        [InlineData(null, "Shutdown")]
        public void Stored_action_is_restored_or_falls_back_to_shutdown(string stored, string expected) => Sta.Run(() =>
        {
            var w = Make(new Settings { Action = stored });
            Assert.Equal(expected == "Restart", w.ActRestart.IsChecked == true);
            Assert.Equal(expected == "Shutdown", w.ActShutdown.IsChecked == true);
            Assert.NotEqual(true, w.ActHibernate.IsChecked);
            Assert.NotEqual(true, w.ActSleep.IsChecked);
        });

        [Fact]
        public void Stored_hibernate_is_restored_only_when_the_pc_allows_it() => Sta.Run(() =>
        {
            bool allowed = Make().ActHibernate.IsEnabled;
            var w = Make(new Settings { Action = "Hibernate" });
            Assert.Equal(allowed, w.ActHibernate.IsChecked == true);
            Assert.Equal(!allowed, w.ActShutdown.IsChecked == true);
        });

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void An_unavailable_action_has_a_disabled_segment_and_an_enabled_info_badge(bool sleep, bool allowed) => Sta.Run(() =>
        {
            var previousHibernate = MainWindow.HibernateAllowed;
            var previousSleep = MainWindow.SleepAllowed;
            if (sleep) MainWindow.SleepAllowed = () => allowed;
            else MainWindow.HibernateAllowed = () => allowed;
            try
            {
                string action = sleep ? "Sleep" : "Hibernate";
                var w = Make(new Settings { Action = action });
                var segment = sleep ? w.ActSleep : w.ActHibernate;
                var info = ById<Button>(w, action + "Info");
                Assert.Equal(allowed, segment.IsEnabled);
                Assert.Equal(allowed, segment.IsChecked == true);
                Assert.Equal(!allowed, w.ActShutdown.IsChecked == true);
                string help = Loc.I[allowed ? "Tip" + action : action + "Unavailable"];
                Assert.Equal(help, segment.ToolTip);
                Assert.Equal(help, AutomationProperties.GetHelpText(segment));
                Assert.Equal(allowed ? Visibility.Collapsed : Visibility.Visible, info.Visibility);
                if (allowed) return;

                Assert.True(ToolTipService.GetShowOnDisabled(segment));
                Assert.True(info.IsEnabled);
                Assert.True(info.Focusable);
                Assert.True(info.IsTabStop);
                Assert.Equal(help, AutomationProperties.GetName(info));
                Assert.True(info.Width >= 24 && info.Height >= 24);
                var tip = Assert.IsType<ToolTip>(info.ToolTip);
                Assert.Equal(help, tip.Content);

                try
                {
                    Click(info);
                    Assert.True(tip.IsOpen);
                }
                finally { tip.IsOpen = false; }
            }
            finally
            {
                MainWindow.HibernateAllowed = previousHibernate;
                MainWindow.SleepAllowed = previousSleep;
            }
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void German_action_segments_fit_the_two_by_two_grid(bool available) => Sta.Run(() =>
        {
            var w = Make();
            Loc.I.Apply("de");
            w.ActHibernate.IsEnabled = available;
            w.ActSleep.IsEnabled = available;
            if (available) w.ActSleep.IsChecked = true;

            var row = Assert.IsType<UniformGrid>(w.ActShutdown.Parent);
            Assert.Equal(2, row.Rows);
            Assert.Equal(2, row.Columns);
            Assert.Equal(4, row.Children.Count);
            var root = Assert.IsAssignableFrom<FrameworkElement>(w.Content);
            double clientWidth = w.Width - w.BorderThickness.Left - w.BorderThickness.Right;
            root.Measure(new Size(clientWidth, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, clientWidth, root.DesiredSize.Height));
            double cellWidth = row.ActualWidth / row.Columns;

            double widest = 0;
            foreach (UIElement segment in row.Children)
            {
                segment.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                widest = Math.Max(widest, segment.DesiredSize.Width);
            }
            Assert.True(widest <= cellWidth, $"The widest German segment needs {widest} px and its cell has {cellWidth} px.");
        });

        [Theory]
        [InlineData("de")]
        [InlineData("en")]
        public void Preset_chips_fit_on_one_row(string language) => Sta.Run(() =>
        {
            var w = Make();
            Loc.I.Apply(language);
            var root = Assert.IsAssignableFrom<FrameworkElement>(w.Content);
            double clientWidth = w.Width - w.BorderThickness.Left - w.BorderThickness.Right;
            root.Measure(new Size(clientWidth, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, clientWidth, root.DesiredSize.Height));

            var tops = w.Chips.Children.Cast<FrameworkElement>()
                .Select(c => c.TranslatePoint(new Point(0, 0), w.Chips).Y).Distinct().ToList();
            Assert.True(w.Chips.Children.Count >= 5);
            Assert.Single(tops);
        });

        /// <summary>Text of a TextBlock built from inlines (its Text property stays empty then); a line break reads as "\n".</summary>
        internal static string InlineText(TextBlock block) =>
            string.Concat(block.Inlines.Select(i => i is System.Windows.Documents.Run r ? r.Text : i is System.Windows.Documents.LineBreak ? "\n" : ""));

        static void LayOut(MainWindow w)
        {
            var root = Assert.IsAssignableFrom<FrameworkElement>(w.Content);
            double clientWidth = w.Width - w.BorderThickness.Left - w.BorderThickness.Right;
            root.Measure(new Size(clientWidth, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, clientWidth, root.DesiredSize.Height));
            root.UpdateLayout();
            // Runs the slider's deferred selection update.
            w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            root.UpdateLayout();
        }

        static Rect BoundsIn(SelectionSlider slider, ToggleButton toggle)
        {
            var part = (FrameworkElement)toggle.Template.FindName("Bd", toggle);
            return part.TransformToAncestor(slider).TransformBounds(new Rect(part.RenderSize));
        }

        [Fact]
        public void Selection_shape_follows_the_checked_segment() => Sta.Run(() =>
        {
            var w = Make();
            LayOut(w);
            var slider = Assert.IsType<SelectionSlider>(((FrameworkElement)w.ActShutdown.Parent).Parent);
            Assert.True(slider.IndicatorShown);
            Assert.Equal(BoundsIn(slider, w.ActShutdown), slider.IndicatorRect);

            w.ActRestart.IsChecked = true;
            LayOut(w);
            Assert.Equal(BoundsIn(slider, w.ActRestart), slider.IndicatorTarget);
            Assert.Equal(slider.IndicatorTarget, slider.IndicatorRect);
            // The slider paints the accent; the checked segment itself stays see-through.
            var bd = (Border)w.ActRestart.Template.FindName("Bd", w.ActRestart);
            Assert.Equal(0, ((SolidColorBrush)bd.Background).Color.A);
        });

        [Fact]
        public void Selection_shape_follows_the_matching_chip_and_hides_without_one() => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = 0, Minutes = 30 });
            LayOut(w);
            var slider = Assert.IsType<SelectionSlider>(w.Chips.Parent);
            Assert.NotNull(slider.ItemFill);
            var chip30 = w.Chips.Children.Cast<ToggleButton>().Single(c => AutomationProperties.GetAutomationId(c) == "Chip30");
            Assert.True(slider.IndicatorShown);
            Assert.Equal(BoundsIn(slider, chip30), slider.IndicatorRect);

            w.DurMinutes.Value = 31;
            LayOut(w);
            Assert.False(slider.IndicatorShown);
        });

        [Theory]
        [InlineData("At", true)]
        [InlineData("Countdown", false)]
        [InlineData("Bogus", false)]
        [InlineData("at", false)]
        [InlineData(null, false)]
        public void Stored_mode_is_restored_or_falls_back_to_countdown(string stored, bool at) => Sta.Run(() =>
        {
            var w = Make(new Settings { Mode = stored });
            Assert.Equal(at, w.ModeAt.IsChecked == true);
            Assert.Equal(!at, w.ModeCountdown.IsChecked == true);
            Assert.Equal(at ? Visibility.Visible : Visibility.Collapsed, w.AtPanel.Visibility);
            Assert.Equal(at ? Visibility.Collapsed : Visibility.Visible, w.CountdownPanel.Visibility);
            Assert.Equal(at ? Visibility.Visible : Visibility.Collapsed, w.DateRow.Visibility);
            Assert.Equal(at ? Visibility.Collapsed : Visibility.Visible, w.Chips.Visibility);
        });

        [Theory]
        [InlineData(-3, -1, 0, 0, "ErrTooShort")]
        [InlineData(999, 59, 999, 59, null)]
        [InlineData(2, 30, 2, 30, null)]
        [InlineData(2, 90, 3, 30, null)]
        [InlineData(0, 6000, 100, 0, null)]
        public void Stored_duration_normalizes_minutes_and_caps_hours(int h, int m, int eh, int em, string error) => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = h, Minutes = m });
            Assert.Equal(eh, w.DurHours.Value);
            Assert.Equal(em, w.DurMinutes.Value);
            Assert.Equal(error == null, w.StartButton.IsEnabled);
            if (error != null) Assert.Equal(Loc.I[error], w.ResultText.Text);
        });

        [Fact]
        public void An_oversized_stored_duration_loads_at_the_limit_with_info() => Sta.Run(() =>
        {
            // Above the ten-year Windows limit; anything below it may come from the command line and is kept.
            var w = Make(new Settings { Hours = 90000, Minutes = 75 });
            Assert.Equal(87600, w.DurHours.Value);
            Assert.Equal(0, w.DurMinutes.Value);
            Assert.True(w.StartButton.IsEnabled);
            Assert.Equal(Loc.I["ErrTooLong"], w.MessageText.Text);
        });

        [Theory]
        [InlineData(12, 0, 13, 0, 0)]
        [InlineData(12, 3, 13, 5, 0)]
        [InlineData(12, 56, 14, 0, 0)]
        [InlineData(12, 5, 13, 5, 0)]
        [InlineData(23, 30, 0, 30, 1)]
        [InlineData(23, 58, 1, 0, 1)]
        public void Date_mode_starts_an_hour_ahead_rounded_up_to_five_minutes(int h, int m, int eh, int em, int dayShift) => Sta.Run(() =>
        {
            _now = new DateTime(2026, 10, 16, h, m, 0, DateTimeKind.Local);
            var w = Make();
            Assert.Equal(eh, w.AtHour.Value);
            Assert.Equal(em, w.AtMinute.Value);
            Assert.Equal(Loc.I.DateText(_now.Date.AddDays(dayShift)), w.DateLabel.Text);
        });

        // ---- window position ----

        static bool OnScreen(double left, double top) =>
            (bool)typeof(MainWindow).GetMethod("OnScreen", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { left, top });

        [Fact]
        public void Position_check_accepts_the_virtual_screen_with_a_margin() => Sta.Run(() =>
        {
            double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
            double w = SystemParameters.VirtualScreenWidth, h = SystemParameters.VirtualScreenHeight;
            if (w < 300 || h < 300) return;
            Assert.True(OnScreen(l, t));
            Assert.True(OnScreen(l - 50, t - 10));
            Assert.False(OnScreen(l - 51, t));
            Assert.False(OnScreen(l, t - 11));
            Assert.True(OnScreen(l + w - 101, t + h - 101));
            Assert.False(OnScreen(l + w - 100, t));
            Assert.False(OnScreen(l, t + h - 100));
        });

        [Fact]
        public void A_stored_position_on_screen_is_restored() => Sta.Run(() =>
        {
            double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
            if (SystemParameters.VirtualScreenWidth < 300 || SystemParameters.VirtualScreenHeight < 300) return;
            var w = Make(new Settings { Left = l + 20, Top = t + 30 });
            Assert.Equal(WindowStartupLocation.Manual, w.WindowStartupLocation);
            Assert.Equal(l + 20, w.Left);
            Assert.Equal(t + 30, w.Top);
        });

        [Fact]
        public void A_stored_position_off_screen_is_ignored() => Sta.Run(() =>
        {
            double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
            var w = Make(new Settings { Left = l + SystemParameters.VirtualScreenWidth + 500, Top = t });
            Assert.Equal(WindowStartupLocation.CenterScreen, w.WindowStartupLocation);
        });

        [Fact]
        public void Half_a_stored_position_is_ignored() => Sta.Run(() =>
        {
            var w = Make(new Settings { Left = SystemParameters.VirtualScreenLeft + 10 });
            Assert.Equal(WindowStartupLocation.CenterScreen, w.WindowStartupLocation);
            w = Make(new Settings { Top = SystemParameters.VirtualScreenTop + 10 });
            Assert.Equal(WindowStartupLocation.CenterScreen, w.WindowStartupLocation);
        });

        // ---- persistence ----

        [Fact]
        public void Changing_the_inputs_saves_them() => Sta.Run(() =>
        {
            var w = Make();
            w.ActRestart.IsChecked = true;
            var s = _store.Load();
            Assert.Equal("Restart", s.Action);
            w.ModeAt.IsChecked = true;
            Assert.Equal("At", _store.Load().Mode);
            w.ModeCountdown.IsChecked = true;
            Assert.Equal("Countdown", _store.Load().Mode);
            w.DurHours.Value = 4;
            w.DurMinutes.Value = 25;
            s = _store.Load();
            Assert.Equal(4, s.Hours);
            Assert.Equal(25, s.Minutes);
        });

        [Fact]
        public void The_clock_fields_are_not_saved() => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 2;
            var before = File.ReadAllText(SettingsFile);
            w.ModeAt.IsChecked = true;
            w.AtHour.Value = 3;
            w.AtMinute.Value = 7;
            Assert.DoesNotContain("\"AtHour\"", File.ReadAllText(SettingsFile));
            Assert.Equal(2, _store.Load().Hours);
            Assert.NotEqual("", before);
        });

        [Fact]
        public void An_unshown_window_saves_no_position() => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 2;
            var s = _store.Load();
            Assert.Null(s.Left);
            Assert.Null(s.Top);
        });

        [Fact]
        public void Closing_saves_the_inputs_again() => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 5;
            File.Delete(SettingsFile);
            w.Close();
            _windows.Remove(w);
            Assert.True(File.Exists(SettingsFile));
            Assert.Equal(5, _store.Load().Hours);
        });

        [Fact]
        public void A_started_schedule_without_a_boot_marker_is_stored_as_unknown() => Sta.Run(() =>
        {
            _marker = null;
            var w = Make();
            Click(w.StartButton);
            Assert.Equal("unknown", _store.Load().PendingBoot);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
        });

        [Fact]
        public void A_stored_schedule_with_an_empty_boot_marker_is_dropped() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Shutdown", PendingDue = Due(30), PendingBoot = "" };
            var w = Make(s);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Null(s.PendingAction);
            Assert.Null(s.PendingBoot);
        });

        [Theory]
        [InlineData("Hibernate")]
        [InlineData("Sleep")]
        public void A_stored_app_timed_schedule_is_never_restored(string action) => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = action, PendingDue = Due(30), PendingBoot = Marker };
            var w = Make(s);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Null(s.PendingAction);
        });

        // ---- texts and controls per state ----

        [Theory]
        [InlineData("en",
            "Turns the PC off. Open programs are closed without asking.",
            "Restarts the PC. Open programs are closed without asking.",
            "Saves the session to disk and turns the PC off. On the next start everything is as it was.",
            "Runs once the set time has passed.",
            "Runs at a set date and time.",
            "Hibernation is turned off on this PC.")]
        [InlineData("de",
            "Schaltet den PC aus. Offene Programme werden ohne Nachfrage beendet.",
            "Startet den PC neu. Offene Programme werden ohne Nachfrage beendet.",
            "Speichert die Sitzung auf der Festplatte und schaltet den PC aus. Beim nächsten Start ist alles wieder wie vorher.",
            "Läuft nach Ablauf der eingestellten Zeit.",
            "Läuft zu einem festen Datum und einer Uhrzeit.",
            "Ruhezustand ist auf diesem PC deaktiviert.")]
        public void Action_and_mode_tooltips_match_accessible_help(string language, string shutdown, string restart,
            string hibernate, string countdown, string at, string hibernateUnavailable) => Sta.Run(() =>
        {
            var w = Make();
            Loc.I.Apply(language);

            void AssertHelp(RadioButton button, string expected)
            {
                Assert.Equal(expected, button.ToolTip);
                Assert.Equal(expected, AutomationProperties.GetHelpText(button));
            }

            AssertHelp(w.ActShutdown, shutdown);
            AssertHelp(w.ActRestart, restart);
            AssertHelp(w.ModeCountdown, countdown);
            AssertHelp(w.ModeAt, at);
            AssertHelp(w.ActHibernate, w.ActHibernate.IsEnabled ? hibernate : hibernateUnavailable);

            w.ActHibernate.IsEnabled = true;
            AssertHelp(w.ActHibernate, hibernate);
            w.ActHibernate.IsEnabled = false;
            AssertHelp(w.ActHibernate, hibernateUnavailable);
            Assert.True(ToolTipService.GetShowOnDisabled(w.ActHibernate));
        });

        [Fact]
        public void Start_has_its_own_row_and_abort_is_only_shown_in_the_pending_card() => Sta.Run(() =>
        {
            var w = Make();
            var bottomRow = Assert.IsType<Grid>(w.StartButton.Parent);
            Assert.Single(bottomRow.ColumnDefinitions);
            Assert.Same(w.StartButton, Assert.Single(bottomRow.Children.Cast<UIElement>()));
            Assert.Same(bottomRow, Assert.IsType<StackPanel>(bottomRow.Parent).Children.Cast<UIElement>().Last());

            var abort = ById<Button>(w.PendingCard, "Abort");
            Assert.Same(w.AbortButton, abort);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);

            Click(w.StartButton);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal(Visibility.Visible, abort.Visibility);

            Click(abort);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Theory]
        [InlineData("en", "Shutdown", "Schedule shutdown", "")]
        [InlineData("en", "Restart", "Schedule restart", "")]
        [InlineData("en", "Hibernate", "Schedule hibernation", "")]
        [InlineData("en", "Sleep", "Schedule sleep", "\uE945")]
        [InlineData("de", "Shutdown", "Herunterfahren planen", "")]
        [InlineData("de", "Sleep", "Energie sparen planen", "\uE945")]
        public void Start_button_follows_the_action_and_language(string lang, string action, string text, string icon) => Sta.Run(() =>
        {
            var w = Make();
            Loc.I.Apply(lang);
            var segment = ById<RadioButton>(w, "Act" + action);
            segment.IsEnabled = true;
            segment.IsChecked = true;
            Assert.Equal(text, w.StartText.Text);
            Assert.Equal(icon, w.StartIcon.Text);
            bool power = action == "Shutdown";
            Assert.Equal(power ? Visibility.Visible : Visibility.Collapsed, w.StartPower.Visibility);
            Assert.Equal(power ? Visibility.Collapsed : Visibility.Visible, w.StartIcon.Visibility);
            Assert.Equal(text, AutomationProperties.GetName(w.StartButton));
        });

        [Theory]
        [InlineData("en", "Scheduled with the Shutdown app.")]
        [InlineData("de", "Geplant mit der App Shutdown.")]
        public void Shutdown_warning_uses_the_localized_comment(string language, string expected) => Sta.Run(() =>
        {
            var w = Make();
            Loc.I.Apply(language);
            string comment = Loc.I["ShutdownComment"];
            Assert.Equal(expected, comment);
            Assert.DoesNotContain("\"", comment);

            Click(w.StartButton);
            Assert.Equal(new[] { "/a", "/s /f /t 3600 /c \"" + expected + "\"" }, _runner.Calls);
        });

        [Fact]
        public void Start_button_offers_to_reschedule_the_selected_action() => Sta.Run(() =>
        {
            var w = Make();
            Click(w.StartButton);
            w.ActRestart.IsChecked = true;

            Assert.Equal("Reschedule", w.StartText.Text);
            Assert.Equal("Reschedule: Restart", AutomationProperties.GetName(w.StartButton));
            Assert.Equal("\uE72C", w.StartIcon.Text);
            Assert.Equal(Visibility.Visible, w.StartIcon.Visibility);

            Loc.I.Apply("de");
            Assert.Equal("Neu planen", w.StartText.Text);
            Assert.Equal("Neu planen: Neu starten", AutomationProperties.GetName(w.StartButton));

            Click(w.AbortButton);
            Assert.Equal("Neustart planen", w.StartText.Text);
            Assert.Equal("Neustart planen", AutomationProperties.GetName(w.StartButton));
        });

        [Fact]
        public void The_app_timed_note_shows_for_hibernate_and_sleep() => Sta.Run(() =>
        {
            var w = Make();
            Assert.Equal(Visibility.Collapsed, w.AppTimedNote.Visibility);
            w.ActRestart.IsChecked = true;
            Assert.Equal(Visibility.Collapsed, w.AppTimedNote.Visibility);
            w.ActHibernate.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.AppTimedNote.Visibility);
            Assert.Equal(Loc.I["AppTimedHibernate"] + "\n" + Loc.I["PendingKeepOpen"], UiWindowTests.InlineText(w.AppTimedText));
            w.ActSleep.IsEnabled = true;
            w.ActSleep.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.AppTimedNote.Visibility);
            Assert.Equal(Loc.I["AppTimedSleep"] + "\n" + Loc.I["PendingKeepOpen"], UiWindowTests.InlineText(w.AppTimedText));
            Loc.I.Apply("de");
            Assert.Equal(Loc.I["AppTimedSleep"] + "\n" + Loc.I["PendingKeepOpen"], UiWindowTests.InlineText(w.AppTimedText));
            w.ActShutdown.IsChecked = true;
            Assert.Equal(Visibility.Collapsed, w.AppTimedNote.Visibility);
        });

        [Fact]
        public void Chips_have_stable_ids_and_texts_and_follow_the_language() => Sta.Run(() =>
        {
            var w = Make();
            string[] ids = { "Chip15", "Chip30", "Chip60", "Chip120", "Chip180" };
            string[] en = { "15 min", "30 min", "1 h", "2 h", "3 h" };
            Assert.Equal(5, w.Chips.Children.Count);
            for (int i = 0; i < 5; i++)
            {
                var b = Assert.IsAssignableFrom<ToggleButton>(w.Chips.Children[i]);
                Assert.Equal(ids[i], AutomationProperties.GetAutomationId(b));
                Assert.Equal(en[i], b.Content);
            }
            Loc.I.Apply("de");
            Assert.Equal(5, w.Chips.Children.Count);
            Assert.Equal(Loc.I.F("ChipMinutes", 15), ((ToggleButton)w.Chips.Children[0]).Content);
            Assert.Equal(Loc.I.F("ChipHours", 3), ((ToggleButton)w.Chips.Children[4]).Content);
            Assert.Equal("Chip180", AutomationProperties.GetAutomationId(w.Chips.Children[4]));
        });

        [Theory]
        [InlineData("Chip15", 0, 15)]
        [InlineData("Chip30", 0, 30)]
        [InlineData("Chip60", 1, 0)]
        [InlineData("Chip120", 2, 0)]
        [InlineData("Chip180", 3, 0)]
        public void Each_chip_sets_and_saves_its_duration(string id, int h, int m) => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 9;
            w.DurMinutes.Value = 9;
            var chip = ById<ToggleButton>(w, id);
            var onClick = typeof(ToggleButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
            onClick.Invoke(chip, null);
            Assert.Equal(h, w.DurHours.Value);
            Assert.Equal(m, w.DurMinutes.Value);
            Assert.True(chip.IsChecked);
            onClick.Invoke(chip, null);
            Assert.True(chip.IsChecked);
            Assert.Equal(h, _store.Load().Hours);
            Assert.Equal(m, _store.Load().Minutes);
        });

        [Fact]
        public void A_chip_made_before_a_language_switch_is_replaced_not_duplicated() => Sta.Run(() =>
        {
            var w = Make();
            var first = w.Chips.Children[0];
            Loc.I.Apply("de");
            Loc.I.Apply("en");
            Assert.Equal(5, w.Chips.Children.Count);
            Assert.NotSame(first, w.Chips.Children[0]);
        });

        [Fact]
        public void Day_buttons_step_and_reset_the_date() => Sta.Run(() =>
        {
            var w = Make();
            w.ModeAt.IsChecked = true;
            var today = _now.Date;
            Assert.Equal(Loc.I.DateText(today), w.DateLabel.Text);
            Assert.False(w.PrevDay.IsEnabled);
            Click(w.NextDay);
            Assert.Equal(Loc.I.DateText(today.AddDays(1)), w.DateLabel.Text);
            Assert.True(w.PrevDay.IsEnabled);
            Click(ById<Button>(w, "Tomorrow"));
            Assert.Equal(Loc.I.DateText(today.AddDays(1)), w.DateLabel.Text);
            Click(w.NextDay);
            Click(w.NextDay);
            Assert.Equal(Loc.I.DateText(today.AddDays(3)), w.DateLabel.Text);
            Click(ById<Button>(w, "Today"));
            Assert.Equal(Loc.I.DateText(today), w.DateLabel.Text);
            Assert.False(w.PrevDay.IsEnabled);
            // Stepping back past today does nothing, even when the button is forced.
            Click(w.PrevDay);
            Assert.Equal(Loc.I.DateText(today), w.DateLabel.Text);
        });

        [Fact]
        public void The_date_label_follows_the_language() => Sta.Run(() =>
        {
            var w = Make();
            w.ModeAt.IsChecked = true;
            var en = w.DateLabel.Text;
            Loc.I.Apply("de");
            Assert.Equal(Loc.I.DateText(_now.Date), w.DateLabel.Text);
            Assert.NotEqual(en, w.DateLabel.Text);
        });

        [Fact]
        public void Time_left_in_date_mode_rounds_up_to_whole_minutes() => Sta.Run(() =>
        {
            _now = new DateTime(2026, 10, 16, 11, 59, 59, 500, DateTimeKind.Local);
            var w = Make();
            w.ModeAt.IsChecked = true;
            w.AtHour.Value = 12;
            w.AtMinute.Value = 1;
            // 60.5 s left shows as 2 min, never as 1.
            Assert.Equal("in 2 min", w.ResultText.Text);
            _now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Local);
            w.OnTick();
            Assert.Equal("in 1 min", w.ResultText.Text);
        });

        [Fact]
        public void Time_left_in_date_mode_shows_days_and_hours() => Sta.Run(() =>
        {
            var w = Make();
            w.ModeAt.IsChecked = true;
            Click(w.NextDay);
            w.AtHour.Value = 13;
            w.AtMinute.Value = 0;
            Assert.Equal("in 1 d 1 h", w.ResultText.Text);
        });

        // ---- invalid input ----

        [Fact]
        public void A_zero_countdown_shows_the_reason_and_a_forced_click_runs_nothing() => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 0;
            w.DurMinutes.Value = 0;
            Assert.False(w.StartButton.IsEnabled);
            Assert.Equal(Loc.I["ErrTooShort"], w.ResultText.Text);
            Assert.Same(w.FindResource("Danger"), w.ResultText.Foreground);
            Assert.Equal(Loc.I["ErrTooShort"], AutomationProperties.GetHelpText(w.StartButton));
            Click(w.StartButton);
            Assert.Empty(_runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.False(File.Exists(SettingsFile) && _store.Load().PendingAction != null);
        });

        [Fact]
        public void A_valid_input_clears_the_reason() => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 0;
            w.DurMinutes.Value = 0;
            w.DurMinutes.Value = 1;
            Assert.True(w.StartButton.IsEnabled);
            Assert.Equal("", AutomationProperties.GetHelpText(w.StartButton));
            Assert.Same(w.FindResource("Text"), w.ResultText.Foreground);
        });

        [Fact]
        public void A_past_time_runs_nothing_when_start_is_forced() => Sta.Run(() =>
        {
            var w = Make();
            w.ModeAt.IsChecked = true;
            w.AtHour.Value = 11;
            w.AtMinute.Value = 0;
            Assert.False(w.StartButton.IsEnabled);
            Assert.Equal(Loc.I["ErrPast"], w.ResultText.Text);
            Click(w.StartButton);
            Assert.Empty(_runner.Calls);
        });

        [Fact]
        public void The_reason_is_shown_in_german_after_a_switch() => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 0;
            w.DurMinutes.Value = 0;
            Loc.I.Apply("de");
            Assert.Equal(Loc.I["ErrTooShort"], w.ResultText.Text);
            Assert.NotEqual("Choose at least 1 minute.", w.ResultText.Text);
        });

        // ---- start outcomes ----

        [Fact]
        public void A_runner_that_cannot_start_shows_an_error_with_the_log_button() => Sta.Run(() =>
        {
            _runner.Result = a => throw new RunnerException("shutdown.exe", new InvalidOperationException("boom text"));
            var w = Make();
            Click(w.StartButton);
            Assert.Single(_runner.Calls);
            Assert.Equal(Visibility.Visible, w.MessageCard.Visibility);
            Assert.Equal(Loc.I["ErrStart"], w.MessageText.Text);
            Assert.Equal("shutdown.exe: boom text", w.MessageDetail.Text);
            Assert.Equal(Visibility.Visible, w.OpenLogButton.Visibility);
            Assert.Equal(Visibility.Visible, w.CopyButton.Visibility);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Theory]
        [InlineData(5, "ErrAccessDenied")]
        [InlineData(1, "ErrRunFailed")]
        [InlineData(1190, "ErrRunFailed")]
        public void A_failing_schedule_call_maps_the_exit_code_to_a_message(int code, string key) => Sta.Run(() =>
        {
            _runner.Result = a => a == "/a" ? 0 : code;
            var w = Make();
            Click(w.StartButton);
            Assert.Equal(new[] { "/a", "/s /f /t 3600 /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
            Assert.Equal(Loc.I[key], w.MessageText.Text);
            Assert.Equal("shutdown /s /f /t 3600 /c \"Scheduled with the Shutdown app.\": exit code " + code, w.MessageDetail.Text);
            Assert.Equal(Visibility.Collapsed, w.OpenLogButton.Visibility);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void No_shutdown_in_progress_is_the_normal_answer_to_the_abort() => Sta.Run(() =>
        {
            _runner.Result = a => a == "/a" ? ShutdownRunner.ErrorNoShutdownInProgress : 0;
            var w = Make();
            Click(w.StartButton);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
        });

        [Fact]
        public void A_new_start_hides_the_old_message() => Sta.Run(() =>
        {
            _runner.Result = a => a == "/a" ? 0 : 1;
            var w = Make();
            Click(w.StartButton);
            Assert.Equal(Visibility.Visible, w.MessageCard.Visibility);
            _runner.Result = a => 0;
            Click(w.StartButton);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
        });

        // ---- message card ----

        [Fact]
        public void An_error_without_detail_or_log_shows_only_copy_and_dismiss() => Sta.Run(() =>
        {
            var w = Make();
            w.ShowError("ErrUnexpected", null, false);
            Assert.Equal(Visibility.Visible, w.MessageCard.Visibility);
            Assert.Equal(Visibility.Collapsed, w.MessageDetail.Visibility);
            Assert.Equal(Visibility.Visible, w.CopyButton.Visibility);
            Assert.Equal(Visibility.Collapsed, w.OpenLogButton.Visibility);
            Assert.Equal(Visibility.Collapsed, w.CloseAnywayButton.Visibility);
            Assert.Equal("Dismiss", w.DismissButton.Content);
            Assert.Same(w.FindResource("DangerSoft"), w.MessageCard.Background);
            Assert.Same(w.FindResource("Danger"), w.MessageText.Foreground);
        });

        [Fact]
        public void An_error_with_detail_and_log_shows_all_its_buttons() => Sta.Run(() =>
        {
            var w = Make();
            w.ShowError("ErrStart", "some detail", true);
            Assert.Equal(Visibility.Visible, w.MessageDetail.Visibility);
            Assert.Equal("some detail", w.MessageDetail.Text);
            Assert.Equal(Visibility.Visible, w.OpenLogButton.Visibility);
            Assert.Equal(Visibility.Collapsed, w.CloseAnywayButton.Visibility);
        });

        [Fact]
        public void A_missed_run_is_a_neutral_message_without_copy() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            _now = _now.AddHours(3);
            w.OnTick();
            Assert.Equal(Loc.I["MsgMissed"], w.MessageText.Text);
            Assert.Equal(Visibility.Collapsed, w.CopyButton.Visibility);
            Assert.Equal(Visibility.Collapsed, w.OpenLogButton.Visibility);
            Assert.Equal(Visibility.Collapsed, w.CloseAnywayButton.Visibility);
            Assert.Same(w.FindResource("Control"), w.MessageCard.Background);
            Assert.Same(w.FindResource("Text"), w.MessageText.Foreground);
        });

        [Fact]
        public void Dismiss_hides_the_message() => Sta.Run(() =>
        {
            var w = Make();
            w.ShowError("ErrUnexpected", "x", true);
            Click(w.DismissButton);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
        });

        [Fact]
        public void The_message_is_translated_again_on_a_language_switch() => Sta.Run(() =>
        {
            var w = Make();
            w.ShowError("ErrStart", "kept", false);
            var en = w.MessageText.Text;
            Loc.I.Apply("de");
            Assert.Equal(Loc.I["ErrStart"], w.MessageText.Text);
            Assert.NotEqual(en, w.MessageText.Text);
            Assert.Equal("kept", w.MessageDetail.Text);
        });

        [Fact]
        public void The_close_warning_offers_keep_open_and_close_anyway() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            w.Close();
            Assert.Equal(Loc.I["CloseGuard"], w.MessageText.Text);
            Assert.Equal(Visibility.Visible, w.CloseAnywayButton.Visibility);
            Assert.Equal("Keep open", w.DismissButton.Content);
            Assert.Equal(Visibility.Collapsed, w.CopyButton.Visibility);
            Assert.Equal(Visibility.Collapsed, w.OpenLogButton.Visibility);
            Assert.Same(w.FindResource("Control"), w.MessageCard.Background);

            Click(w.DismissButton);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);

            bool closed = false;
            w.Closed += (s, e) => closed = true;
            w.Close();
            Click(w.CloseAnywayButton);
            Assert.True(closed);
            _windows.Remove(w);
        });

        // ---- conventions ----

        [Fact]
        public void Every_interactive_control_has_a_unique_automation_id() => Sta.Run(() =>
        {
            var w = Make();
            w.ShowError("ErrStart", "d", true);
            var interactive = Walk(w).Where(d => d is Button || d is RadioButton || d is TextBox).ToList();
            Assert.True(interactive.Count >= 20);
            foreach (var d in interactive)
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetAutomationId(d)), d.GetType().Name + " without id");
            var ids = Walk(w).Select(AutomationProperties.GetAutomationId).Where(i => !string.IsNullOrEmpty(i)).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        });

        [Fact]
        public void The_window_has_the_documented_ids() => Sta.Run(() =>
        {
            var w = Make();
            foreach (var id in new[] { "ActShutdown", "ActRestart", "ActHibernate", "ActSleep", "SleepInfo", "ModeCountdown", "ModeAt", "Start", "Abort",
                "PrevDay", "NextDay", "Today", "Tomorrow", "DurHours", "DurHoursUp", "DurHoursDown", "DurMinutes",
                "AtHour", "AtMinute", "ResultText", "PendingCard", "PendingClock", "MessageCard", "MessageText",
                "CopyMessage", "OpenLog", "CloseAnyway", "DismissMessage" })
                Assert.Contains(Walk(w), d => AutomationProperties.GetAutomationId(d) == id);
        });

        [Fact]
        public void The_clock_colour_follows_the_fractions_of_a_second()
        {
            Assert.Equal("Text", MainWindow.ClockBrushKey(TimeSpan.FromSeconds(300.001)));
            Assert.Equal("Warn", MainWindow.ClockBrushKey(TimeSpan.FromSeconds(300)));
            Assert.Equal("Warn", MainWindow.ClockBrushKey(TimeSpan.FromSeconds(60.001)));
            Assert.Equal("Urgent", MainWindow.ClockBrushKey(TimeSpan.FromSeconds(60)));
            Assert.Equal("Urgent", MainWindow.ClockBrushKey(TimeSpan.FromSeconds(-5)));
            Assert.Equal("Text", MainWindow.ClockBrushKey(TimeSpan.FromDays(400)));
        }
    }
}
