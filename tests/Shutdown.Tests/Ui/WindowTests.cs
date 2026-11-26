using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using Shutdown.Core;
using Shutdown.Ui;
using Xunit;

namespace Shutdown.Tests
{
    [Collection("window")]
    [Trait("Layer", "Ui")]
    public class WindowTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownTests-" + Guid.NewGuid().ToString("N"));
        DateTime _now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Local);
        readonly FakeRunner _runner = new FakeRunner();
        SettingsStore _store;
        readonly List<bool> _awake = new List<bool>();
        static readonly DateTime Boot = new DateTime(2026, 10, 16, 5, 0, 0, DateTimeKind.Utc);
        const string Marker = "boot:42";
        string _marker = Marker;
        // How far the wall clock was set forward after boot; uptime does not follow it.
        TimeSpan _clockSet;

        TimeSpan UptimeNow => _now.ToUniversalTime() - Boot - _clockSet;
        long Due(double minutesFromNow) => (long)(UptimeNow + TimeSpan.FromMinutes(minutesFromNow)).TotalMilliseconds;

        public WindowTests() { Directory.CreateDirectory(_dir); }
        readonly List<MainWindow> _windows = new List<MainWindow>();

        public void Dispose()
        {
            // Windows left open keep their process-wide event subscriptions and stall the test host at exit.
            var allowClose = typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Sta.Run(() =>
            {
                foreach (var w in _windows)
                {
                    allowClose.SetValue(w, true);
                    w.Close();
                }
            });
            try { Directory.Delete(_dir, true); } catch { }
        }

        MainWindow Make(Settings s = null)
        {
            // Fixed language: "system" would follow the machine running the tests.
            s = s ?? new Settings();
            Loc.I.Apply("en");
            MainWindow.Uptime = () => UptimeNow;
            MainWindow.BootMarker = () => _marker;
            MainWindow.KeepAwake = on => _awake.Add(on);
            _store = new SettingsStore(Path.Combine(_dir, SettingsStore.FileName));
            var w = new MainWindow(_store, s, _runner, () => _now);
            _windows.Add(w);
            return w;
        }

        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));

        [Fact]
        public void Forced_shutdown_replaces_pending_and_hands_the_timer_to_windows() => Sta.Run(() =>
        {
            var w = Make();
            Assert.True(w.StartButton.IsEnabled);
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
        public void Postponing_shutdown_replaces_the_windows_timer_and_updates_the_card() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Shutdown", PendingDue = Due(30), PendingBoot = Marker };
            var w = Make(s);
            Assert.Equal(1d, w.TaskbarItemInfo.ProgressValue);
            _now = _now.AddMinutes(5);
            w.OnTick();
            Assert.Equal(25d / 30, w.TaskbarItemInfo.ProgressValue, 8);
            Click(w.Extend15Button);

            Assert.Equal(new[] { "/a", "/s /f /t 2400" }, _runner.Calls);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal("0:40:00", w.PendingClock.Text);
            Assert.Equal(40d / 45, w.TaskbarItemInfo.ProgressValue, 8);
            var target = _now.AddMinutes(40);
            Assert.Equal(Loc.I.F("SummaryAt", Loc.I.DateText(target), Loc.I.TimeText(target)), w.PendingWhen.Text);
            Assert.Equal("Shutdown", s.PendingAction);
            Assert.Equal(Due(40), s.PendingDue);
        });

        [Fact]
        public void Postponing_past_the_windows_limit_keeps_the_old_timer() => Sta.Run(() =>
        {
            var due = Due((Schedule.MaxSeconds - 899) / 60d);
            var s = new Settings { PendingAction = "Shutdown", PendingDue = due, PendingBoot = Marker };
            var w = Make(s);
            var oldTime = w.PendingWhen.Text;
            Click(w.Extend15Button);

            Assert.Empty(_runner.Calls);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal(oldTime, w.PendingWhen.Text);
            Assert.Equal(due, s.PendingDue);
            Assert.Equal(Loc.I["ErrTooLong"], w.MessageText.Text);
        });

        [Fact]
        public void Failed_abort_during_postpone_keeps_the_old_timer() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Shutdown", PendingDue = Due(30), PendingBoot = Marker };
            var w = Make(s);
            _runner.Result = a => ShutdownRunner.ErrorAccessDenied;
            Click(w.Extend15Button);

            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal("0:30:00", w.PendingClock.Text);
            Assert.Equal(Due(30), s.PendingDue);
            Assert.Equal(Loc.I["ErrAccessDenied"], w.MessageText.Text);
        });

        [Fact]
        public void Time_left_turns_yellow_then_red_near_the_end() => Sta.Run(() =>
        {
            var w = Make();
            Click(w.StartButton);
            Assert.Equal("Text", w.PendingClock.Tag);
            _now = _now.AddMinutes(55);
            w.OnTick();
            Assert.Equal("Warn", w.PendingClock.Tag);
            Assert.Same(w.FindResource("Warn"), w.PendingClock.Foreground);
            _now = _now.AddMinutes(4);
            w.OnTick();
            Assert.Equal("Urgent", w.PendingClock.Tag);
            Assert.Same(w.FindResource("Urgent"), w.PendingClock.Foreground);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Last_minute_warns_once_and_postponing_rearms_it(bool appTimed) => Sta.Run(() =>
        {
            var previousFlash = MainWindow.Flash;
            var previousShow = MainWindow.ShowNoActivate;
            var flashes = new List<Window>();
            var restores = new List<Window>();
            MainWindow.Flash = flashes.Add;
            MainWindow.ShowNoActivate = window =>
            {
                restores.Add(window);
                window.WindowState = WindowState.Normal;
            };

            try
            {
                var w = Make(new Settings { Hours = 0, Minutes = 2 });
                w.ActHibernate.IsChecked = appTimed;
                Click(w.StartButton);
                var due = _now.AddMinutes(2);
                w.WindowState = WindowState.Minimized;
                Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(w.PendingClock));

                _now = due.AddSeconds(-61);
                w.OnTick();
                Assert.Empty(flashes);
                Assert.Empty(restores);
                Assert.False(w.Topmost);

                _now = due.AddSeconds(-59);
                w.OnTick();
                Assert.Same(w, Assert.Single(flashes));
                Assert.Same(w, Assert.Single(restores));
                Assert.True(w.Topmost);
                Assert.Equal("Less than a minute left.", AutomationProperties.GetName(w.PendingClock));

                _now = due.AddSeconds(-58);
                w.OnTick();
                Assert.Single(flashes);
                Assert.Single(restores);
                Assert.True(w.Topmost);
                Assert.Equal(Loc.I.F("PendingRemaining", w.PendingClock.Text), AutomationProperties.GetName(w.PendingClock));

                Click(w.Extend15Button);
                Assert.False(w.Topmost);

                _now = due.AddMinutes(14);
                w.OnTick();
                Assert.Equal(new Window[] { w, w }, flashes);
                Assert.Single(restores);
                Assert.True(w.Topmost);

                Click(w.AbortButton);
                Assert.False(w.Topmost);
                Assert.False(w.IsVisible);
            }
            finally
            {
                MainWindow.Flash = previousFlash;
                MainWindow.ShowNoActivate = previousShow;
            }
        });

        [Fact]
        public void Pending_countdown_updates_the_window_title_and_taskbar_until_aborted() => Sta.Run(() =>
        {
            var w = Make();
            var start = _now;
            Click(w.StartButton);

            _now = start.AddMinutes(50);
            w.OnTick();
            Assert.Equal("Shutting down in 0:10:00", w.Title);
            Assert.Equal(TaskbarItemProgressState.Normal, w.TaskbarItemInfo.ProgressState);
            Assert.Equal(10d / 60, w.TaskbarItemInfo.ProgressValue, 8);

            _now = start.AddMinutes(56);
            w.OnTick();
            Assert.Equal("Shutting down in 0:04:00", w.Title);
            Assert.Equal(TaskbarItemProgressState.Paused, w.TaskbarItemInfo.ProgressState);
            Assert.Equal(4d / 60, w.TaskbarItemInfo.ProgressValue, 8);

            _now = start.AddSeconds(3570);
            w.OnTick();
            Assert.Equal("Shutting down in 0:00:30", w.Title);
            Assert.Equal(TaskbarItemProgressState.Error, w.TaskbarItemInfo.ProgressState);
            Assert.Equal(30d / 3600, w.TaskbarItemInfo.ProgressValue, 8);

            Click(w.AbortButton);
            Assert.Equal("Shutdown", w.Title);
            Assert.Equal(TaskbarItemProgressState.None, w.TaskbarItemInfo.ProgressState);
            Assert.Equal(0d, w.TaskbarItemInfo.ProgressValue);
        });

        [Fact]
        public void Restored_restart_title_follows_the_active_language() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Restart", PendingDue = Due(10), PendingBoot = Marker });
            Assert.Equal("Restarting in 0:10:00", w.Title);
            try
            {
                Loc.I.Apply("de");
                Assert.Equal("Neustart in 0:10:00", w.Title);
            }
            finally { Loc.I.Apply("en"); }
        });

        [Theory]
        [InlineData(400, "Text")]
        [InlineData(301, "Text")]
        [InlineData(300, "Warn")]
        [InlineData(61, "Warn")]
        [InlineData(60, "Urgent")]
        [InlineData(0, "Urgent")]
        public void Clock_colour_thresholds(int seconds, string key) =>
            Assert.Equal(key, MainWindow.ClockBrushKey(TimeSpan.FromSeconds(seconds)));

        [Fact]
        public void Start_counts_the_time_the_abort_call_took() => Sta.Run(() =>
        {
            var w = Make();
            w.ModeAt.IsChecked = true;
            _runner.Result = a => { if (a == "/a") _now = _now.AddSeconds(10); return 0; };
            Click(w.StartButton);
            Assert.Equal(new[] { "/a", "/s /f /t 3590" }, _runner.Calls);
        });

        [Fact]
        public void Replacing_a_schedule_with_a_time_that_became_too_soon_says_the_old_one_is_gone() => Sta.Run(() =>
        {
            var w = Make();
            w.ModeAt.IsChecked = true;
            _runner.Result = a => { if (a == "/a") _now = _now.AddSeconds(10); return 0; };
            Click(w.StartButton);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            // 65 s ahead passes the first check; the abort call pushes it under the 1 minute floor.
            _now = new DateTime(2026, 10, 16, 12, 0, 55, DateTimeKind.Local);
            w.AtHour.Value = 12;
            w.AtMinute.Value = 2;
            Click(w.StartButton);
            Assert.Equal(new[] { "/a", "/s /f /t 3590", "/a" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Loc.I["MsgOldCancelled"], w.MessageText.Text);
            Assert.Null(_store.Load().PendingAction);
        });

        [Fact]
        public void Pending_from_an_earlier_run_is_restored() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Restart", PendingDue = Due(30), PendingBoot = Marker });
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal("Restart scheduled", w.PendingTitle.Text);
            Assert.Equal("0:30:00", w.PendingClock.Text);
        });

        [Fact]
        public void Pending_survives_setting_the_clock() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Restart", PendingDue = Due(30), PendingBoot = Marker };
            // The clock jumps two hours ahead after scheduling; Windows' timer still has 30 minutes left.
            _now = _now.AddHours(2);
            _clockSet = TimeSpan.FromHours(2);
            var w = Make(s);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal("0:30:00", w.PendingClock.Text);
        });

        [Fact]
        public void Setting_the_clock_while_open_keeps_the_time_left() => Sta.Run(() =>
        {
            var w = Make();
            Click(w.StartButton);
            _now = _now.AddMinutes(-45);
            _clockSet = TimeSpan.FromMinutes(-45);
            w.OnTimeChanged(null, EventArgs.Empty);
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            Assert.Equal("1:00:00", w.PendingClock.Text);
        });

        [Fact]
        public void Pending_after_a_fast_startup_is_dropped() => Sta.Run(() =>
        {
            // Fast Startup keeps the uptime counting, but Windows logs a new boot.
            _marker = "boot:43";
            var w = Make(new Settings { PendingAction = "Restart", PendingDue = Due(30), PendingBoot = Marker });
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void Pending_is_kept_when_the_boot_cannot_be_told() => Sta.Run(() =>
        {
            _marker = null;
            var w = Make(new Settings { PendingAction = "Restart", PendingDue = Due(30), PendingBoot = Marker });
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            _marker = Marker;
            var w2 = Make(new Settings { PendingAction = "Restart", PendingDue = Due(30), PendingBoot = "unknown" });
            Assert.Equal(Visibility.Visible, w2.PendingCard.Visibility);
        });

        [Fact]
        public void Pending_from_another_boot_is_dropped() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Restart", PendingDue = Due(30), PendingBoot = "boot:41" };
            var w = Make(s);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Null(s.PendingAction);
            Assert.Null(s.PendingDue);
            Assert.Null(s.PendingBoot);
        });

        [Fact]
        public void Pending_without_a_boot_marker_is_dropped() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Restart", PendingDue = Due(30) });
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void Started_schedule_stores_the_boot_marker_and_due_uptime() => Sta.Run(() =>
        {
            var w = Make();
            Click(w.StartButton);
            Assert.Equal(Marker, _store.Load().PendingBoot);
            Assert.Equal(Due(60), _store.Load().PendingDue);
        });

        [Fact]
        public void Expired_pending_is_not_restored() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Restart", PendingDue = Due(-1), PendingBoot = Marker });
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void Invalid_pending_due_is_not_restored() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Restart", PendingDue = long.MaxValue, PendingBoot = Marker };
            var w = Make(s);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Null(s.PendingAction);
            Assert.Null(s.PendingDue);
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
            var s = new Settings { PendingAction = "Shutdown", PendingDue = Due(-5), PendingBoot = Marker };
            Make(s);
            Assert.Null(s.PendingAction);
            Assert.Null(s.PendingDue);
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
            Assert.Equal("shutdown /s /f /t 3600", w.MessageDetail.Text);
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
        public void German_days_are_written_out() => Sta.Run(() =>
        {
            Loc.I.Apply("de");
            try
            {
                Assert.Equal("1 Tag 02:00:00", Loc.I.Clock(TimeSpan.FromHours(26)));
                Assert.Equal("2 Tage 00:00:00", Loc.I.Clock(TimeSpan.FromDays(2)));
                Assert.Equal("in 1 Tag 3 Std.", Loc.I.F("SummaryIn", Loc.I.Span(TimeSpan.FromHours(27))));
                Assert.Equal("in 2 Tagen", Loc.I.F("SummaryIn", Loc.I.Span(TimeSpan.FromDays(2))));
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
            _runner.Result = a => ShutdownRunner.ErrorAccessDenied;
            Click(w.AbortButton);
            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
            _now = _now.AddHours(2);
            w.OnTick();
            Assert.DoesNotContain("/h /f", _runner.Calls);
        });

        [Fact]
        public void Abort_of_a_schedule_cancelled_elsewhere_just_clears_it() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Shutdown", PendingDue = Due(30), PendingBoot = Marker });
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            _runner.Result = a => ShutdownRunner.ErrorNoShutdownInProgress;
            Click(w.AbortButton);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
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
            Assert.Equal("Hibernation scheduled", w.PendingTitle.Text);
            Assert.Equal(Visibility.Visible, w.PendingKeepOpen.Visibility);
            Assert.Null(_store.Load().PendingAction);

            _now = _now.AddHours(1).AddSeconds(1);
            w.OnTick();
            Assert.Equal(new[] { "/a", "/h /f" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void Postponing_hibernate_moves_its_due_uptime_without_calling_the_runner() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            _runner.Calls.Clear();
            Click(w.Extend60Button);

            Assert.Empty(_runner.Calls);
            Assert.Equal("2:00:00", w.PendingClock.Text);
            Assert.Equal("Hibernating in 2:00:00", w.Title);
            Assert.Null(_store.Load().PendingDue);

            _now = _now.AddHours(1);
            w.OnTick();
            Assert.Empty(_runner.Calls);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal(0.5, w.TaskbarItemInfo.ProgressValue, 8);

            _now = _now.AddHours(1);
            w.OnTick();
            Assert.Equal(new[] { "/h /f" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void App_timed_run_missed_during_sleep_is_skipped() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Assert.Equal(Loc.I["AppTimedHibernate"], w.AppTimedText.Text);
            Click(w.StartButton);
            _now = _now.AddHours(1).AddMinutes(10);
            w.OnTick();
            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal(Loc.I["MsgMissed"], w.MessageText.Text);
        });

        [Fact]
        public void App_timed_pending_keeps_the_system_awake_until_it_ends() => Sta.Run(() =>
        {
            var w = Make();
            Click(w.StartButton);
            Assert.Empty(_awake);

            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            Assert.Equal(new[] { true }, _awake);
            Click(w.AbortButton);
            Assert.Equal(new[] { true, false }, _awake);

            Click(w.StartButton);
            _now = _now.AddHours(1);
            w.OnTick();
            Assert.Equal(new[] { true, false, true, false }, _awake);
        });

        [Fact]
        public void Closing_releases_the_awake_request() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            Click(w.CloseAnywayButton);
            Assert.Equal(new[] { true, false }, _awake);
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
            Assert.Equal("Shutdown", _store.Load().PendingAction);
            Click(w.AbortButton);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void Restored_windows_timed_pending_past_its_target_is_dropped() => Sta.Run(() =>
        {
            var s = new Settings { PendingAction = "Shutdown", PendingDue = Due(-1), PendingBoot = Marker };
            var w = Make(s);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Null(s.PendingDue);
        });

        [Fact]
        public void Restart_is_forced_and_needs_no_open_window() => Sta.Run(() =>
        {
            var w = Make();
            w.ActRestart.IsChecked = true;
            Assert.Equal(Visibility.Collapsed, w.AppTimedNote.Visibility);
            Click(w.StartButton);
            Assert.Equal(new[] { "/a", "/r /f /t 3600" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingKeepOpen.Visibility);
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
        public void A_newer_build_is_refused_while_a_hibernate_needs_the_window() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            bool closed = false;
            w.Closed += (s, e) => closed = true;
            Assert.False(w.CloseForNewerBuild());
            Assert.Equal(Loc.I["NewerBuildWaits"], w.MessageText.Text);
            // The newer copy has already given up: "Close anyway" would leave no window open.
            Assert.Equal(Visibility.Collapsed, w.CloseAnywayButton.Visibility);
            Assert.False(closed);
        });

        [Fact]
        public void A_newer_build_is_refused_while_an_unsaved_schedule_exists() => Sta.Run(() =>
        {
            var w = Make();
            // A settings file of a newer version is never overwritten, so nothing gets saved.
            File.WriteAllText(_store.FilePath, "{\"Version\":99}");
            _store.Load();
            Click(w.StartButton);
            bool closed = false;
            w.Closed += (s, e) => closed = true;
            Assert.False(w.CloseForNewerBuild());
            Assert.Equal(Loc.I["NewerBuildWaitsUnsaved"], w.MessageText.Text);
            Assert.False(closed);
            // Once the schedule is cancelled, nothing is lost by the takeover.
            Click(w.AbortButton);
            Assert.True(w.CloseForNewerBuild());
            Assert.True(closed);
        });

        [Fact]
        public void A_newer_build_closes_the_window_otherwise() => Sta.Run(() =>
        {
            var w = Make();
            bool closed = false;
            w.Closed += (s, e) => closed = true;
            Assert.True(w.CloseForNewerBuild());
            Assert.True(closed);
        });

        [Fact]
        public void Title_bar_close_goes_through_the_close_guard() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            bool closed = false;
            w.Closed += (s, e) => closed = true;
            Click(w.CloseButton);
            Assert.False(closed);
            Assert.Equal(Loc.I["CloseGuard"], w.MessageText.Text);
        });

        [Fact]
        public void Title_bar_buttons_carry_ids_and_names_but_stay_out_of_the_tab_order() => Sta.Run(() =>
        {
            var w = Make();
            Loc.I.Apply("de");
            Assert.Equal("TitleMinimize", AutomationProperties.GetAutomationId(w.MinimizeButton));
            Assert.Equal("TitleClose", AutomationProperties.GetAutomationId(w.CloseButton));
            Assert.Equal("Minimieren", AutomationProperties.GetName(w.MinimizeButton));
            Assert.Equal("Schließen", AutomationProperties.GetName(w.CloseButton));
            Assert.False(w.MinimizeButton.IsTabStop);
            Assert.False(w.CloseButton.IsTabStop);
            Assert.Null(w.CloseButton.ToolTip);
        });

        [Fact]
        public void Close_warning_goes_once_the_countdown_has_run() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            var e = new CancelEventArgs();
            typeof(MainWindow).GetMethod("OnClosing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(w, new object[] { e });
            Assert.Equal(Visibility.Visible, w.MessageCard.Visibility);
            _now = _now.AddHours(1).AddSeconds(1);
            w.OnTick();
            Assert.Equal(new[] { "/a", "/h /f" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
        });

        [Fact]
        public void App_timed_run_ignores_a_clock_jump_not_yet_reported() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Click(w.StartButton);
            // The clock is set two hours ahead; the tick comes before the TimeChanged event.
            _now = _now.AddHours(2);
            _clockSet = TimeSpan.FromHours(2);
            w.OnTick();
            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
        });

        [Fact]
        public void Timed_out_call_that_cannot_be_taken_back_keeps_the_card() => Sta.Run(() =>
        {
            var w = Make();
            int aborts = 0;
            // The first /a (before scheduling) finds nothing; the take-back /a fails.
            _runner.Result = a => a != "/a" ? ShutdownRunner.TimeoutCode : aborts++ == 0 ? ShutdownRunner.ErrorNoShutdownInProgress : 1;
            Click(w.StartButton);
            Assert.Equal(new[] { "/a", "/s /f /t 3600", "/a" }, _runner.Calls);
            Assert.Equal(Loc.I["ErrTimeout"], w.MessageText.Text);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal("Shutdown", _store.Load().PendingAction);
        });

        [Fact]
        public void Schedule_that_cannot_be_saved_says_so() => Sta.Run(() =>
        {
            var w = Make();
            // A settings file of a newer version is never overwritten, so nothing gets saved.
            File.WriteAllText(_store.FilePath, "{\"Version\":99}");
            _store.Load();
            Click(w.StartButton);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal(Loc.I["MsgNotSaved"], w.MessageText.Text);
        });

        [Fact]
        public void Damaged_pending_action_is_not_restored() => Sta.Run(() =>
        {
            foreach (var action in new[] { "99", "Hibernate" })
            {
                var s = new Settings { PendingAction = action, PendingDue = Due(30), PendingBoot = Marker, Action = "7" };
                var w = Make(s);
                Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
                Assert.Null(s.PendingAction);
                Assert.True(w.ActShutdown.IsChecked);
            }
        });

        [Fact]
        public void Zero_countdown_disables_start() => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 0;
            w.DurMinutes.Value = 0;
            Assert.False(w.StartButton.IsEnabled);
            Assert.Equal(Loc.I["ErrTooShort"], w.ResultText.Text);
        });

        [Fact]
        public void Chip_sets_the_countdown() => Sta.Run(() =>
        {
            var w = Make();
            // 15 min, 30 min, 1 h, 2 h, 3 h.
            Assert.Equal(5, w.Chips.Children.Count);
            Click((Button)w.Chips.Children[1]);
            Assert.Equal(0, w.DurHours.Value);
            Assert.Equal(30, w.DurMinutes.Value);
        });

        [Fact]
        public void Date_mode_rejects_past_time_and_steps_days() => Sta.Run(() =>
        {
            var w = Make();
            w.ModeAt.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.AtPanel.Visibility);
            Assert.Equal(Visibility.Visible, w.DateRow.Visibility);
            Assert.Equal(Visibility.Collapsed, w.CountdownPanel.Visibility);
            // Default is the next full 5 minutes one hour ahead.
            Assert.Equal(13, w.AtHour.Value);
            Assert.Equal(0, w.AtMinute.Value);
            Assert.False(w.PrevDay.IsEnabled);

            w.AtHour.Value = 11;
            Assert.False(w.StartButton.IsEnabled);
            Assert.Equal(Loc.I["ErrPast"], w.ResultText.Text);

            Click(w.NextDay);
            Assert.True(w.StartButton.IsEnabled);
            // Date mode shows the time left; the countdown chips are hidden.
            Assert.Equal("in 23 h", w.ResultText.Text);
            Assert.Equal(Visibility.Collapsed, w.Chips.Visibility);
            Assert.True(w.PrevDay.IsEnabled);
            Click(w.PrevDay);
            Assert.False(w.StartButton.IsEnabled);
        });

        [Fact]
        public void Date_mode_summary_rounds_the_time_left_up() => Sta.Run(() =>
        {
            var w = Make();
            w.ModeAt.IsChecked = true;
            _now = _now.AddMilliseconds(400);
            w.OnTick();
            Assert.Equal("in 1 h", w.ResultText.Text);
            _now = _now.AddMinutes(30);
            w.OnTick();
            Assert.Equal("in 30 min", w.ResultText.Text);
        });

        [Fact]
        public void Typed_value_above_the_maximum_counts_as_the_maximum() => Sta.Run(() =>
        {
            var w = Make();
            w.DurMinutes.Box.Text = "7";
            Assert.Equal(7, w.DurMinutes.Value);
            // The summary must not keep showing 7 while 75 is in the field.
            w.DurMinutes.Box.Text = "75";
            Assert.Equal(59, w.DurMinutes.Value);
            var end = _now.AddHours(1).AddMinutes(59);
            Assert.Equal(Loc.I.F("SummaryAt", Loc.I.DateText(end), Loc.I.TimeText(end)), w.ResultText.Text);
        });

        [Fact]
        public void Time_change_refreshes_the_ui_on_the_dispatcher() => Sta.Run(() =>
        {
            var w = Make();
            w.ResultText.Text = "stale";
            w.OnTimeChanged(null, EventArgs.Empty);
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            Assert.NotEqual("stale", w.ResultText.Text);
        });

        [Fact]
        public void Messages_are_an_assertive_live_region() => Sta.Run(() =>
        {
            var w = Make();
            Assert.Equal(System.Windows.Automation.AutomationLiveSetting.Assertive,
                System.Windows.Automation.AutomationProperties.GetLiveSetting(w.MessageText));
            w.ShowError("ErrStart", "x", false);
            Assert.Equal("x", w.MessageDetail.Text);
        });

        [Fact]
        public void Theme_override_ignores_case() => Sta.Run(() =>
        {
            var before = Theme.Override;
            try
            {
                Theme.Override = "DARK";
                Theme.Apply(new ResourceDictionary());
                Assert.True(Theme.IsDark);
                Theme.Override = "Light";
                Theme.Apply(new ResourceDictionary());
                Assert.False(Theme.IsDark);
            }
            finally { Theme.Override = before; Theme.Apply(Application.Current.Resources); }
        });

        [Fact]
        public void Accent_fill_carries_white_text_in_both_themes() => Sta.Run(() =>
        {
            var before = Theme.Override;
            try
            {
                foreach (var t in new[] { "dark", "light" })
                {
                    Theme.Override = t;
                    var res = new ResourceDictionary();
                    Theme.Apply(res);
                    Assert.Equal(System.Windows.Media.Colors.White, ((System.Windows.Media.SolidColorBrush)res["AccentText"]).Color);
                    Assert.Equal(Theme.AccentPalette()[4], ((System.Windows.Media.SolidColorBrush)res["Accent"]).Color);
                }
            }
            finally { Theme.Override = before; Theme.Apply(Application.Current.Resources); }
        });

        [Fact]
        public void Target_summary_is_neutral_text_not_accent() => Sta.Run(() =>
        {
            var w = Make();
            Assert.Same(Application.Current.Resources["Text"], w.ResultText.Foreground);
        });

        [Fact]
        public void Language_change_rerenders_live() => Sta.Run(() =>
        {
            var w = Make();
            Assert.Equal("Schedule shutdown", w.StartText.Text);
            Assert.Equal("Extend15", AutomationProperties.GetAutomationId(w.Extend15Button));
            Assert.Equal("Extend60", AutomationProperties.GetAutomationId(w.Extend60Button));
            Assert.Equal("+15 min", w.Extend15Button.Content);
            Assert.Equal("+1 h", w.Extend60Button.Content);
            Assert.Equal("Postpone by 15 minutes", AutomationProperties.GetName(w.Extend15Button));
            Assert.Equal("Postpone by 1 hour", AutomationProperties.GetName(w.Extend60Button));
            Loc.I.Apply("de");
            Assert.Equal("Herunterfahren planen", w.StartText.Text);
            Assert.Equal("Stunden", w.DurHours.CaptionText.Text);
            Assert.Equal("+15 Min.", w.Extend15Button.Content);
            Assert.Equal("+1 Std.", w.Extend60Button.Content);
            Assert.Equal("Um 15 Minuten verschieben", AutomationProperties.GetName(w.Extend15Button));
            Assert.Equal("Um 1 Stunde verschieben", AutomationProperties.GetName(w.Extend60Button));
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
        public void Text_fields_open_the_app_styled_menu() => Sta.Run(() =>
        {
            var w = Make();
            var res = Application.Current.Resources;
            Assert.IsType<Style>(res[typeof(ContextMenu)]);
            Assert.IsType<Style>(res[typeof(MenuItem)]);
            Assert.IsType<Style>(res[MenuItem.SeparatorStyleKey]);

            var readOnly = (ContextMenu)res["ReadOnlyTextMenu"];
            Assert.Same(readOnly, w.MessageText.ContextMenu);
            Assert.Same(readOnly, w.MessageDetail.ContextMenu);
            foreach (var sp in new[] { w.DurHours, w.DurMinutes, w.AtHour, w.AtMinute })
                Assert.Same(res["EditTextMenu"], sp.Box.ContextMenu);

            var copy = (MenuItem)readOnly.Items[0];
            var selectAll = (MenuItem)readOnly.Items[1];
            Assert.Equal("Copy", copy.Header);
            Assert.Equal("Select all", selectAll.Header);
            Loc.I.Apply("de");
            Assert.Equal("Alles auswählen", selectAll.Header);
            Loc.I.Apply("en");

            // The entries act on the field that was right-clicked.
            w.ShowError("ErrStart", "detail text", false);
            readOnly.PlacementTarget = w.MessageDetail;
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));
            Assert.Same(w.MessageDetail, selectAll.CommandTarget);
            ((RoutedCommand)selectAll.Command).Execute(null, selectAll.CommandTarget);
            Assert.Equal("detail text", w.MessageDetail.SelectedText);
            Assert.True(((RoutedCommand)copy.Command).CanExecute(null, copy.CommandTarget));
        });

        [Fact]
        public void Real_runner_is_blocked_in_tests()
        {
            Sta.Run(() => { });
            Assert.Throws<InvalidOperationException>(() => new ShutdownRunner().Run("/a"));
        }
    }
}
