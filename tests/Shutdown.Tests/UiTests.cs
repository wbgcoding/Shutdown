using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Shutdown.Core;
using Shutdown.Ui;
using Xunit;

namespace Shutdown.Tests
{
    /// <summary>Window behaviour beyond WindowTests: restore, persistence, texts, messages, input errors.</summary>
    [Collection("window")]
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
        [InlineData(5000, 75, 999, 59)]
        [InlineData(-3, -1, 0, 0)]
        [InlineData(999, 59, 999, 59)]
        [InlineData(2, 30, 2, 30)]
        public void Stored_duration_is_clamped_to_the_field_ranges(int h, int m, int eh, int em) => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = h, Minutes = m });
            Assert.Equal(eh, w.DurHours.Value);
            Assert.Equal(em, w.DurMinutes.Value);
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

        [Fact]
        public void A_stored_hibernate_schedule_is_never_restored() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Hibernate", PendingDue = Due(30), PendingBoot = Marker };
            var w = Make(s);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.False(w.AbortButton.IsEnabled);
            Assert.Null(s.PendingAction);
        });

        // ---- texts and controls per state ----

        [Theory]
        [InlineData("en", "Shutdown", "Schedule shutdown", "")]
        [InlineData("en", "Restart", "Schedule restart", "")]
        [InlineData("en", "Hibernate", "Schedule hibernation", "")]
        [InlineData("de", "Shutdown", "Herunterfahren planen", "")]
        public void Start_button_follows_the_action_and_language(string lang, string action, string text, string icon) => Sta.Run(() =>
        {
            var w = Make();
            Loc.I.Apply(lang);
            (action == "Restart" ? w.ActRestart : action == "Hibernate" ? w.ActHibernate : w.ActShutdown).IsChecked = true;
            Assert.Equal(text, w.StartText.Text);
            Assert.Equal(icon, w.StartIcon.Text);
            bool power = action == "Shutdown";
            Assert.Equal(power ? Visibility.Visible : Visibility.Collapsed, w.StartPower.Visibility);
            Assert.Equal(power ? Visibility.Collapsed : Visibility.Visible, w.StartIcon.Visibility);
            Assert.Equal(text, AutomationProperties.GetName(w.StartButton));
        });

        [Fact]
        public void The_app_timed_note_shows_only_for_hibernate() => Sta.Run(() =>
        {
            var w = Make();
            Assert.Equal(Visibility.Collapsed, w.AppTimedNote.Visibility);
            w.ActRestart.IsChecked = true;
            Assert.Equal(Visibility.Collapsed, w.AppTimedNote.Visibility);
            w.ActHibernate.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.AppTimedNote.Visibility);
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
                var b = (Button)w.Chips.Children[i];
                Assert.Equal(ids[i], AutomationProperties.GetAutomationId(b));
                Assert.Equal(en[i], b.Content);
            }
            Loc.I.Apply("de");
            Assert.Equal(5, w.Chips.Children.Count);
            Assert.Equal(Loc.I.F("ChipMinutes", 15), ((Button)w.Chips.Children[0]).Content);
            Assert.Equal(Loc.I.F("ChipHours", 3), ((Button)w.Chips.Children[4]).Content);
            Assert.Equal("Chip180", AutomationProperties.GetAutomationId((Button)w.Chips.Children[4]));
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
            Click(ById<Button>(w, id));
            Assert.Equal(h, w.DurHours.Value);
            Assert.Equal(m, w.DurMinutes.Value);
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
            Assert.False(w.AbortButton.IsEnabled);
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
            Assert.Equal(new[] { "/a", "/s /f /t 3600" }, _runner.Calls);
            Assert.Equal(Loc.I[key], w.MessageText.Text);
            Assert.Equal("shutdown /s /f /t 3600: exit code " + code, w.MessageDetail.Text);
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
            foreach (var id in new[] { "ActShutdown", "ActRestart", "ActHibernate", "ModeCountdown", "ModeAt", "Start", "Abort",
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

    [Collection("window")]
    public class SpinnerTests : IDisposable
    {
        public void Dispose() => Sta.Run(() => Loc.I.Apply("en"));

        static NumberSpinner Make(int min = 0, int max = 99, bool wrap = false)
        {
            Loc.I.Apply("en");
            return new NumberSpinner { Minimum = min, Maximum = max, Wrap = wrap };
        }

        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));

        static bool Press(NumberSpinner s, Key k)
        {
            using (var src = new HwndSource(new HwndSourceParameters()))
            {
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, k) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                s.Box.RaiseEvent(e);
                return e.Handled;
            }
        }

        static bool TypeText(NumberSpinner s, string text)
        {
            var e = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, s.Box, text))
            { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            s.Box.RaiseEvent(e);
            return e.Handled;
        }

        static bool Paste(NumberSpinner s, string text)
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            var e = new DataObjectPastingEventArgs(data, false, DataFormats.UnicodeText) { RoutedEvent = DataObject.PastingEvent };
            s.Box.RaiseEvent(e);
            return e.CommandCancelled;
        }

        static void Wheel(NumberSpinner s, int delta) =>
            s.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent });

        [Fact]
        public void A_new_spinner_shows_two_digits_from_zero_to_ninety_nine() => Sta.Run(() =>
        {
            var s = new NumberSpinner();
            Assert.Equal(0, s.Value);
            Assert.Equal(0, s.Minimum);
            Assert.Equal(99, s.Maximum);
            Assert.False(s.Wrap);
            Assert.Equal(1, s.WheelStep);
            Assert.Equal("00", s.Box.Text);
        });

        [Theory]
        [InlineData(1, 7, "7")]
        [InlineData(2, 7, "07")]
        [InlineData(3, 7, "007")]
        [InlineData(0, 7, "7")]
        [InlineData(-4, 7, "7")]
        [InlineData(2, 123, "123")]
        public void Digits_pads_the_field(int digits, int value, string text) => Sta.Run(() =>
        {
            var s = Make(max: 999);
            s.Digits = digits;
            s.Value = value;
            Assert.Equal(text, s.Box.Text);
        });

        [Theory]
        [InlineData(5, 5)]
        [InlineData(100, 99)]
        [InlineData(-1, 0)]
        [InlineData(int.MaxValue, 99)]
        [InlineData(int.MinValue, 0)]
        public void Value_is_clamped(int set, int expected) => Sta.Run(() =>
        {
            var s = Make();
            s.Value = set;
            Assert.Equal(expected, s.Value);
        });

        [Fact]
        public void The_event_fires_only_for_a_real_change() => Sta.Run(() =>
        {
            var s = Make();
            int n = 0;
            s.ValueChanged += (a, b) => n++;
            s.Value = 0;
            Assert.Equal(0, n);
            s.Value = 5;
            Assert.Equal(1, n);
            s.Value = 5;
            Assert.Equal(1, n);
            s.Value = 500;
            Assert.Equal(2, n);
            s.Value = 99;
            Assert.Equal(2, n);
            s.Value = -50;
            Assert.Equal(3, n);
        });

        [Fact]
        public void Typed_digits_count_at_once_and_are_clamped() => Sta.Run(() =>
        {
            var s = Make();
            int n = 0;
            s.ValueChanged += (a, b) => n++;
            s.Box.Text = "42";
            Assert.Equal(42, s.Value);
            Assert.Equal(1, n);
            s.Box.Text = "007";
            Assert.Equal(7, s.Value);
            s.Box.Text = "500";
            Assert.Equal(99, s.Value);
            // The field keeps what was typed until it is committed.
            Assert.Equal("500", s.Box.Text);
            Assert.Equal(3, n);
        });

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("-5")]
        [InlineData("1 2")]
        [InlineData("99999999999")]
        [InlineData("+3")]
        public void Unreadable_text_leaves_the_value_alone(string text) => Sta.Run(() =>
        {
            var s = Make();
            s.Value = 12;
            int n = 0;
            s.ValueChanged += (a, b) => n++;
            s.Box.Text = text;
            Assert.Equal(12, s.Value);
            Assert.Equal(0, n);
        });

        [Fact]
        public void Enter_commits_and_reformats_the_field() => Sta.Run(() =>
        {
            var s = Make();
            s.Box.Text = "7";
            Assert.True(Press(s, Key.Enter));
            Assert.Equal("07", s.Box.Text);
            s.Box.Text = "500";
            Press(s, Key.Enter);
            Assert.Equal("99", s.Box.Text);
            s.Box.Text = "abc";
            Press(s, Key.Enter);
            Assert.Equal("99", s.Box.Text);
            Assert.Equal(99, s.Value);
        });

        [Fact]
        public void Arrow_keys_step_from_the_typed_value() => Sta.Run(() =>
        {
            var s = Make();
            s.Box.Text = "5";
            Assert.True(Press(s, Key.Up));
            Assert.Equal(6, s.Value);
            Assert.Equal("06", s.Box.Text);
            Assert.True(Press(s, Key.Down));
            Assert.Equal(5, s.Value);
            Assert.True(Press(s, Key.PageUp));
            Assert.Equal(15, s.Value);
            Assert.True(Press(s, Key.PageDown));
            Assert.Equal(5, s.Value);
        });

        [Fact]
        public void Other_keys_are_left_alone() => Sta.Run(() =>
        {
            var s = Make();
            Assert.False(Press(s, Key.A));
            Assert.False(Press(s, Key.Left));
            Assert.Equal(0, s.Value);
        });

        [Fact]
        public void Keys_stop_at_the_ends_without_wrap_and_wrap_with_it() => Sta.Run(() =>
        {
            var s = Make();
            s.Value = 95;
            Press(s, Key.PageUp);
            Assert.Equal(99, s.Value);
            s.Value = 3;
            Press(s, Key.PageDown);
            Assert.Equal(0, s.Value);
            Press(s, Key.Down);
            Assert.Equal(0, s.Value);

            var c = Make(max: 59, wrap: true);
            c.Value = 55;
            Press(c, Key.PageUp);
            Assert.Equal(5, c.Value);
            Press(c, Key.PageDown);
            Assert.Equal(55, c.Value);
        });

        [Fact]
        public void Wrap_respects_a_minimum_above_zero() => Sta.Run(() =>
        {
            var s = Make(min: 5, max: 8, wrap: true);
            s.Value = 8;
            Click(s.UpButton);
            Assert.Equal(5, s.Value);
            Click(s.DownButton);
            Assert.Equal(8, s.Value);
        });

        [Fact]
        public void Arrow_buttons_step_by_one_and_stop_at_the_ends() => Sta.Run(() =>
        {
            var s = Make(max: 2);
            Click(s.UpButton);
            Click(s.UpButton);
            Click(s.UpButton);
            Assert.Equal(2, s.Value);
            Click(s.DownButton);
            Click(s.DownButton);
            Click(s.DownButton);
            Assert.Equal(0, s.Value);
        });

        [Fact]
        public void The_wheel_steps_by_the_wheel_step_and_is_handled() => Sta.Run(() =>
        {
            var s = Make();
            s.WheelStep = 5;
            s.Value = 20;
            Wheel(s, 120);
            Assert.Equal(25, s.Value);
            Wheel(s, -120);
            Wheel(s, -1);
            Assert.Equal(15, s.Value);
            Wheel(s, 0);
            Assert.Equal(10, s.Value);
        });

        [Fact]
        public void The_wheel_wraps_on_clock_fields() => Sta.Run(() =>
        {
            var s = Make(max: 23, wrap: true);
            Wheel(s, -120);
            Assert.Equal(23, s.Value);
            Wheel(s, 120);
            Assert.Equal(0, s.Value);
        });

        [Theory]
        [InlineData("5", false)]
        [InlineData("0", false)]
        [InlineData("a", true)]
        [InlineData("-", true)]
        [InlineData(" ", true)]
        [InlineData(".", true)]
        [InlineData("", true)]
        [InlineData("٣", true)]
        [InlineData("1a", true)]
        public void Only_ascii_digits_can_be_typed(string text, bool blocked) => Sta.Run(() =>
        {
            Assert.Equal(blocked, TypeText(Make(), text));
        });

        [Theory]
        [InlineData("12", false)]
        [InlineData("007", false)]
        [InlineData("1x", true)]
        [InlineData("-1", true)]
        [InlineData("", true)]
        [InlineData("1 2", true)]
        [InlineData("٣", true)]
        public void Only_ascii_digits_can_be_pasted(string text, bool cancelled) => Sta.Run(() =>
        {
            Assert.Equal(cancelled, Paste(Make(), text));
        });

        [Fact]
        public void A_paste_without_text_is_cancelled() => Sta.Run(() =>
        {
            var s = Make();
            var data = new DataObject();
            data.SetData(DataFormats.Rtf, "x");
            var e = new DataObjectPastingEventArgs(data, false, DataFormats.Rtf) { RoutedEvent = DataObject.PastingEvent };
            s.Box.RaiseEvent(e);
            Assert.True(e.CommandCancelled);
        });

        [Fact]
        public void Automation_ids_come_from_the_key() => Sta.Run(() =>
        {
            var s = Make();
            s.AutomationKey = "Foo";
            Assert.Equal("FooSpinner", AutomationProperties.GetAutomationId(s));
            Assert.Equal("Foo", AutomationProperties.GetAutomationId(s.Box));
            Assert.Equal("FooUp", AutomationProperties.GetAutomationId(s.UpButton));
            Assert.Equal("FooDown", AutomationProperties.GetAutomationId(s.DownButton));
        });

        [Fact]
        public void The_caption_names_the_field_and_its_arrows() => Sta.Run(() =>
        {
            var s = Make();
            s.Caption = "Hours";
            Assert.Equal("Hours", s.CaptionText.Text);
            Assert.Equal("Hours", AutomationProperties.GetName(s.Box));
            Assert.Equal("Increase Hours", AutomationProperties.GetName(s.UpButton));
            Assert.Equal("Decrease Hours", AutomationProperties.GetName(s.DownButton));
            Loc.I.Apply("de");
            s.Caption = "Stunden";
            Assert.Equal(Loc.I.F("IncreaseFmt", "Stunden"), AutomationProperties.GetName(s.UpButton));
            Assert.NotEqual("Increase Stunden", AutomationProperties.GetName(s.UpButton));
        });

        [Fact]
        public void The_field_is_limited_to_three_characters() => Sta.Run(() =>
        {
            Assert.Equal(3, Make().Box.MaxLength);
        });
    }

    /// <summary>Palette and resources of Theme; Theme.Override is process-wide, so every test restores it.</summary>
    [Collection("window")]
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
