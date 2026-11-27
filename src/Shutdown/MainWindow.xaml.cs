using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using Shutdown.Core;
using Shutdown.Ui;

namespace Shutdown
{
    public partial class MainWindow : Window
    {
        sealed class Pending
        {
            public PowerAction Action;
            public DateTime TargetUtc;
            /// <summary>Uptime at the target; it puts TargetUtc right again after the clock is set.</summary>
            public TimeSpan Due;
            public TimeSpan Total;
            public DateTime Target => TargetUtc.ToLocalTime();
            public bool AppTimed;
            public bool WarnedLastMinute;
        }

        enum MessageKind { None, Info, Error, CloseGuard }

        // An app-timed run this late (PC was asleep) is skipped instead of firing on wake.
        static readonly TimeSpan MissedGrace = TimeSpan.FromMinutes(2);

        readonly SettingsStore _store;
        readonly Settings _settings;
        readonly IShutdownRunner _runner;
        readonly ISuspendRunner _suspendRunner;
        readonly DispatcherTimer _tick;
        readonly Action _unwatchTheme;
        DateTime _date;
        Pending _pending;
        bool _loading = true;
        string _lastInputError;
        bool _allowClose;
        // A Windows-timed schedule whose last save failed: only this window knows about it.
        bool _pendingUnsaved;
        bool _keepingAwake;

        MessageKind _msgKind;
        string _msgKey, _msgDetail;
        bool _msgShowLog;

        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        /// <summary>Time since Windows started; it keeps counting when the wall clock is set.</summary>
        internal static Func<TimeSpan> Uptime = () => TimeSpan.FromMilliseconds(GetTickCount64());

        /// <summary>Identifies the current Windows start; a pending schedule from an earlier start is gone.</summary>
        internal static Func<string> BootMarker = BootInfo.ReadMarker;

        // Stored when the event log could not be read while scheduling.
        const string UnknownBoot = "unknown";

        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint flags);

        const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001;

        /// <summary>Holds or releases the system-awake request; tests replace it so no power state changes.</summary>
        internal static Action<bool> KeepAwake = on => SetThreadExecutionState(on ? EsContinuous | EsSystemRequired : EsContinuous);

        readonly Func<DateTime> _clock;
        DateTime Now() => _clock();
        DateTime NowUtc() => _clock().ToUniversalTime();

        public MainWindow(SettingsStore store, Settings settings, IShutdownRunner runner, Func<DateTime> clock = null)
            : this(store, settings, runner, new SuspendRunner(), clock)
        {
        }

        internal MainWindow(SettingsStore store, Settings settings, IShutdownRunner runner, ISuspendRunner suspendRunner, Func<DateTime> clock)
        {
            _clock = clock ?? (() => DateTime.Now);
            _store = store;
            _settings = settings;
            _runner = runner;
            _suspendRunner = suspendRunner;
            InitializeComponent();

            InitializeAction(settings.Action);
            (settings.Mode == nameof(TimeMode.At) ? ModeAt : ModeCountdown).IsChecked = true;
            InitializeCountdown(settings);

            // One real hour ahead: wall-clock math lands in the gap or an hour off on a DST change day.
            var start = Now().ToUniversalTime().AddHours(1).ToLocalTime();
            start = start.AddMinutes((5 - start.Minute % 5) % 5);
            _date = start.Date;
            AtHour.Value = start.Hour;
            AtMinute.Value = start.Minute;

            foreach (var sp in new[] { AtHour, AtMinute }) sp.ValueChanged += (s, e) => OnInputChanged(s, null);

            _pending = RestorePending(settings);
            if (_pending == null)
            {
                // Expired, unreadable or from before a restart (Windows dropped it): forget it so it never shows up again.
                settings.ClearPending();
            }

            if (settings.Left.HasValue && settings.Top.HasValue && OnScreen(settings.Left.Value, settings.Top.Value))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = settings.Left.Value;
                Top = settings.Top.Value;
            }

            Loc.I.Changed += OnLanguageChanged;
            _unwatchTheme = Theme.Watch(() => { Theme.Apply(Application.Current.Resources); Theme.ApplyTitleBar(this); });
            SystemEvents.TimeChanged += OnTimeChanged;
            SourceInitialized += (s, e) => Theme.ApplyTitleBar(this);
            Activated += (s, e) => RefreshActionAvailability();
            SizeChanged += (s, e) => KeepOnScreen();
            // Without the system frame, Windows 10 draws no window edge (Windows 11 adds its own border and corners).
            if (Environment.OSVersion.Version.Build < 22000)
            {
                BorderThickness = new Thickness(1);
                SetResourceReference(BorderBrushProperty, "ControlBorder");
            }

            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (s, e) => OnTick();
            _tick.Start();

            _loading = false;
            RefreshTexts();
        }

        void OnLanguageChanged(object sender, EventArgs e) => RefreshTexts();

        // .NET caches the local zone; after a zone or clock change the shown times would use the old one.
        internal void OnTimeChanged(object sender, EventArgs e)
        {
            TimeZoneInfo.ClearCachedData();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Windows' timer and the app's countdown run on uptime: a clock change moves only the shown time.
                if (_pending != null) _pending.TargetUtc = NowUtc() + (_pending.Due - Uptime());
                UpdateUi();
            }));
        }

        // Loc and the system theme events are process-wide and would keep a closed window alive.
        protected override void OnClosed(EventArgs e)
        {
            _tick.Stop();
            _saveTimer?.Stop();
            DisposeTrayIcon();
            _pending = null;
            SyncKeepAwake();
            Loc.I.Changed -= OnLanguageChanged;
            _unwatchTheme();
            SystemEvents.TimeChanged -= OnTimeChanged;
            base.OnClosed(e);
        }

        static bool OnScreen(double left, double top) =>
            left >= SystemParameters.VirtualScreenLeft - 50 &&
            top >= SystemParameters.VirtualScreenTop - 10 &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

        // The window grows when a status or message card appears; keep its bottom inside the work area.
        void KeepOnScreen()
        {
            if (WindowState != WindowState.Normal || !IsLoaded) return;
            var area = SystemParameters.WorkArea;
            // WorkArea is the primary monitor; leave windows on other monitors alone (beside, above or below).
            if (Left < area.Left || Left > area.Right || Top < area.Top || Top >= area.Bottom) return;
            double overflow = Top + ActualHeight - area.Bottom;
            if (overflow > 0) Top = Math.Max(area.Top, Top - overflow);
        }

        public void BringToFront()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            HideTrayIcon();
            // Topmost for a moment lifts the window above others even where Windows refuses the focus.
            if (!Topmost)
            {
                Topmost = true;
                Topmost = false;
            }
            Activate();
        }

        void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

        void OnCloseButton(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// Closes for a newer build of the app that was just started; false (window stays) while a timed
        /// action needs this window open, with the reason shown.
        /// </summary>
        public bool CloseForNewerBuild()
        {
            if (_pending?.AppTimed == true)
            {
                // The newer copy has already exited, so offering "Close anyway" here would leave no window at all.
                ShowInfo("NewerBuildWaits");
                return false;
            }
            if (_pendingUnsaved)
            {
                // The newer copy restores schedules only from the settings file, so it would lose this one.
                ShowInfo("NewerBuildWaitsUnsaved");
                return false;
            }
            Close();
            return true;
        }

        TimeMode SelectedMode => ModeAt.IsChecked == true ? TimeMode.At : TimeMode.Countdown;

        ScheduleResult Compute(DateTime now) =>
            SelectedMode == TimeMode.At
                ? Schedule.FromTarget(_date, AtHour.Value, AtMinute.Value, now)
                : ComputeCountdown(now);

        void RefreshTexts()
        {
            var L = Loc.I;
            DurHours.Caption = L["Hours"];
            DurMinutes.Caption = L["Minutes"];
            AtHour.Caption = L["Hour"];
            AtMinute.Caption = L["Minute"];
            Extend15Button.Content = "+" + L.F("ChipMinutes", 15);
            Extend60Button.Content = "+" + L.F("ChipHours", 1);

            RefreshPresetChips();
            UpdateUi();
            RenderMessage();
        }

        void OnInputChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            UpdateUi();
            PersistSoon();
        }

        /// <summary>Input changes are saved after this pause, so a click's first animation frames never wait on the disk.</summary>
        internal static TimeSpan InputSaveDelay = TimeSpan.FromMilliseconds(400);
        DispatcherTimer _saveTimer;

        void PersistSoon()
        {
            if (InputSaveDelay <= TimeSpan.Zero) { Persist(); return; }
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = InputSaveDelay };
                _saveTimer.Tick += (s, e) => Persist();
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        void UpdateUi()
        {
            if (_loading) return;
            var L = Loc.I;
            var now = Now();
            var action = SelectedAction;
            bool appTimed = Schedule.IsAppTimed(action);

            CountdownPanel.Visibility = SelectedMode == TimeMode.Countdown ? Visibility.Visible : Visibility.Collapsed;
            AtPanel.Visibility = SelectedMode == TimeMode.At ? Visibility.Visible : Visibility.Collapsed;
            DateRow.Visibility = AtPanel.Visibility;

            DateLabel.Text = L.DateText(_date);
            bool prevDay = _date > now.Date;
            // A focused control that turns itself off drops the focus to the window and Tab starts over.
            if (!prevDay && PrevDay.IsKeyboardFocused) NextDay.Focus();
            PrevDay.IsEnabled = prevDay;

            var (startKey, actionKey, icon) = action switch
            {
                PowerAction.Restart => ("StartRestart", "ActRestart", "\uE72C"),
                PowerAction.Hibernate => ("StartHibernate", "ActHibernate", "\uE708"),
                PowerAction.Sleep => ("StartSleep", "ActSleep", SleepIcon.Text),
                _ => ("StartShutdown", "ActShutdown", "")
            };
            bool pending = _pending != null;
            StartText.Text = L[pending ? "StartReschedule" : startKey];
            System.Windows.Automation.AutomationProperties.SetName(StartButton,
                pending ? L.F("StartRescheduleFmt", L[actionKey]) : StartText.Text);
            bool power = action == PowerAction.Shutdown;
            StartPower.Visibility = power ? Visibility.Visible : Visibility.Collapsed;
            StartIcon.Visibility = power ? Visibility.Collapsed : Visibility.Visible;
            StartIcon.Text = icon;

            AppTimedNote.Visibility = appTimed ? Visibility.Visible : Visibility.Collapsed;
            SetAppTimedNote(appTimed ? L[action == PowerAction.Sleep ? "AppTimedSleep" : "AppTimedHibernate"] : null);
            Chips.Visibility = SelectedMode == TimeMode.Countdown ? Visibility.Visible : Visibility.Collapsed;
            UpdatePresetChips();

            var r = Compute(now);
            StartButton.IsEnabled = r.Ok;
            AnnounceInputError(r.Ok ? null : r.ErrorKey);
            SetBrush(ResultText, r.Ok ? "Text" : "Danger");
            // Show what the inputs do not: the end time for a countdown, the time left for a date.
            ResultText.Text = !r.Ok ? L[r.ErrorKey] : SelectedMode == TimeMode.Countdown
                ? L.F("SummaryAt", L.RelativeDateText(r.Target, now), L.TimeText(r.Target))
                : L.F("SummaryIn", L.Span(WholeMinutesUp(r.TargetUtc - now.ToUniversalTime())));
            UpdatePending(now);
            if (IsInTray) _trayIcon.Show(Title);
        }

        // Runs every second: a new resource reference only when the colour actually changes.
        static void SetBrush(TextBlock t, string key)
        {
            if (Equals(t.Tag, key)) return;
            t.Tag = key;
            t.SetResourceReference(TextBlock.ForegroundProperty, key);
        }

        // Screen readers hear why Start is disabled, once per new reason rather than on every tick.
        void AnnounceInputError(string errorKey)
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(StartButton, errorKey == null ? "" : Loc.I[errorKey]);
            if (errorKey == _lastInputError) return;
            _lastInputError = errorKey;
            if (errorKey != null && !_loading) AnnounceChange(ResultText);
        }

        // 59:59.6 left reads "in 1 h", not "in 59 min".
        static TimeSpan WholeMinutesUp(TimeSpan t) =>
            t >= TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(Math.Ceiling(t.TotalMinutes)) : t;

        internal void OnTick()
        {
            // A schedule handed to Windows stays until aborted or replaced: Windows counts its own timer and may fire later than this clock.
            // Uptime, not the wall clock: a clock change seen before its TimeChanged event must not fire early.
            if (_pending?.AppTimed == true && Uptime() >= _pending.Due)
            {
                var p = _pending;
                var late = Uptime() - p.Due;
                ClearPending();
                if (late > MissedGrace) ShowInfo("MsgMissed");
                else if (p.Action == PowerAction.Sleep)
                {
                    string error = _suspendRunner.Run();
                    if (error != null) ShowError(error, null, false);
                }
                else Execute(Schedule.ArgsFor(p.Action, 0, Loc.I["ShutdownComment"]));
            }
            UpdateUi();
            WarnLastMinute();
        }

        void OnStart(object sender, RoutedEventArgs e)
        {
            if (!Compute(Now()).Ok) { UpdateUi(); return; }
            if (SelectedMode == TimeMode.Countdown && !DurMinutes.Commit()) return;
            ReplaceSchedule(SelectedAction, () => Compute(Now()));
        }

        void OnAbort(object sender, RoutedEventArgs e)
        {
            HideMessage();
            // The app's own countdown needs no call: Start already cleared Windows' schedule.
            // A Windows schedule only counts as gone when /a says so.
            // Success needs no message: the pending card disappearing says it.
            if (_pending?.AppTimed == true)
            {
                ClearPending();
                UpdateUi();
                return;
            }
            if (TryRun(Schedule.AbortArgs, out int code))
            {
                if (AbortSucceeded(code)) ClearPending();
                else ShowRunError(Schedule.AbortArgs, code);
            }
            UpdateUi();
        }

        // Idle sleep would swallow an app-timed run, so the PC stays awake while one is pending.
        void SyncKeepAwake()
        {
            bool need = _pending?.AppTimed == true;
            if (need == _keepingAwake) return;
            _keepingAwake = need;
            KeepAwake(need);
        }

        void ClearPending(bool persist = true)
        {
            ResetLastMinuteWarning();
            _pending = null;
            SyncKeepAwake();
            // The close warning and the note for a newer copy are about the countdown that just ended.
            if (_msgKind == MessageKind.CloseGuard || _msgKey == "NewerBuildWaits" || _msgKey == "NewerBuildWaitsUnsaved") HideMessage();
            if (_settings.ClearPending() && persist) Persist();
        }

        /// <summary>Runs shutdown.exe; false and an error message when it fails.</summary>
        bool Execute(string args) => Execute(args, out _);

        /// <summary>maybeScheduled: the call timed out and taking it back failed too, so Windows may still run it.</summary>
        bool Execute(string args, out bool maybeScheduled)
        {
            maybeScheduled = false;
            if (!TryRun(args, out int code)) return false;
            if (code == 0) return true;
            // A timed-out call may still have registered the schedule; take it back so none stays hidden.
            if (code == ShutdownRunner.TimeoutCode)
                maybeScheduled = !TryRun(Schedule.AbortArgs, out int undo) || !AbortSucceeded(undo);
            ShowRunError(args, code);
            return false;
        }

        // Nothing left to abort counts as aborted.
        static bool AbortSucceeded(int code) => code == 0 || code == ShutdownRunner.ErrorNoShutdownInProgress;

        // Without a listener (no screen reader) there is nothing to raise and no peer to create. CreatePeerForElement,
        // not FromElement: the latter is null until a client asked for the peer, as for a card that was just collapsed.
        static void AnnounceChange(UIElement element)
        {
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        bool TryRun(string args, out int code)
        {
            code = 0;
            try
            {
                code = _runner.Run(args);
                Log.Write("shutdown " + args + " -> " + code);
                return true;
            }
            catch (RunnerException ex)
            {
                Log.Write("shutdown start failed", ex);
                ShowError("ErrStart", ex.Message, true);
                return false;
            }
        }

        void ShowRunError(string args, int code)
        {
            bool timeout = code == ShutdownRunner.TimeoutCode;
            string key = code == ShutdownRunner.ErrorAccessDenied ? "ErrAccessDenied" : timeout ? "ErrTimeout" : "ErrRunFailed";
            // The timeout marker is the app's own, not an exit code of shutdown.exe.
            ShowError(key, "shutdown " + args + (timeout ? "" : ": " + Loc.I.F("ExitCodeFmt", code)), false);
        }

        public void ShowError(string key, string detail, bool showLog) => SetMessage(MessageKind.Error, key, detail, showLog);
        void ShowInfo(string key) => SetMessage(MessageKind.Info, key, null, false);
        void HideMessage() => SetMessage(MessageKind.None, null, null, false);

        void SetMessage(MessageKind kind, string key, string detail, bool showLog)
        {
            _msgKind = kind; _msgKey = key; _msgDetail = detail; _msgShowLog = showLog;
            RenderMessage();
            if (IsInTray && (kind == MessageKind.Error || (kind == MessageKind.Info && key == "MsgMissed")))
            {
                RestoreWithoutFocus();
                Flash(this);
            }
            if (kind != MessageKind.None) AnnounceChange(MessageText);
        }

        void RenderMessage()
        {
            if (_msgKind == MessageKind.None) { MessageCard.Visibility = Visibility.Collapsed; return; }
            MessageCard.Visibility = Visibility.Visible;
            bool error = _msgKind == MessageKind.Error;
            MessageCard.SetResourceReference(Border.BackgroundProperty, error ? "DangerSoft" : "Control");
            MessageText.SetResourceReference(ForegroundProperty, error ? "Danger" : "Text");
            MessageText.Text = Loc.I[_msgKey];
            MessageDetail.Text = _msgDetail ?? "";
            MessageDetail.Visibility = string.IsNullOrEmpty(_msgDetail) ? Visibility.Collapsed : Visibility.Visible;
            CopyButton.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
            OpenLogButton.Visibility = error && _msgShowLog ? Visibility.Visible : Visibility.Collapsed;
            CloseAnywayButton.Visibility = _msgKind == MessageKind.CloseGuard ? Visibility.Visible : Visibility.Collapsed;
            DismissButton.Content = Loc.I[_msgKind == MessageKind.CloseGuard ? "KeepOpen" : "Dismiss"];
        }

        void OnCopyMessage(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(MessageText.Text + (string.IsNullOrEmpty(_msgDetail) ? "" : Environment.NewLine + _msgDetail)); }
            catch (Exception ex) { Log.Write("clipboard", ex); }
        }

        void OnOpenLog(object sender, RoutedEventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppPaths.LogDir);
                Process.Start(new ProcessStartInfo(AppPaths.LogDir) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("open log folder", ex); }
        }

        void OnDismissMessage(object sender, RoutedEventArgs e) => HideMessage();

        void OnCloseAnyway(object sender, RoutedEventArgs e)
        {
            _allowClose = true;
            Close();
        }

        void OnPrevDay(object sender, RoutedEventArgs e)
        {
            if (_date > Now().Date) _date = _date.AddDays(-1);
            UpdateUi();
        }

        void OnNextDay(object sender, RoutedEventArgs e) { _date = _date.AddDays(1); UpdateUi(); }
        void OnToday(object sender, RoutedEventArgs e) { _date = Now().Date; UpdateUi(); }
        void OnTomorrow(object sender, RoutedEventArgs e) { _date = Now().Date.AddDays(1); UpdateUi(); }

        bool Persist()
        {
            if (_loading) return true;
            _saveTimer?.Stop();
            _settings.Action = SelectedAction.ToString();
            _settings.Mode = SelectedMode.ToString();
            _settings.Hours = DurHours.Value;
            _settings.Minutes = DurMinutes.Value;
            if (WindowState == WindowState.Normal && IsLoaded) { _settings.Left = Left; _settings.Top = Top; }
            bool ok = _store.Save(_settings);
            _pendingUnsaved = !ok && _pending?.AppTimed == false;
            return ok;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_pending?.AppTimed == true && !_allowClose)
            {
                e.Cancel = true;
                SetMessage(MessageKind.CloseGuard, "CloseGuard", null, false);
                // Restore after the close attempt returns so the warning can be seen.
                if (IsInTray || WindowState == WindowState.Minimized) Dispatcher.BeginInvoke(new Action(BringToFront));
                return;
            }
            DurMinutes.Commit();
            Persist();
            base.OnClosing(e);
        }
    }
}
