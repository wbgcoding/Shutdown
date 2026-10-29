using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Reflection;
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
            public DateTime Target => TargetUtc.ToLocalTime();
            public bool AppTimed;
        }

        enum MessageKind { None, Info, Error, CloseGuard }

        // An app-timed run this late (PC was asleep) is skipped instead of firing on wake.
        static readonly TimeSpan MissedGrace = TimeSpan.FromMinutes(2);

        readonly SettingsStore _store;
        readonly Settings _settings;
        readonly IShutdownRunner _runner;
        readonly DispatcherTimer _tick;
        readonly Action _unwatchTheme;
        DateTime _date;
        Pending _pending;
        bool _loading = true;
        string _lastInputError;
        bool _allowClose;
        bool _keepingAwake;

        MessageKind _msgKind;
        string _msgKey, _msgDetail;
        bool _msgShowLog;

        // Returns a one-byte BOOLEAN; the default four-byte BOOL marshalling reads garbage.
        [DllImport("powrprof.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool IsPwrHibernateAllowed();

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
        {
            _clock = clock ?? (() => DateTime.Now);
            _store = store;
            _settings = settings;
            _runner = runner;
            InitializeComponent();

            if (!HibernateAllowed())
            {
                ActHibernate.IsEnabled = false;
                ToolTipService.SetShowOnDisabled(ActHibernate, true);
                ActHibernate.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[HibernateUnavailable]") { Source = Loc.I });
            }

            var action = Enum.TryParse(settings.Action, out PowerAction a) ? a : PowerAction.Shutdown;
            if (action == PowerAction.Hibernate && !ActHibernate.IsEnabled) action = PowerAction.Shutdown;
            (action == PowerAction.Restart ? ActRestart : action == PowerAction.Hibernate ? ActHibernate : ActShutdown).IsChecked = true;
            (settings.Mode == nameof(TimeMode.At) ? ModeAt : ModeCountdown).IsChecked = true;
            DurHours.Value = settings.Hours;
            DurMinutes.Value = settings.Minutes;

            var start = Now().AddHours(1);
            start = start.AddMinutes((5 - start.Minute % 5) % 5);
            _date = start.Date;
            AtHour.Value = start.Hour;
            AtMinute.Value = start.Minute;

            foreach (var sp in new[] { DurHours, DurMinutes, AtHour, AtMinute }) sp.ValueChanged += (s, e) => OnInputChanged(s, null);

            var restored = RestoredTargetUtc(settings);
            if (restored.HasValue && Enum.TryParse(settings.PendingAction, out PowerAction pa))
                _pending = new Pending { Action = pa, TargetUtc = restored.Value, Due = TimeSpan.FromMilliseconds(settings.PendingDue.Value), AppTimed = false };
            else
            {
                // Expired, unreadable or from before a restart (Windows dropped it): forget it so it never shows up again.
                settings.PendingAction = null;
                settings.PendingDue = null;
                settings.PendingBoot = null;
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
            SizeChanged += (s, e) => KeepOnScreen();

            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (s, e) => OnTick();
            _tick.Start();

            _loading = false;
            RefreshTexts();
        }

        TimeSpan DueOf(DateTime targetUtc) => Uptime() + (targetUtc - NowUtc());

        // The target is kept as uptime, so setting the clock neither hides the schedule nor shifts its time.
        DateTime? RestoredTargetUtc(Settings s)
        {
            if (!s.PendingDue.HasValue || !SameBoot(s.PendingBoot)) return null;
            double remainingMs = s.PendingDue.Value - Uptime().TotalMilliseconds;
            if (remainingMs <= 0 || remainingMs > Schedule.MaxSeconds * 1000d) return null;
            return NowUtc().AddMilliseconds(remainingMs);
        }

        static bool SameBoot(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return false;
            string current = BootMarker();
            // Unknown on either side: rather show a card that may be stale (Abort clears it) than hide a live schedule.
            if (stored == UnknownBoot || current == null) return true;
            return stored == current;
        }

        void OnLanguageChanged(object sender, EventArgs e) => RefreshTexts();

        // .NET caches the local zone; after a zone or clock change the shown times would use the old one.
        // Raised on a system thread.
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
            _pending = null;
            SyncKeepAwake();
            Loc.I.Changed -= OnLanguageChanged;
            _unwatchTheme();
            SystemEvents.TimeChanged -= OnTimeChanged;
            base.OnClosed(e);
        }

        static bool HibernateAllowed()
        {
            try { return IsPwrHibernateAllowed(); } catch { return true; }
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
            // WorkArea is the primary monitor; leave windows on other monitors alone.
            if (Left < area.Left || Left > area.Right) return;
            double overflow = Top + ActualHeight - area.Bottom;
            if (overflow > 0) Top = Math.Max(area.Top, Top - overflow);
        }

        public void BringToFront()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
        }

        PowerAction SelectedAction =>
            ActRestart.IsChecked == true ? PowerAction.Restart :
            ActHibernate.IsChecked == true ? PowerAction.Hibernate : PowerAction.Shutdown;

        TimeMode SelectedMode => ModeAt.IsChecked == true ? TimeMode.At : TimeMode.Countdown;

        ScheduleResult Compute(DateTime now) =>
            SelectedMode == TimeMode.At
                ? Schedule.FromTarget(_date, AtHour.Value, AtMinute.Value, now)
                : Schedule.FromCountdown(DurHours.Value, DurMinutes.Value, now);

        static readonly Assembly Asm = typeof(MainWindow).Assembly;

        void RefreshTexts()
        {
            var L = Loc.I;
            DurHours.Caption = L["Hours"];
            DurMinutes.Caption = L["Minutes"];
            AtHour.Caption = L["Hour"];
            AtMinute.Caption = L["Minute"];
            VersionText.Text = L.F("VersionFmt", Asm.GetName().Version.ToString(3)) + "  ·  " +
                               Asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

            Chips.Children.Clear();
            foreach (var (h, m) in new[] { (0, 15), (0, 30), (1, 0), (2, 0), (3, 0) })
            {
                var b = new Button
                {
                    Style = (Style)FindResource("ChipButton"),
                    Content = h > 0 ? L.F("ChipHours", h) : L.F("ChipMinutes", m),
                };
                System.Windows.Automation.AutomationProperties.SetAutomationId(b, "Chip" + (h * 60 + m));
                b.Click += (s, e) => { DurHours.Value = h; DurMinutes.Value = m; };
                Chips.Children.Add(b);
            }
            UpdateUi();
            RenderMessage();
        }

        void OnInputChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            UpdateUi();
            Persist();
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
            PrevDay.IsEnabled = _date > now.Date;

            StartText.Text = L[action == PowerAction.Restart ? "StartRestart" : action == PowerAction.Hibernate ? "StartHibernate" : "StartShutdown"];
            System.Windows.Automation.AutomationProperties.SetName(StartButton, StartText.Text);
            StartIcon.Text = action == PowerAction.Restart ? "" : action == PowerAction.Hibernate ? "" : "";

            AppTimedNote.Visibility = appTimed ? Visibility.Visible : Visibility.Collapsed;
            AppTimedText.Text = L["AppTimedHibernate"];
            Chips.Visibility = SelectedMode == TimeMode.Countdown ? Visibility.Visible : Visibility.Collapsed;

            var r = Compute(now);
            StartButton.IsEnabled = r.Ok;
            AnnounceInputError(r.Ok ? null : r.ErrorKey);
            if (r.Ok)
            {
                // Show what the inputs do not: the end time for a countdown, the time left for a date.
                ResultText.SetResourceReference(TextBlock.ForegroundProperty, "AccentFg");
                ResultText.Text = SelectedMode == TimeMode.Countdown
                    ? L.F("SummaryAt", L.DateText(r.Target), L.TimeText(r.Target))
                    : L.F("SummaryIn", L.Span(WholeMinutesUp(r.TargetUtc - now.ToUniversalTime())));
            }
            else
            {
                ResultText.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
                ResultText.Text = L[r.ErrorKey];
            }
            UpdatePending(now);
        }

        // Screen readers hear why Start is disabled, once per new reason rather than on every tick.
        void AnnounceInputError(string errorKey)
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(StartButton, errorKey == null ? "" : Loc.I[errorKey]);
            if (errorKey == _lastInputError) return;
            _lastInputError = errorKey;
            if (errorKey != null && !_loading && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.FromElement(ResultText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        // 59:59.6 left reads "in 1 h", not "in 59 min".
        static TimeSpan WholeMinutesUp(TimeSpan t) =>
            t >= TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(Math.Ceiling(t.TotalMinutes)) : t;

        void UpdatePending(DateTime now)
        {
            var L = Loc.I;
            // Only something this app scheduled can be cancelled here.
            AbortButton.IsEnabled = _pending != null;
            if (_pending == null)
            {
                PendingCard.Visibility = Visibility.Collapsed;
                return;
            }
            PendingCard.Visibility = Visibility.Visible;
            PendingTitle.Text = L[_pending.Action == PowerAction.Restart ? "PendingRestart" :
                                  _pending.Action == PowerAction.Hibernate ? "PendingHibernate" : "PendingShutdown"];
            PendingWhen.Text = L.F("SummaryAt", L.DateText(_pending.Target), L.TimeText(_pending.Target));
            var left = _pending.TargetUtc - now.ToUniversalTime();
            PendingClock.Text = L.Clock(left);
            System.Windows.Automation.AutomationProperties.SetName(PendingClock, L.F("PendingRemaining", L.Clock(left)));
            PendingKeepOpen.Visibility = _pending.AppTimed ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void OnTick()
        {
            var now = Now();
            // A schedule handed to Windows stays until aborted or replaced: Windows counts its own timer and may fire later than this clock.
            if (_pending?.AppTimed == true && now.ToUniversalTime() >= _pending.TargetUtc)
            {
                var p = _pending;
                ClearPending();
                if (now.ToUniversalTime() - p.TargetUtc > MissedGrace) ShowInfo("MsgMissed");
                else Execute(Schedule.ArgsFor(p.Action, 0));
            }
            UpdateUi();
        }

        void OnStart(object sender, RoutedEventArgs e)
        {
            var now = Now();
            var r = Compute(now);
            if (!r.Ok) { UpdateUi(); return; }
            var action = SelectedAction;
            bool appTimed = Schedule.IsAppTimed(action);

            HideMessage();
            // Replace whatever is scheduled; "nothing pending" is the normal answer here.
            // Any other answer means an old schedule may still run, so stop instead.
            if (!TryRun(Schedule.AbortArgs, out int abortCode)) return;
            if (abortCode != 0 && abortCode != ShutdownRunner.ErrorNoShutdownInProgress)
            {
                ShowRunError(Schedule.AbortArgs, abortCode);
                return;
            }
            ClearPending();

            // The abort call took time; the target must count from now, not from the click.
            r = Compute(Now());
            if (!r.Ok) { UpdateUi(); return; }

            if (appTimed)
            {
                _pending = new Pending { Action = action, TargetUtc = r.TargetUtc, Due = DueOf(r.TargetUtc), AppTimed = true };
                SyncKeepAwake();
            }
            else if (Execute(Schedule.ArgsFor(action, r.Seconds), true))
            {
                _pending = new Pending { Action = action, TargetUtc = r.TargetUtc, Due = DueOf(r.TargetUtc), AppTimed = false };
                _settings.PendingAction = action.ToString();
                _settings.PendingDue = (long)_pending.Due.TotalMilliseconds;
                _settings.PendingBoot = BootMarker() ?? UnknownBoot;
            }
            Persist();
            UpdateUi();
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
                if (code == 0 || code == ShutdownRunner.ErrorNoShutdownInProgress) ClearPending();
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

        void ClearPending()
        {
            _pending = null;
            SyncKeepAwake();
            if (_settings.PendingAction != null || _settings.PendingDue != null || _settings.PendingBoot != null)
            {
                _settings.PendingAction = null;
                _settings.PendingDue = null;
                _settings.PendingBoot = null;
                Persist();
            }
        }

        /// <summary>Runs shutdown.exe; false and an error message when it fails.</summary>
        bool Execute(string args, bool scheduling = false)
        {
            if (!TryRun(args, out int code)) return false;
            if (code == 0) return true;
            // A timed-out call may still have registered the schedule; take it back so none stays hidden.
            if (scheduling && code == ShutdownRunner.TimeoutCode) TryRun(Schedule.AbortArgs, out _);
            ShowRunError(args, code);
            return false;
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
            // Without a listener (no screen reader) there is nothing to raise, and no peer to create.
            if (kind != MessageKind.None && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.FromElement(MessageText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        void RenderMessage()
        {
            if (_msgKind == MessageKind.None) { MessageCard.Visibility = Visibility.Collapsed; return; }
            MessageCard.Visibility = Visibility.Visible;
            bool error = _msgKind == MessageKind.Error;
            MessageCard.SetResourceReference(Border.BackgroundProperty, error ? "DangerSoft" : "AccentSoft");
            MessageText.SetResourceReference(ForegroundProperty, error ? "Danger" : "AccentFg");
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

        void Persist()
        {
            if (_loading) return;
            _settings.Action = SelectedAction.ToString();
            _settings.Mode = SelectedMode.ToString();
            _settings.Hours = DurHours.Value;
            _settings.Minutes = DurMinutes.Value;
            if (WindowState == WindowState.Normal && IsLoaded) { _settings.Left = Left; _settings.Top = Top; }
            _store.Save(_settings);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_pending?.AppTimed == true && !_allowClose)
            {
                e.Cancel = true;
                SetMessage(MessageKind.CloseGuard, "CloseGuard", null, false);
                return;
            }
            Persist();
            base.OnClosing(e);
        }
    }
}
