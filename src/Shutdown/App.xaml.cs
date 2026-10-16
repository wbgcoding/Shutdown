using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Shutdown.Core;
using Shutdown.Ui;

namespace Shutdown
{
    public partial class App : Application
    {
        const string MutexName = @"Local\BGCoding.Shutdown.Instance";
        const string ActivateName = @"Local\BGCoding.Shutdown.Activate";

        const string AckName = @"Local\BGCoding.Shutdown.Activated";
        static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(400);
        static readonly TimeSpan RetryWait = TimeSpan.FromMilliseconds(200);
        static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(5);

        Mutex _mutex;
        EventWaitHandle _activate;
        EventWaitHandle _ack;
        readonly ManualResetEvent _stopListening = new ManualResetEvent(false);
        bool _owned;

        /// <summary>Set by the window tests: only the resources are wanted, no window, settings or instance lock.</summary>
        internal bool HostedByTests { get; set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (HostedByTests) { base.OnStartup(e); return; }

            DispatcherUnhandledException += OnDispatcherError;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Write("unhandled", a.ExceptionObject as Exception);

            if (!ClaimSingleInstance())
            {
                Shutdown();
                return;
            }

            var store = SettingsStore.Default();
            var settings = store.Load();
            Theme.Override = settings.Theme;
            Theme.Apply(Resources);
            Loc.I.Apply("system");

            base.OnStartup(e);
            var w = new MainWindow(store, settings, new ShutdownRunner());
            MainWindow = w;
            w.Closed += (s, a) => ReleaseInstance();
            w.Show();
        }

        // A closing instance can still own the lock for a moment after its window is gone, so a quick
        // restart would only signal a process that is about to exit. The running instance acknowledges
        // each activation; without an acknowledgement the new start keeps trying to take the lock.
        bool ClaimSingleInstance()
        {
            try
            {
                _mutex = new Mutex(false, MutexName);
                _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
                _ack = new EventWaitHandle(false, EventResetMode.AutoReset, AckName);
            }
            catch (UnauthorizedAccessException)
            {
                // The running copy was started as administrator; a normal start may not even signal it.
                Log.Write("another instance runs elevated");
                return false;
            }
            var deadline = DateTime.UtcNow + ClaimTimeout;
            while (!TryOwnMutex(TimeSpan.Zero))
            {
                _ack.Reset();
                _activate.Set();
                if (_ack.WaitOne(AckTimeout)) return false;
                if (TryOwnMutex(RetryWait)) break;
                if (DateTime.UtcNow > deadline)
                {
                    Log.Write("running instance did not answer");
                    return false;
                }
            }
            _activate.Reset();
            _owned = true;
            new Thread(Listen) { IsBackground = true, Name = "activate" }.Start();
            return true;
        }

        bool TryOwnMutex(TimeSpan wait)
        {
            try { return _mutex.WaitOne(wait); }
            catch (AbandonedMutexException) { return true; }
        }

        void Listen()
        {
            var handles = new WaitHandle[] { _activate, _stopListening };
            while (WaitHandle.WaitAny(handles) == 0)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_owned || !(MainWindow is MainWindow w) || !w.IsVisible) return;
                    w.BringToFront();
                    _ack.Set();
                }));
        }

        /// <summary>Frees the instance lock as soon as the window is gone, before the process has finished exiting.</summary>
        void ReleaseInstance()
        {
            if (!_owned) return;
            _owned = false;
            _stopListening.Set();
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ReleaseInstance();
            base.OnExit(e);
        }

        void OnDispatcherError(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Write("ui error", e.Exception);
            e.Handled = true;
            if (MainWindow is MainWindow w && w.IsLoaded)
            {
                w.ShowError("ErrUnexpected", e.Exception.Message, true);
                return;
            }
            // No window to report into: without one the process would linger invisibly.
            MessageBox.Show(Loc.I["ErrUnexpected"] + Environment.NewLine + Environment.NewLine + e.Exception.Message,
                "Shutdown", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
