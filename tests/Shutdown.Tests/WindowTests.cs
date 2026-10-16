using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Shutdown.Core;
using Shutdown.Ui;
using Xunit;

namespace Shutdown.Tests
{
    sealed class FakeRunner : IShutdownRunner
    {
        public readonly List<string> Calls = new List<string>();
        public Func<string, int> Result = a => 0;
        public int Run(string args) { Calls.Add(args); return Result(args); }
    }

    /// <summary>One STA thread with one Application for all window tests.</summary>
    static class Sta
    {
        static readonly Dispatcher D;

        static Sta()
        {
            ShutdownRunner.BlockedForTests = true;
            Dispatcher d = null;
            var ready = new ManualResetEventSlim();
            var t = new Thread(() =>
            {
                // Dispatcher.Run would otherwise run App.OnStartup: the real window, settings and single-instance lock.
                var app = new App { HostedByTests = true };
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Theme.Apply(app.Resources);
                d = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            ready.Wait();
            D = d;
        }

        public static void Run(Action a) => D.Invoke(a);
    }

    [Collection("window")]
    public class WindowTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownTests-" + Guid.NewGuid().ToString("N"));
        DateTime _now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Local);
        readonly FakeRunner _runner = new FakeRunner();
        SettingsStore _store;
        static readonly DateTime Boot = new DateTime(2026, 10, 16, 5, 0, 0, DateTimeKind.Utc);
        static string BootText => Boot.ToString("o");

        public WindowTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        MainWindow Make(Settings s = null)
        {
            // Fixed language: "system" would follow the machine running the tests.
            s = s ?? new Settings();
            Loc.I.Apply("en");
            MainWindow.BootUtc = () => Boot;
            _store = new SettingsStore(Path.Combine(_dir, SettingsStore.FileName));
            return new MainWindow(_store, s, _runner, () => _now);
        }

        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));

        [Fact]
        public void Forced_shutdown_replaces_pending_and_hands_the_timer_to_windows() => Sta.Run(() =>
        {
            var w = Make();
            Assert.True(w.StartButton.IsEnabled);
            Assert.Equal("shutdown /s /f /t 3600", w.CommandText.Text);
            // Countdown mode shows the end time.
            Assert.Equal(Loc.I.F("SummaryAt", Loc.I.DateText(_now.AddHours(1)), Loc.I.TimeText(_now.AddHours(1))), w.ResultText.Text);
            Click(w.StartButton);
            Assert.Equal(new[] { "/a", "/s /f /t 3600" }, _runner.Calls);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal("Shutdown scheduled", w.PendingTitle.Text);
            Assert.Equal("1:00:00", w.PendingClock.Text);
            Assert.Equal(Visibility.Collapsed, w.PendingKeepOpen.Visibility);
            var saved = _store.Load();
            Assert.Equal("Shutdown", saved.PendingAction);
        });

        [Fact]
        public void Pending_from_an_earlier_run_is_restored() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Restart", PendingTarget = _now.AddMinutes(30).ToString("o"), PendingBoot = BootText });
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal("Restart scheduled", w.PendingTitle.Text);
            Assert.Equal("0:30:00", w.PendingClock.Text);
        });

        [Fact]
        public void Pending_from_the_same_boot_is_restored_within_the_tolerance() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Restart", PendingTarget = _now.AddMinutes(30).ToString("o"), PendingBoot = Boot.AddSeconds(90).ToString("o") });
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
        });

        [Fact]
        public void Pending_from_another_boot_is_dropped() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Restart", PendingTarget = _now.AddMinutes(30).ToString("o"), PendingBoot = Boot.AddHours(-3).ToString("o") };
            var w = Make(s);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.False(w.AbortButton.IsEnabled);
            Assert.Null(s.PendingAction);
            Assert.Null(s.PendingTarget);
            Assert.Null(s.PendingBoot);
        });

        [Fact]
        public void Pending_without_a_boot_instant_is_dropped() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Restart", PendingTarget = _now.AddMinutes(30).ToString("o") });
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void Started_schedule_stores_the_boot_instant() => Sta.Run(() =>
        {
            var w = Make();
            Click(w.StartButton);
            Assert.Equal(BootText, _store.Load().PendingBoot);
        });

        [Fact]
        public void Expired_pending_is_not_restored() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Restart", PendingTarget = _now.AddMinutes(-1).ToString("o"), PendingBoot = BootText });
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void Abort_clears_pending_without_a_message() => Sta.Run(() =>
        {
            var w = Make();
            Click(w.StartButton);
            _runner.Calls.Clear();
            Click(w.AbortButton);
            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
            Assert.Null(_store.Load().PendingAction);
        });

        [Fact]
        public void Expired_pending_is_removed_from_settings() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Shutdown", PendingTarget = _now.AddMinutes(-5).ToUniversalTime().ToString("o"), PendingBoot = BootText };
            Make(s);
            Assert.Null(s.PendingAction);
            Assert.Null(s.PendingTarget);
        });

        [Fact]
        public void Failed_abort_keeps_the_windows_schedule_visible() => Sta.Run(() =>
        {
            var w = Make();
            Click(w.StartButton);
            _runner.Result = a => ShutdownRunner.ErrorAccessDenied;
            Click(w.AbortButton);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal(Loc.I["ErrAccessDenied"], w.MessageText.Text);
            Assert.Equal("Shutdown", _store.Load().PendingAction);
        });

        [Fact]
        public void Start_stops_when_the_old_schedule_cannot_be_cancelled() => Sta.Run(() =>
        {
            var w = Make();
            _runner.Result = a => ShutdownRunner.ErrorAccessDenied;
            Click(w.StartButton);
            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal("shutdown /a: exit code 5", w.MessageDetail.Text);
        });

        [Fact]
        public void Timed_out_scheduling_call_is_cancelled_again() => Sta.Run(() =>
        {
            var w = Make();
            _runner.Result = a => a == "/a" ? ShutdownRunner.ErrorNoShutdownInProgress : ShutdownRunner.TimeoutCode;
            Click(w.StartButton);
            Assert.Equal(new[] { "/a", "/s /f /t 3600", "/a" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Loc.I["ErrTimeout"], w.MessageText.Text);
        });

        [Fact]
        public void Error_detail_follows_the_language() => Sta.Run(() =>
        {
            var w = Make();
            _runner.Result = a => ShutdownRunner.ErrorAccessDenied;
            Loc.I.Apply("de");
            try
            {
                Click(w.StartButton);
                Assert.Equal("shutdown /a: Rückgabecode 5", w.MessageDetail.Text);
            }
            finally { Loc.I.Apply("en"); }
        });

        [Fact]
        public void Closed_window_no_longer_follows_language_changes() => Sta.Run(() =>
        {
            var w = Make();
            w.Close();
            Loc.I.Apply("de");
            try { Assert.Equal("Schedule shutdown", w.StartText.Text); }
            finally { Loc.I.Apply("en"); }
        });

        [Fact]
        public void Abort_of_app_countdown_stops_it_silently() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            _runner.Result = a => ShutdownRunner.ErrorNoShutdownInProgress;
            Click(w.AbortButton);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
            _now = _now.AddHours(2);
            w.OnTick();
            Assert.DoesNotContain("/h /f", _runner.Calls);
        });

        [Fact]
        public void Abort_is_only_enabled_while_something_is_scheduled() => Sta.Run(() =>
        {
            var w = Make();
            Assert.False(w.AbortButton.IsEnabled);
            Click(w.StartButton);
            Assert.True(w.AbortButton.IsEnabled);
            Click(w.AbortButton);
            Assert.False(w.AbortButton.IsEnabled);
        });

        [Fact]
        public void Abort_of_a_schedule_cancelled_elsewhere_just_clears_it() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Shutdown", PendingTarget = _now.AddMinutes(30).ToString("o"), PendingBoot = BootText });
            Assert.True(w.AbortButton.IsEnabled);
            _runner.Result = a => ShutdownRunner.ErrorNoShutdownInProgress;
            Click(w.AbortButton);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
            Assert.False(w.AbortButton.IsEnabled);
        });

        [Fact]
        public void Refused_command_shows_error_with_detail_and_no_pending() => Sta.Run(() =>
        {
            var w = Make();
            _runner.Result = a => a == "/a" ? ShutdownRunner.ErrorNoShutdownInProgress : ShutdownRunner.ErrorAccessDenied;
            Click(w.StartButton);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Loc.I["ErrAccessDenied"], w.MessageText.Text);
            Assert.Equal("shutdown /s /f /t 3600: exit code 5", w.MessageDetail.Text);
            Assert.Equal(Visibility.Visible, w.CopyButton.Visibility);
        });

        [Fact]
        public void Hibernate_runs_in_the_app_and_tells_to_keep_the_window_open() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.AppTimedNote.Visibility);
            Assert.Equal(Loc.I["AppTimedHibernate"], w.AppTimedText.Text);
            Click(w.StartButton);
            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal("Hibernate scheduled", w.PendingTitle.Text);
            Assert.Equal(Visibility.Visible, w.PendingKeepOpen.Visibility);
            Assert.Null(_store.Load().PendingAction);

            _now = _now.AddHours(1).AddSeconds(1);
            w.OnTick();
            Assert.Equal(new[] { "/a", "/h /f" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void App_timed_run_missed_during_sleep_is_skipped() => Sta.Run(() =>
        {
            var w = Make();
            w.ForceBox.IsChecked = false;
            Assert.Equal(Loc.I["AppTimedForceOff"], w.AppTimedText.Text);
            Click(w.StartButton);
            _now = _now.AddHours(1).AddMinutes(10);
            w.OnTick();
            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal(Loc.I["MsgMissed"], w.MessageText.Text);
        });

        [Fact]
        public void Windows_timed_pending_stays_after_its_target_until_aborted() => Sta.Run(() =>
        {
            var w = Make();
            Click(w.StartButton);
            _now = _now.AddHours(1).AddMinutes(5);
            w.OnTick();
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal("0:00:00", w.PendingClock.Text);
            Assert.True(w.AbortButton.IsEnabled);
            Assert.Equal("Shutdown", _store.Load().PendingAction);
            Click(w.AbortButton);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void Restored_windows_timed_pending_past_its_target_is_dropped() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Shutdown", PendingTarget = _now.AddMinutes(-1).ToUniversalTime().ToString("o"), PendingBoot = BootText };
            var w = Make(s);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Null(s.PendingTarget);
        });

        [Fact]
        public void Unforced_shutdown_fires_without_force_flag() => Sta.Run(() =>
        {
            var w = Make();
            w.ForceBox.IsChecked = false;
            Click(w.StartButton);
            _now = _now.AddHours(1);
            w.OnTick();
            Assert.Equal(new[] { "/a", "/s /t 0" }, _runner.Calls);
        });

        [Fact]
        public void Closing_with_app_timed_countdown_asks_first() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            var e = new CancelEventArgs();
            typeof(MainWindow).GetMethod("OnClosing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(w, new object[] { e });
            Assert.True(e.Cancel);
            Assert.Equal(Loc.I["CloseGuard"], w.MessageText.Text);
            Assert.Equal(Visibility.Visible, w.CloseAnywayButton.Visibility);
            Assert.Equal(Loc.I["KeepOpen"], w.DismissButton.Content);
        });

        [Fact]
        public void Zero_countdown_disables_start() => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 0;
            w.DurMinutes.Value = 0;
            Assert.False(w.StartButton.IsEnabled);
            Assert.Equal(Loc.I["ErrTooShort"], w.ResultText.Text);
            Assert.Equal("", w.CommandText.Text);
        });

        [Fact]
        public void Chip_sets_the_countdown() => Sta.Run(() =>
        {
            var w = Make();
            Click((Button)w.Chips.Children[1]);
            Assert.Equal(0, w.DurHours.Value);
            Assert.Equal(30, w.DurMinutes.Value);
            Assert.Equal("shutdown /s /f /t 1800", w.CommandText.Text);
        });

        [Fact]
        public void Date_mode_rejects_past_time_and_steps_days() => Sta.Run(() =>
        {
            var w = Make();
            w.ModeAt.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.AtPanel.Visibility);
            // Default is the next full 5 minutes one hour ahead.
            Assert.Equal(13, w.AtHour.Value);
            Assert.Equal(0, w.AtMinute.Value);
            Assert.Equal("shutdown /s /f /t 3600", w.CommandText.Text);
            Assert.False(w.PrevDay.IsEnabled);

            w.AtHour.Value = 11;
            Assert.False(w.StartButton.IsEnabled);
            Assert.Equal(Loc.I["ErrPast"], w.ResultText.Text);

            Click(w.NextDay);
            Assert.True(w.StartButton.IsEnabled);
            Assert.Equal("shutdown /s /f /t " + (23 * 3600), w.CommandText.Text);
            // Date mode shows the time left; the countdown chips are hidden.
            Assert.Equal("in 23 h", w.ResultText.Text);
            Assert.Equal(Visibility.Collapsed, w.Chips.Visibility);
            Assert.True(w.PrevDay.IsEnabled);
            Click(w.PrevDay);
            Assert.False(w.StartButton.IsEnabled);
        });

        [Fact]
        public void Language_change_rerenders_live() => Sta.Run(() =>
        {
            var w = Make();
            Assert.Equal("Schedule shutdown", w.StartText.Text);
            Loc.I.Apply("de");
            Assert.Equal("Herunterfahren planen", w.StartText.Text);
            Assert.Equal("Stunden", w.DurHours.CaptionText.Text);
            Loc.I.Apply("en");
            Assert.Equal("Schedule shutdown", w.StartText.Text);
        });

        [Fact]
        public void System_language_follows_the_ui_culture()
        {
            Assert.Equal("de", Loc.Resolve(System.Globalization.CultureInfo.GetCultureInfo("de-AT")));
            Assert.Equal("en", Loc.Resolve(System.Globalization.CultureInfo.GetCultureInfo("fr-FR")));
            Assert.Equal("en", Loc.Resolve(System.Globalization.CultureInfo.GetCultureInfo("en-GB")));
        }

        [Fact]
        public void Spinner_clamps_and_wraps() => Sta.Run(() =>
        {
            var w = Make();
            w.DurMinutes.Value = 75;
            Assert.Equal(59, w.DurMinutes.Value);
            w.AtHour.Value = 24;
            Assert.Equal(23, w.AtHour.Value);
            w.AtMinute.Value = -1;
            Assert.Equal(0, w.AtMinute.Value);
        });

        [Fact]
        public void Spinner_wraps_only_when_stepping() => Sta.Run(() =>
        {
            var w = Make();
            w.AtMinute.Value = 59;
            Click(w.AtMinute.UpButton);
            Assert.Equal(0, w.AtMinute.Value);
            Click(w.AtMinute.DownButton);
            Assert.Equal(59, w.AtMinute.Value);
        });

        [Fact]
        public void Real_runner_is_blocked_in_tests()
        {
            Sta.Run(() => { });
            Assert.Throws<InvalidOperationException>(() => new ShutdownRunner().Run("/a"));
        }
    }
}
