using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Shutdown.Core;
using Shutdown.Ui;

// The portable exe often sits in Downloads: native DLLs load from System32 only, never from beside the exe.
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]

namespace Shutdown
{
    public partial class App : Application
    {
        const string MutexName = @"Local\BGCoding.Shutdown.Instance";
        const string ActivateName = @"Local\BGCoding.Shutdown.Activate";

        const string AckName = @"Local\BGCoding.Shutdown.Activated";
        const string QuitName = @"Local\BGCoding.Shutdown.Quit";
        const string InfoName = @"Local\BGCoding.Shutdown.Build";
        static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(400);
        static readonly TimeSpan RetryWait = TimeSpan.FromMilliseconds(200);
        static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(5);

        Mutex _mutex;
        EventWaitHandle _activate;
        EventWaitHandle _ack;
        EventWaitHandle _quit;
        System.IO.MemoryMappedFiles.MemoryMappedFile _info;
        readonly ManualResetEvent _stopListening = new ManualResetEvent(false);

        // Lets the running instance take the foreground: Windows only allows it when the process that the
        // user just started hands its right on, otherwise the window only flashes in the taskbar.
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);
        const int AsfwAny = -1;
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
                _quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);
            }
            catch (UnauthorizedAccessException)
            {
                // The running copy was started as administrator; a normal start may not even signal it.
                Log.Write("another instance runs elevated");
                return false;
            }
            var self = BuildId.Current();
            bool takeoverTried = false;
            var deadline = DateTime.UtcNow + ClaimTimeout;
            while (!TryOwnMutex(TimeSpan.Zero))
            {
                AllowSetForegroundWindow(AsfwAny);
                if (!takeoverTried && BuildId.ReadPublished(InfoName) is BuildId running && self.IsNewerThan(running))
                {
                    takeoverTried = true;
                    if (TakeOver()) break;
                    deadline = DateTime.UtcNow + ClaimTimeout;
                    continue;
                }
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
            _quit.Reset();
            _owned = true;
            try { _info = self.Publish(InfoName); }
            catch (Exception ex) { Log.Write("build info not published", ex); }
            new Thread(Listen) { IsBackground = true, Name = "activate" }.Start();
            return true;
        }

        /// <summary>
        /// Asks an older running build to close and waits for its lock. It declines (and comes to the front
        /// instead) while it must stay open for a timed hibernate; then this start only activates it.
        /// </summary>
        bool TakeOver()
        {
            _ack.Reset();
            _quit.Set();
            try
            {
                int i = WaitHandle.WaitAny(new WaitHandle[] { _mutex, _ack }, ClaimTimeout);
                if (i == 0) return true;
                if (i == 1) Log.Write("running instance declined to close");
                else Log.Write("running instance did not close");
                _quit.Reset();
                return false;
            }
            catch (AbandonedMutexException ex) when (ex.MutexIndex == 0) { return true; }
        }

        bool TryOwnMutex(TimeSpan wait)
        {
            try { return _mutex.WaitOne(wait); }
            catch (AbandonedMutexException) { return true; }
        }

        void Listen()
        {
            var handles = new WaitHandle[] { _activate, _quit, _stopListening };
            int i;
            while ((i = WaitHandle.WaitAny(handles)) < 2)
            {
                bool quit = i == 1;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_owned || !(MainWindow is MainWindow w) || !w.IsVisible) return;
                    // A newer build was started: close so it can take over, unless a timed hibernate needs this window.
                    if (quit && w.CloseForNewerBuild()) return;
                    w.BringToFront();
                    _ack.Set();
                }));
            }
        }

        /// <summary>Frees the instance lock as soon as the window is gone, before the process has finished exiting.</summary>
        void ReleaseInstance()
        {
            if (!_owned) return;
            _owned = false;
            _stopListening.Set();
            _info?.Dispose();
            _info = null;
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
            // Free the lock first, so a new start is not refused while the dialog is open.
            ReleaseInstance();
            MessageBox.Show(Loc.I["ErrUnexpected"] + Environment.NewLine + Environment.NewLine + e.Exception.Message,
                "Shutdown", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
