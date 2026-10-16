using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
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
            public DateTime Target;
            public bool AppTimed;
            public bool Force;
        }

        enum MessageKind { None, Info, Error, CloseGuard }

        // An app-timed run this late (PC was asleep) is skipped instead of firing on wake.
        static readonly TimeSpan MissedGrace = TimeSpan.FromMinutes(2);

        readonly SettingsStore _store;
        readonly Settings _settings;
        readonly IShutdownRunner _runner;
        readonly DispatcherTimer _tick;
        DateTime _date;
        Pending _pending;
        bool _loading = true;
        bool _allowClose;

        MessageKind _msgKind;
        string _msgKey, _msgDetail;
        bool _msgShowLog;

        [DllImport("powrprof.dll")]
        static extern bool IsPwrHibernateAllowed();

        readonly Func<DateTime> _clock;
        DateTime Now() => _clock();

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
                ActHibernate.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[HibernateUnavailable]") { Source = Loc.I });
            }

            var action = Enum.TryParse(settings.Action, out PowerAction a) ? a : PowerAction.Shutdown;
            if (action == PowerAction.Hibernate && !ActHibernate.IsEnabled) action = PowerAction.Shutdown;
            (action == PowerAction.Restart ? ActRestart : action == PowerAction.Hibernate ? ActHibernate : ActShutdown).IsChecked = true;
            (settings.Mode == nameof(TimeMode.At) ? ModeAt : ModeCountdown).IsChecked = true;
            ForceBox.IsChecked = settings.Force;
            DurHours.Value = settings.Hours;
            DurMinutes.Value = settings.Minutes;

            var start = Now().AddHours(1);
            start = start.AddMinutes((5 - start.Minute % 5) % 5);
            _date = start.Date;
            AtHour.Value = start.Hour;
            AtMinute.Value = start.Minute;

            foreach (var sp in new[] { DurHours, DurMinutes, AtHour, AtMinute }) sp.ValueChanged += (s, e) => OnInputChanged(s, null);

            if (Enum.TryParse(settings.PendingAction, out PowerAction pa) &&
                DateTime.TryParse(settings.PendingTarget, null, System.Globalization.DateTimeStyles.RoundtripKind, out var pt) &&
                pt > Now())
                _pending = new Pending { Action = pa, Target = pt, AppTimed = false };

            foreach (ComboBoxItem item in LangBox.Items)
                if ((string)item.Tag == settings.Language) LangBox.SelectedItem = item;
            if (LangBox.SelectedItem == null) LangBox.SelectedIndex = 0;

            if (settings.Left.HasValue && settings.Top.HasValue && OnScreen(settings.Left.Value, settings.Top.Value))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = settings.Left.Value;
                Top = settings.Top.Value;
            }

            Loc.I.Changed += (s, e) => RefreshTexts();
            Theme.Watch(() => { Theme.Apply(Application.Current.Resources); Theme.ApplyTitleBar(this); });
            SourceInitialized += (s, e) => Theme.ApplyTitleBar(this);

            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (s, e) => OnTick();
            _tick.Start();

            _loading = false;
            RefreshTexts();
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

        bool Force => ForceBox.IsChecked == true;

        ScheduleResult Compute(DateTime now) =>
            SelectedMode == TimeMode.At
                ? Schedule.FromTarget(_date, AtHour.Value, AtMinute.Value, now)
                : Schedule.FromCountdown(DurHours.Value, DurMinutes.Value, now);

        void RefreshTexts()
        {
            var L = Loc.I;
            DurHours.Caption = L["Hours"];
            DurMinutes.Caption = L["Minutes"];
            AtHour.Caption = L["Hour"];
            AtMinute.Caption = L["Minute"];
            VersionText.Text = L.F("VersionFmt", typeof(MainWindow).Assembly.GetName().Version.ToString(3)) + "  ·  © 2026 BGCoding";

            Chips.Children.Clear();
            foreach (var (h, m) in new[] { (0, 15), (0, 30), (1, 0), (2, 0), (4, 0) })
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

        void OnModeChanged(object sender, RoutedEventArgs e)
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
            bool appTimed = Schedule.IsAppTimed(action, Force);

            CountdownPanel.Visibility = SelectedMode == TimeMode.Countdown ? Visibility.Visible : Visibility.Collapsed;
            AtPanel.Visibility = SelectedMode == TimeMode.At ? Visibility.Visible : Visibility.Collapsed;

            DateLabel.Text = L.DateText(_date);
            PrevDay.IsEnabled = _date > now.Date;

            StartText.Text = L[action == PowerAction.Restart ? "StartRestart" : action == PowerAction.Hibernate ? "StartHibernate" : "StartShutdown"];
            StartIcon.Text = action == PowerAction.Restart ? "" : action == PowerAction.Hibernate ? "" : "";

            ForceHint.Visibility = Force ? Visibility.Visible : Visibility.Collapsed;
            AppTimedNote.Visibility = appTimed ? Visibility.Visible : Visibility.Collapsed;
            AppTimedText.Text = L[action == PowerAction.Hibernate ? "AppTimedHibernate" : "AppTimedForceOff"];

            var r = Compute(now);
            StartButton.IsEnabled = r.Ok;
            if (r.Ok)
            {
                SummaryCard.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                SummaryMain.SetResourceReference(TextBlock.ForegroundProperty, "AccentFg");
                SummaryIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentFg");
                SummaryIcon.Text = "";
                SummaryMain.Text = L.F("SummaryAt", L.DateText(r.Target), L.TimeText(r.Target));
                SummarySub.Text = L.F("SummaryIn", L.Span(r.Target - now));
                SummarySub.Visibility = Visibility.Visible;
                string cmd = "shutdown " + Schedule.ArgsFor(action, Force, r.Seconds);
                CommandText.Text = appTimed ? L.F("CommandAt", cmd, L.TimeText(r.Target)) : cmd;
            }
            else
            {
                SummaryCard.SetResourceReference(Border.BackgroundProperty, "DangerSoft");
                SummaryMain.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
                SummaryIcon.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
                SummaryIcon.Text = "";
                SummaryMain.Text = L[r.ErrorKey];
                SummarySub.Visibility = Visibility.Collapsed;
                CommandText.Text = "";
            }
            UpdatePending(now);
        }

        void UpdatePending(DateTime now)
        {
            var L = Loc.I;
            if (_pending == null)
            {
                PendingCard.Visibility = Visibility.Collapsed;
                return;
            }
            PendingCard.Visibility = Visibility.Visible;
            PendingTitle.Text = L[_pending.Action == PowerAction.Restart ? "PendingRestart" :
                                  _pending.Action == PowerAction.Hibernate ? "PendingHibernate" : "PendingShutdown"];
            PendingWhen.Text = L.F("SummaryAt", L.DateText(_pending.Target), L.TimeText(_pending.Target));
            PendingClock.Text = L.Clock(_pending.Target - now);
            System.Windows.Automation.AutomationProperties.SetName(PendingClock, L.F("PendingRemaining", L.Clock(_pending.Target - now)));
            PendingKeepOpen.Visibility = _pending.AppTimed ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void OnTick()
        {
            var now = Now();
            if (_pending != null && now >= _pending.Target)
            {
                var p = _pending;
                ClearPending();
                if (p.AppTimed)
                {
                    if (now - p.Target > MissedGrace) ShowInfo("MsgMissed");
                    else Execute(Schedule.ArgsFor(p.Action, p.Force, 0));
                }
            }
            UpdateUi();
        }

        void OnStart(object sender, RoutedEventArgs e)
        {
            var now = Now();
            var r = Compute(now);
            if (!r.Ok) { UpdateUi(); return; }
            var action = SelectedAction;
            bool appTimed = Schedule.IsAppTimed(action, Force);

            // Replace whatever is scheduled; "nothing pending" is the normal answer here.
            TryRun(Schedule.AbortArgs, out _);
            ClearPending();
            HideMessage();

            if (appTimed)
            {
                _pending = new Pending { Action = action, Target = r.Target, AppTimed = true, Force = Force };
            }
            else if (Execute(Schedule.ArgsFor(action, Force, r.Seconds)))
            {
                _pending = new Pending { Action = action, Target = r.Target, AppTimed = false, Force = Force };
                _settings.PendingAction = action.ToString();
                _settings.PendingTarget = r.Target.ToString("o");
            }
            Persist();
            UpdateUi();
        }

        void OnAbort(object sender, RoutedEventArgs e)
        {
            bool hadAppTimed = _pending?.AppTimed == true;
            HideMessage();
            if (!TryRun(Schedule.AbortArgs, out int code)) { ClearPending(); UpdateUi(); return; }
            ClearPending();
            if (code == 0 || hadAppTimed) ShowInfo("MsgAborted");
            else if (code == ShutdownRunner.ErrorNoShutdownInProgress) ShowInfo("MsgNothingPending");
            else ShowRunError(Schedule.AbortArgs, code);
            UpdateUi();
        }

        void ClearPending()
        {
            _pending = null;
            if (_settings.PendingAction != null || _settings.PendingTarget != null)
            {
                _settings.PendingAction = null;
                _settings.PendingTarget = null;
                Persist();
            }
        }

        /// <summary>Runs shutdown.exe; false and an error message when it fails.</summary>
        bool Execute(string args)
        {
            if (!TryRun(args, out int code)) return false;
            if (code == 0) return true;
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
            string key = code == ShutdownRunner.ErrorAccessDenied ? "ErrAccessDenied" : code == -1 ? "ErrTimeout" : "ErrRunFailed";
            ShowError(key, "shutdown " + args + ": exit code " + code, false);
        }

        public void ShowError(string key, string detail, bool showLog) => SetMessage(MessageKind.Error, key, detail, showLog);
        void ShowInfo(string key) => SetMessage(MessageKind.Info, key, null, false);
        void HideMessage() => SetMessage(MessageKind.None, null, null, false);

        void SetMessage(MessageKind kind, string key, string detail, bool showLog)
        {
            _msgKind = kind; _msgKey = key; _msgDetail = detail; _msgShowLog = showLog;
            RenderMessage();
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

        void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(LangBox.SelectedItem is ComboBoxItem item)) return;
            _settings.Language = (string)item.Tag;
            Loc.I.Apply(_settings.Language);
            if (!_loading) Persist();
        }

        void Persist()
        {
            if (_loading) return;
            _settings.Action = SelectedAction.ToString();
            _settings.Mode = SelectedMode.ToString();
            _settings.Force = Force;
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
