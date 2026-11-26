using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
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
        readonly FakeSuspendRunner _suspendRunner = new FakeSuspendRunner();
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
            var w = new MainWindow(_store, s, _runner, _suspendRunner, () => _now);
            _windows.Add(w);
            return w;
        }

        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));

        sealed class FakeTray : ITrayIcon
        {
            public Action Restore { get; private set; }
            public Func<ContextMenu> CreateMenu { get; private set; }
            public string Tooltip { get; private set; }
            public bool Visible { get; private set; }
            public bool Disposed { get; private set; }

            public ITrayIcon Create(Action restore, Func<ContextMenu> createMenu)
            {
                Restore = restore;
                CreateMenu = createMenu;
                return this;
            }

            public void Show(string text)
            {
                Tooltip = text;
                Visible = true;
            }

            public void Hide() => Visible = false;

            public void Dispose()
            {
                Hide();
                Disposed = true;
            }
        }

        static void ClickTrayItem(ContextMenu menu, string id)
        {
            foreach (var entry in menu.Items)
            {
                if (entry is MenuItem item && AutomationProperties.GetAutomationId(item) == id)
                {
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
                    return;
                }
            }
            Assert.Fail("Missing tray menu item: " + id);
        }

        static void FlushDispatcher() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

        [Theory]
        [InlineData("45", 2700)]
        [InlineData("65", 3900)]
        public void Enter_in_minutes_commits_and_schedules(string minutes, int seconds) => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = 0, Minutes = 5 });
            var box = w.DurMinutes.Box;

            // Give the access key a presentation source without showing the window.
            using (var source = new HwndSource(new HwndSourceParameters()))
            {
                box.Text = minutes;

                var enter = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                box.RaiseEvent(enter);
                Assert.False(enter.Handled);

                var button = w.StartButton;
                var parent = (Panel)button.Parent;
                int index = parent.Children.IndexOf(button);
                parent.Children.RemoveAt(index);
                try
                {
                    source.RootVisual = button;
                    Assert.True(AccessKeyManager.IsKeyRegistered(source, "\r"));
                    AccessKeyManager.ProcessKey(source, "\r", false);
                }
                finally
                {
                    source.RootVisual = null;
                    parent.Children.Insert(index, button);
                }
            }

            Assert.Equal(new[] { "/a", $"/s /f /t {seconds} /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
        });

        [Fact]
        public void Forced_shutdown_replaces_pending_and_hands_the_timer_to_windows() => Sta.Run(() =>
        {
            var w = Make();
            Assert.True(w.StartButton.IsEnabled);
            // Countdown mode shows the end time.
            Assert.Equal("Today at " + Loc.I.TimeText(_now.AddHours(1)), w.ResultText.Text);
            Click(w.StartButton);
            Assert.Equal(new[] { "/a", "/s /f /t 3600 /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
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

            Assert.Equal(new[] { "/a", "/s /f /t 2400 /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            Assert.Equal("0:40:00", w.PendingClock.Text);
            Assert.Equal(40d / 45, w.TaskbarItemInfo.ProgressValue, 8);
            var target = _now.AddMinutes(40);
            Assert.Equal("Today at " + Loc.I.TimeText(target), w.PendingWhen.Text);
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
        public void Windows_timed_card_past_its_target_stops_staying_on_top() => Sta.Run(() =>
        {
            var previousFlash = MainWindow.Flash;
            MainWindow.Flash = _ => { };
            try
            {
                var w = Make(new Settings { Hours = 0, Minutes = 2 });
                Click(w.StartButton);
                var due = _now.AddMinutes(2);

                _now = due.AddSeconds(-30);
                w.OnTick();
                Assert.True(w.Topmost);

                // Windows' timer was cancelled elsewhere: the card stays, the window must not cover everything.
                _now = due.AddSeconds(5);
                w.OnTick();
                Assert.False(w.Topmost);
                Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
            }
            finally { MainWindow.Flash = previousFlash; }
        });

        [Fact]
        public void Long_command_line_countdown_survives_the_next_start() => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = 1500, Minutes = 0 });
            Assert.Equal(1500, w.DurHours.Value);
            Assert.NotEqual(Visibility.Visible, w.MessageCard.Visibility);
        });

        [Fact]
        public void Pending_countdown_updates_the_window_title_and_taskbar_until_aborted() => Sta.Run(() =>
        {
            var w = Make();
            var start = _now;
            Click(w.StartButton);

            _now = start.AddMinutes(50);
            w.OnTick();
            Assert.Equal("Shutdown: 0:10:00", w.Title);
            Assert.Equal(TaskbarItemProgressState.Normal, w.TaskbarItemInfo.ProgressState);
            Assert.Equal(10d / 60, w.TaskbarItemInfo.ProgressValue, 8);

            _now = start.AddMinutes(56);
            w.OnTick();
            Assert.Equal("Shutdown: 0:04:00", w.Title);
            Assert.Equal(TaskbarItemProgressState.Paused, w.TaskbarItemInfo.ProgressState);
            Assert.Equal(4d / 60, w.TaskbarItemInfo.ProgressValue, 8);

            _now = start.AddSeconds(3570);
            w.OnTick();
            Assert.Equal("Shutdown: 0:00:30", w.Title);
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
            Assert.Equal("Restart: 0:10:00", w.Title);
            try
            {
                Loc.I.Apply("de");
                Assert.Equal("Neustart: 0:10:00", w.Title);
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
            Assert.Equal(new[] { "/a", "/s /f /t 3590 /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
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
            Assert.Equal(new[] { "/a", "/s /f /t 3590 /c \"Scheduled with the Shutdown app.\"", "/a" }, _runner.Calls);
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
            Assert.Equal(new[] { "/a", "/s /f /t 3600 /c \"Scheduled with the Shutdown app.\"", "/a" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Loc.I["ErrTimeout"], w.MessageText.Text);
            Assert.Equal("shutdown /s /f /t 3600 /c \"Scheduled with the Shutdown app.\"", w.MessageDetail.Text);
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
            Assert.Equal("shutdown /s /f /t 3600 /c \"Scheduled with the Shutdown app.\": exit code 5", w.MessageDetail.Text);
            Assert.Equal(Visibility.Visible, w.CopyButton.Visibility);
        });

        [Fact]
        public void Hibernate_runs_in_the_app_and_tells_to_keep_the_window_open() => Sta.Run(() =>
        {
            var w = Make();
            w.ActHibernate.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.AppTimedNote.Visibility);
            Assert.Equal(Loc.I["AppTimedHibernate"] + "\n" + Loc.I["PendingKeepOpen"], UiWindowTests.InlineText(w.AppTimedText));
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
        public void Sleep_stays_pending_until_due_and_runs_once_through_the_suspend_runner() => Sta.Run(() =>
        {
            var w = Make();
            w.ActSleep.IsEnabled = true;
            w.ActSleep.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.AppTimedNote.Visibility);
            Assert.Equal(Loc.I["AppTimedSleep"] + "\n" + Loc.I["PendingKeepOpen"], UiWindowTests.InlineText(w.AppTimedText));

            Click(w.StartButton);
            Assert.Equal("Sleep scheduled", w.PendingTitle.Text);
            Assert.Equal("Sleep: 1:00:00", w.Title);
            Assert.Equal(Visibility.Visible, w.PendingKeepOpen.Visibility);
            Assert.Equal(new[] { true }, _awake);
            var saved = _store.Load();
            Assert.Equal("Sleep", saved.Action);
            Assert.Null(saved.PendingAction);

            Assert.False(w.CloseForNewerBuild());
            Assert.Equal(Loc.I["NewerBuildWaits"], w.MessageText.Text);
            Click(w.CloseButton);
            Assert.Equal(Loc.I["CloseGuard"], w.MessageText.Text);
            Assert.Equal(Visibility.Visible, w.CloseAnywayButton.Visibility);

            w.OnTick();
            Assert.Equal(0, _suspendRunner.Calls);
            _now = _now.AddHours(1);
            w.OnTick();
            w.OnTick();
            Assert.Equal(1, _suspendRunner.Calls);
            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal(new[] { true, false }, _awake);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
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
            Assert.Equal("Hibernate: 2:00:00", w.Title);
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void App_timed_run_missed_during_sleep_is_skipped(bool sleep) => Sta.Run(() =>
        {
            var w = Make();
            var action = sleep ? w.ActSleep : w.ActHibernate;
            action.IsEnabled = true;
            action.IsChecked = true;
            Click(w.StartButton);
            _now = _now.AddHours(1).AddMinutes(10);
            w.OnTick();
            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal(0, _suspendRunner.Calls);
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
            Assert.Equal(new[] { "/a", "/r /f /t 3600 /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingKeepOpen.Visibility);
        });

        [Fact]
        public void Command_applies_restart_and_normalizes_90_minutes() => Sta.Run(() =>
        {
            var w = Make(new Settings { Mode = "At" });
            w.ApplyCommand(CommandLine.Parse(new[] { "--restart", "--in", "90" }));

            Assert.True(w.ActRestart.IsChecked);
            Assert.True(w.ModeCountdown.IsChecked);
            Assert.Equal(1, w.DurHours.Value);
            Assert.Equal(30, w.DurMinutes.Value);
            Assert.Equal(new[] { "/a", "/r /f /t 5400 /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
            Assert.Equal(Visibility.Visible, w.PendingCard.Visibility);
        });

        [Fact]
        public void Command_countdown_uses_the_saved_action_and_supports_the_windows_limit() => Sta.Run(() =>
        {
            var w = Make(new Settings { Action = "Restart" });
            w.ApplyCommand(CommandLine.Parse(new[] { "--in", "87600:00" }));

            Assert.True(w.ActRestart.IsChecked);
            Assert.Equal(87600, w.DurHours.Value);
            Assert.Equal(0, w.DurMinutes.Value);
            Assert.Equal(new[] { "/a", $"/r /f /t {Schedule.MaxSeconds} /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
        });

        [Theory]
        [InlineData("13:00", 13, 0, 3600)]
        [InlineData("11:00", 11, 1, 82800)]
        public void Command_at_sets_the_date_and_schedules_immediately(string time, int hour, int days, int seconds) => Sta.Run(() =>
        {
            var w = Make();
            w.ApplyCommand(CommandLine.Parse(new[] { "--at", time }));

            Assert.True(w.ModeAt.IsChecked);
            Assert.Equal(hour, w.AtHour.Value);
            Assert.Equal(0, w.AtMinute.Value);
            Assert.Equal(Loc.I.DateText(_now.Date.AddDays(days)), w.DateLabel.Text);
            Assert.Equal(new[] { "/a", $"/s /f /t {seconds} /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
        });

        [Fact]
        public void Command_action_without_a_time_only_selects_the_action() => Sta.Run(() =>
        {
            var w = Make();
            w.ApplyCommand(CommandLine.Parse(new[] { "--restart" }));

            Assert.True(w.ActRestart.IsChecked);
            Assert.Empty(_runner.Calls);
        });

        [Fact]
        public void Command_abort_calls_the_runner_and_clears_pending() => Sta.Run(() =>
        {
            var w = Make(new Settings { PendingAction = "Shutdown", PendingDue = Due(30), PendingBoot = Marker });
            w.ApplyCommand(CommandLine.Parse(new[] { "--abort" }));

            Assert.Equal(new[] { "/a" }, _runner.Calls);
            Assert.Equal(Visibility.Collapsed, w.PendingCard.Visibility);
        });

        [Fact]
        public void Command_abort_without_pending_shows_info_without_calling_the_runner() => Sta.Run(() =>
        {
            var w = Make();
            w.ApplyCommand(CommandLine.Parse(new[] { "--abort" }));

            Assert.Empty(_runner.Calls);
            Assert.Equal(Visibility.Visible, w.MessageCard.Visibility);
            Assert.Equal(Visibility.Collapsed, w.CopyButton.Visibility);
            Assert.Equal("Nothing is scheduled.", w.MessageText.Text);
            Loc.I.Apply("de");
            Assert.Equal("Es ist nichts geplant.", w.MessageText.Text);
        });

        [Fact]
        public void A_command_acknowledgement_wins_when_the_owner_has_also_released_the_mutex()
        {
            using var mutex = new Mutex();
            using var ack = new AutoResetEvent(true);
            Assert.Equal(App.Reply.Acknowledged, App.WaitForReply(mutex, ack, TimeSpan.Zero));
            Assert.False(ack.WaitOne(0));
            Assert.Throws<ApplicationException>(() => mutex.ReleaseMutex());
        }

        [Fact]
        public void A_jump_list_shell_failure_is_caught_during_registration() => Sta.Run(() =>
        {
            var app = Assert.IsType<App>(Application.Current);
            bool attempted = false;
            app.RegisterJumpList(_ =>
            {
                attempted = true;
                throw new COMException("Shell unavailable");
            });
            Assert.True(attempted);
        });

        [Theory]
        [InlineData("--hibernate", "HibernateUnavailable")]
        [InlineData("--sleep", "SleepUnavailable")]
        public void Unavailable_command_shows_the_action_error_without_scheduling(string option, string error) => Sta.Run(() =>
        {
            var hibernateAllowed = MainWindow.HibernateAllowed;
            var sleepAllowed = MainWindow.SleepAllowed;
            try
            {
                MainWindow.HibernateAllowed = () => false;
                MainWindow.SleepAllowed = () => false;
                var w = Make();
                w.ApplyCommand(CommandLine.Parse(new[] { option, "--in", "30" }));

                Assert.Equal(Visibility.Visible, w.MessageCard.Visibility);
                Assert.Equal(Loc.I[error], w.MessageText.Text);
                Assert.True(w.ActShutdown.IsChecked);
                Assert.Empty(_runner.Calls);
            }
            finally
            {
                MainWindow.HibernateAllowed = hibernateAllowed;
                MainWindow.SleepAllowed = sleepAllowed;
            }
        });

        [Fact]
        public void Invalid_command_shows_the_localized_error_and_usage() => Sta.Run(() =>
        {
            var w = Make();
            w.ApplyCommand(CommandLine.Parse(new[] { "--other" }));

            Assert.Equal(Visibility.Visible, w.MessageCard.Visibility);
            Assert.Equal("The command line could not be read.", w.MessageText.Text);
            Assert.Equal(CommandLine.Usage, w.MessageDetail.Text);
            Assert.Equal(Visibility.Collapsed, w.OpenLogButton.Visibility);
            Assert.Empty(_runner.Calls);
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
        public void Minimize_creates_the_tray_icon_and_either_open_action_restores_the_window() => Sta.Run(() =>
        {
            var w = Make();
            var tray = new FakeTray();
            w.CreateTrayIcon = tray.Create;
            w.ShowActivated = false;
            w.Show();
            FlushDispatcher();
            Assert.Null(tray.Restore);

            Click(w.MinimizeButton);
            FlushDispatcher();
            Assert.True(w.IsLoaded);
            Assert.False(w.IsVisible);
            Assert.True(w.IsInTray);
            Assert.True(tray.Visible);
            Assert.Equal("Shutdown", tray.Tooltip);

            var menu = tray.CreateMenu();
            Assert.Equal("TrayMenu", AutomationProperties.GetAutomationId(menu));
            Assert.Equal(3, menu.Items.Count);
            Assert.Equal("Open", Assert.IsType<MenuItem>(menu.Items[0]).Header);
            Assert.IsType<Separator>(menu.Items[1]);
            Assert.Equal("Exit", Assert.IsType<MenuItem>(menu.Items[2]).Header);

            tray.Restore();
            Assert.Equal(WindowState.Normal, w.WindowState);
            Assert.True(w.IsVisible);
            Assert.True(w.ShowInTaskbar);
            Assert.False(w.IsInTray);
            Assert.False(tray.Visible);

            w.WindowState = WindowState.Minimized;
            Assert.True(tray.Visible);
            Loc.I.Apply("de");
            var germanMenu = tray.CreateMenu();
            Assert.Equal("Öffnen", Assert.IsType<MenuItem>(germanMenu.Items[0]).Header);
            Assert.Equal("Beenden", Assert.IsType<MenuItem>(germanMenu.Items[2]).Header);
            ClickTrayItem(germanMenu, "TrayOpen");
            Assert.True(w.IsVisible);
            Assert.False(tray.Visible);
        });

        [Fact]
        public void Tray_exit_restores_the_hibernate_guard_and_closes_once_the_schedule_is_cancelled() => Sta.Run(() =>
        {
            var w = Make(new Settings { Action = "Hibernate" });
            var tray = new FakeTray();
            w.CreateTrayIcon = tray.Create;
            w.ShowActivated = false;
            w.Show();
            Click(w.StartButton);
            w.WindowState = WindowState.Minimized;
            Assert.Equal("Hibernate: 1:00:00", tray.Tooltip);

            _now = _now.AddMinutes(5);
            w.OnTick();
            Assert.Equal("Hibernate: 0:55:00", tray.Tooltip);
            var menu = tray.CreateMenu();
            Assert.Equal(w.Extend15Button.Content, Assert.IsType<MenuItem>(menu.Items[1]).Header);
            Assert.Equal(Loc.I["Abort"], Assert.IsType<MenuItem>(menu.Items[2]).Header);
            ClickTrayItem(menu, "TrayExtend15");
            Assert.True(w.IsInTray);
            Assert.Equal("Hibernate: 1:10:00", tray.Tooltip);

            ClickTrayItem(menu, "TrayExit");
            FlushDispatcher();
            Assert.Equal(Loc.I["CloseGuard"], w.MessageText.Text);
            Assert.True(w.IsVisible);
            Assert.Equal(WindowState.Normal, w.WindowState);
            Assert.False(tray.Visible);
            Assert.False(tray.Disposed);

            w.WindowState = WindowState.Minimized;
            FlushDispatcher();
            Assert.True(tray.Visible);
            ClickTrayItem(tray.CreateMenu(), "TrayAbort");
            Assert.Equal("Shutdown", tray.Tooltip);
            ClickTrayItem(tray.CreateMenu(), "TrayExit");
            Assert.True(tray.Disposed);
            Assert.False(tray.Visible);
            Assert.False(w.IsInTray);
        });

        [Fact]
        public void Last_minute_restores_the_window_from_the_tray_without_activation() => Sta.Run(() =>
        {
            var previousShow = MainWindow.ShowNoActivate;
            MainWindow.ShowNoActivate = MainWindow.ShowWithoutFocus;
            try
            {
                var w = Make(new Settings { Action = "Hibernate", Hours = 0, Minutes = 2 });
                var tray = new FakeTray();
                w.CreateTrayIcon = tray.Create;
                w.ShowActivated = false;
                w.Show();
                w.ShowActivated = true;
                Click(w.StartButton);
                w.WindowState = WindowState.Minimized;
                Assert.True(tray.Visible);

                _now = _now.AddMinutes(1);
                w.OnTick();
                Assert.True(w.IsVisible);
                Assert.Equal(WindowState.Normal, w.WindowState);
                Assert.False(w.IsActive);
                Assert.True(w.ShowActivated);
                Assert.True(w.ShowInTaskbar);
                Assert.False(w.IsInTray);
                Assert.False(tray.Visible);
            }
            finally
            {
                MainWindow.ShowNoActivate = previousShow;
            }
        });

        [Theory]
        [InlineData(false, "ErrAccessDenied")]
        [InlineData(true, "MsgMissed")]
        public void Tray_errors_and_missed_actions_restore_without_focus_and_flash(bool missed, string message) => Sta.Run(() =>
        {
            var previousShow = MainWindow.ShowNoActivate;
            var previousFlash = MainWindow.Flash;
            var flashes = new List<Window>();
            MainWindow.ShowNoActivate = MainWindow.ShowWithoutFocus;
            MainWindow.Flash = flashes.Add;
            try
            {
                var w = missed ? Make(new Settings { Hours = 0, Minutes = 2 }) :
                    Make(new Settings { PendingAction = "Shutdown", PendingDue = Due(30), PendingBoot = Marker });
                var tray = new FakeTray();
                w.CreateTrayIcon = tray.Create;
                w.ShowActivated = false;
                w.Show();
                w.ShowActivated = true;
                if (missed)
                {
                    w.ActSleep.IsEnabled = true;
                    w.ActSleep.IsChecked = true;
                    Click(w.StartButton);
                }
                w.WindowState = WindowState.Minimized;
                Assert.True(tray.Visible);

                if (missed)
                {
                    _now = _now.AddMinutes(10);
                    w.OnTick();
                    Assert.Equal(0, _suspendRunner.Calls);
                }
                else
                {
                    _runner.Result = _ => ShutdownRunner.ErrorAccessDenied;
                    ClickTrayItem(tray.CreateMenu(), "TrayAbort");
                }

                Assert.Equal(Loc.I[message], w.MessageText.Text);
                Assert.Same(w, Assert.Single(flashes));
                Assert.True(w.IsVisible);
                Assert.Equal(WindowState.Normal, w.WindowState);
                Assert.False(w.IsActive);
                Assert.False(w.IsInTray);
                Assert.False(tray.Visible);
            }
            finally
            {
                MainWindow.ShowNoActivate = previousShow;
                MainWindow.Flash = previousFlash;
            }
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
            Assert.Equal(new[] { "/a", "/s /f /t 3600 /c \"Scheduled with the Shutdown app.\"", "/a" }, _runner.Calls);
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
        public void Only_the_preset_that_matches_the_countdown_is_checked() => Sta.Run(() =>
        {
            var w = Make();
            w.DurHours.Value = 0;
            w.DurMinutes.Value = 30;
            foreach (ToggleButton chip in w.Chips.Children)
                Assert.Equal(System.Windows.Automation.AutomationProperties.GetAutomationId(chip) == "Chip30", chip.IsChecked);

            w.DurHours.Value = 1;
            foreach (ToggleButton chip in w.Chips.Children)
                Assert.False(chip.IsChecked);
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

        static void CommitMinutes(MainWindow w, string text)
        {
            var box = w.DurMinutes.Box;
            box.Text = text;
            box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null)
            {
                RoutedEvent = Keyboard.LostKeyboardFocusEvent
            });
        }

        [Theory]
        [InlineData("90", 1, 30, "30", true)]
        [InlineData("5999", 99, 59, "59", false)]
        public void Typed_minutes_carry_into_hours_on_commit(string text, int hours, int minutes, string display, bool today) => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = 0 });
            Assert.Equal(4, w.DurMinutes.Box.MaxLength);
            CommitMinutes(w, text);
            Assert.Equal(hours, w.DurHours.Value);
            Assert.Equal(minutes, w.DurMinutes.Value);
            Assert.Equal(display, w.DurMinutes.Box.Text);
            var end = _now.AddHours(hours).AddMinutes(minutes);
            Assert.Equal((today ? "Today" : Loc.I.DateText(end)) + " at " + Loc.I.TimeText(end), w.ResultText.Text);
            var saved = _store.Load();
            Assert.Equal(hours, saved.Hours);
            Assert.Equal(minutes, saved.Minutes);
        });

        [Fact]
        public void Countdown_summary_previews_the_full_uncommitted_minutes() => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = 0, Minutes = 0 });
            Assert.False(w.StartButton.IsEnabled);
            w.DurMinutes.Box.Text = "9";
            Assert.Equal("Today at " + Loc.I.TimeText(_now.AddMinutes(9)), w.ResultText.Text);
            w.DurMinutes.Box.Text = "90";
            Assert.Equal("Today at " + Loc.I.TimeText(_now.AddMinutes(90)), w.ResultText.Text);
            Assert.True(w.StartButton.IsEnabled);
            Assert.Equal("90", w.DurMinutes.Box.Text);
            Assert.Equal(0, w.DurHours.Value);
            Assert.Equal(9, w.DurMinutes.Value);
        });

        [Fact]
        public void Closing_commits_and_saves_minutes_without_a_focus_change() => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = 0, Minutes = 9 });
            w.DurMinutes.Box.Text = "90";
            w.Close();
            _windows.Remove(w);

            var saved = _store.Load();
            Assert.Equal(1, saved.Hours);
            Assert.Equal(30, saved.Minutes);
        });

        [Fact]
        public void Starting_commits_pending_minutes_without_a_focus_change() => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = 0, Minutes = 0 });
            w.DurMinutes.Box.Text = "90";
            Assert.True(w.StartButton.IsEnabled);
            Click(w.StartButton);

            Assert.Equal(1, w.DurHours.Value);
            Assert.Equal(30, w.DurMinutes.Value);
            Assert.Equal(new[] { "/a", "/s /f /t 5400 /c \"Scheduled with the Shutdown app.\"" }, _runner.Calls);
        });

        [Theory]
        [InlineData(0, 59, true, 1, 0)]
        [InlineData(1, 0, false, 0, 59)]
        [InlineData(0, 0, false, 0, 0)]
        public void Minute_arrows_carry_or_borrow_hours(int hours, int minutes, bool up, int expectedHours, int expectedMinutes) => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = hours, Minutes = minutes });
            Click(up ? w.DurMinutes.UpButton : w.DurMinutes.DownButton);
            Assert.Equal(expectedHours, w.DurHours.Value);
            Assert.Equal(expectedMinutes, w.DurMinutes.Value);
        });

        [Theory]
        [InlineData(0, 55, Key.PageUp, 1, 5)]
        [InlineData(0, 59, Key.PageUp, 1, 9)]
        [InlineData(1, 0, Key.PageDown, 0, 50)]
        [InlineData(0, 5, Key.PageDown, 0, 0)]
        public void Minute_page_keys_carry_or_borrow_the_full_step(int hours, int minutes, Key key, int expectedHours, int expectedMinutes) => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = hours, Minutes = minutes });
            using (var source = new HwndSource(new HwndSourceParameters()))
            {
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                w.DurMinutes.Box.RaiseEvent(e);
                Assert.True(e.Handled);
            }
            Assert.Equal(expectedHours, w.DurHours.Value);
            Assert.Equal(expectedMinutes, w.DurMinutes.Value);
        });

        [Theory]
        [InlineData(null)]
        [InlineData("90")]
        public void Minute_carry_past_the_hour_limit_keeps_the_duration_and_shows_info(string text) => Sta.Run(() =>
        {
            var w = Make(new Settings { Hours = 999, Minutes = 59 });
            string summary = w.ResultText.Text;
            if (text == null) Click(w.DurMinutes.UpButton);
            else
            {
                w.DurMinutes.Box.Text = text;
                Assert.False(w.StartButton.IsEnabled);
                Assert.Equal(Loc.I["ErrTooLong"], w.ResultText.Text);
                using (var source = new HwndSource(new HwndSourceParameters()))
                {
                    var enter = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    w.DurMinutes.Box.RaiseEvent(enter);
                    Assert.True(enter.Handled);
                }
            }

            Assert.Equal(999, w.DurHours.Value);
            Assert.Equal(59, w.DurMinutes.Value);
            Assert.Equal("59", w.DurMinutes.Box.Text);
            Assert.Equal(summary, w.ResultText.Text);
            Assert.True(w.StartButton.IsEnabled);
            Assert.Equal(Visibility.Visible, w.MessageCard.Visibility);
            Assert.Equal(Loc.I["ErrTooLong"], w.MessageText.Text);
            Assert.Equal(Visibility.Collapsed, w.CopyButton.Visibility);
            Assert.Empty(_runner.Calls);

            Click(w.DismissButton);
            w.OnTick();
            Assert.Equal(Visibility.Collapsed, w.MessageCard.Visibility);
            w.Close();
            _windows.Remove(w);
            var saved = _store.Load();
            Assert.Equal(999, saved.Hours);
            Assert.Equal(59, saved.Minutes);
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
