using System;
using System.IO.MemoryMappedFiles;
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
        const string CommandName = @"Local\BGCoding.Shutdown.Command";
        const string RequestName = @"Local\BGCoding.Shutdown.Request";
        const int MailboxBusyExitCode = 2;
        const int CommandNotAcknowledgedExitCode = 3;
        static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(400);
        static readonly TimeSpan RetryWait = TimeSpan.FromMilliseconds(200);
        static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(5);
        static readonly TimeSpan CloseMargin = TimeSpan.FromSeconds(1.5);

        Mutex _mutex;
        EventWaitHandle _activate;
        EventWaitHandle _ack;
        EventWaitHandle _quit;
        MemoryMappedFile _info;
        MemoryMappedFile _commands;
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
            var trace = new StartTrace();

            DispatcherUnhandledException += OnDispatcherError;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Write("unhandled", a.ExceptionObject as Exception);
            // First, so every message from here on, even an early error, is in the user's language.
            Loc.I.Apply("system");
            trace.Mark("language");

            if (ClaimSingleInstance(e.Args) is int exitCode)
            {
                Shutdown(e.Args.Length == 0 ? 0 : exitCode);
                return;
            }

            trace.Mark("instance");
            RegisterJumpList(JumpListSetup.Register);
            var store = SettingsStore.Default();
            var settings = store.Load();
            trace.Mark("settings");
            Theme.Override = settings.Theme;
            Theme.Apply(Resources);
            trace.Mark("theme");

            base.OnStartup(e);
            var w = new MainWindow(store, settings, new ShutdownRunner());
            trace.Mark("window");
            MainWindow = w;
            w.Closed += (s, a) => ReleaseInstance();
            w.Loaded += OnLoaded;
            w.ContentRendered += (s, a) => trace.Done("first frame");
            w.Show();

            void OnLoaded(object sender, RoutedEventArgs args)
            {
                w.Loaded -= OnLoaded;
                trace.Mark("loaded");
                new Thread(Listen) { IsBackground = true, Name = "activate" }.Start();
                if (e.Args.Length != 0) w.ApplyCommand(CommandLine.Parse(e.Args));
            }
        }

        internal void RegisterJumpList(Action<Application> register)
        {
            try { register(this); }
            catch (Exception ex) { Log.Write("jump list not registered", ex); }
        }

        // A closing instance can still own the lock for a moment after its window is gone, so a quick
        // restart would only signal a process that is about to exit. The running instance acknowledges
        // each activation; without an acknowledgement the new start keeps trying to take the lock.
        // A null result means this instance owns the lock and should continue starting up.
        int? ClaimSingleInstance(string[] args)
        {
            try
            {
                _mutex = new Mutex(false, MutexName);
                _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
                _ack = new EventWaitHandle(false, EventResetMode.AutoReset, AckName);
                _quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);
                using (var request = new Mutex(false, RequestName))
                {
                    // Launchers share the inbox and the reply event.
                    if (!TryOwnMutex(request, ClaimTimeout))
                    {
                        Log.Write("instance request still in progress");
                        return MailboxBusyExitCode;
                    }
                    try { return ClaimOrNotify(args); }
                    finally { request.ReleaseMutex(); }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The running copy was started as administrator; a normal start may not even signal it.
                Log.Write("another instance runs elevated");
                MessageBox.Show(Loc.I["ElevatedRunning"], "Shutdown", MessageBoxButton.OK, MessageBoxImage.Information);
                return CommandNotAcknowledgedExitCode;
            }
        }

        int? ClaimOrNotify(string[] args)
        {
            var self = BuildId.Current();
            bool takeoverTried = false;
            var deadline = DateTime.UtcNow + ClaimTimeout;
            while (!TryOwnMutex(_mutex, TimeSpan.Zero))
            {
                var running = BuildId.ReadPublished(InfoName, out int runningPid);
                // Only the running instance gets the right; a release that published nothing gets it the old way.
                AllowSetForegroundWindow(runningPid > 0 ? runningPid : AsfwAny);
                if (!takeoverTried && running.HasValue && self.IsNewerThan(running.Value))
                {
                    takeoverTried = true;
                    var result = TakeOver();
                    if (result == Reply.Acquired) break;
                    if (result == Reply.Acknowledged && args.Length == 0) return 0;
                }

                _ack.Reset();
                var post = args.Length == 0 ? CommandInbox.PostResult.Unavailable : CommandInbox.Post(CommandName, args, _activate);
                if (post != CommandInbox.PostResult.Unavailable)
                {
                    var result = WaitForReply(_mutex, _ack, ClaimTimeout);
                    if (result == Reply.Acquired) break;
                    if (post == CommandInbox.PostResult.Busy)
                    {
                        Log.Write("command inbox busy, command not posted");
                        return MailboxBusyExitCode;
                    }
                    if (result == Reply.TimedOut)
                    {
                        CommandInbox.Withdraw(CommandName);
                        Log.Write("running instance did not acknowledge the command");
                        return CommandNotAcknowledgedExitCode;
                    }
                    return 0;
                }

                // An owner may still be creating its inbox. A build that published without one can still be activated.
                if (args.Length == 0 || running.HasValue || DateTime.UtcNow >= deadline)
                {
                    _activate.Set();
                    if (_ack.WaitOne(AckTimeout)) return CommandNotAcknowledgedExitCode;
                }
                if (TryOwnMutex(_mutex, RetryWait)) break;
                if (DateTime.UtcNow > deadline)
                {
                    Log.Write("running instance did not answer");
                    return CommandNotAcknowledgedExitCode;
                }
            }
            _activate.Reset();
            _quit.Reset();
            _owned = true;
            try { _commands = CommandInbox.Create(CommandName); }
            catch (Exception ex) { Log.Write("command inbox not created", ex); }
            try { _info = self.Publish(InfoName, System.Diagnostics.Process.GetCurrentProcess().Id); }
            catch (Exception ex) { Log.Write("build info not published", ex); }
            return null;
        }

        /// <summary>
        /// Asks an older running build to close and waits for its lock. It declines (and comes to the front
        /// instead) while a timed action needs it open. In that case this start only activates it.
        /// </summary>
        Reply TakeOver()
        {
            _ack.Reset();
            _quit.Set();
            var result = WaitForReply(_mutex, _ack, ClaimTimeout);
            if (result == Reply.Acquired) return result;
            _quit.Reset();
            Log.Write(result == Reply.Acknowledged ? "running instance declined to close" : "running instance did not close");
            return result;
        }

        internal static Reply WaitForReply(Mutex mutex, EventWaitHandle ack, TimeSpan wait)
        {
            int index;
            try
            {
                index = WaitHandle.WaitAny(new WaitHandle[] { mutex, ack }, wait);
            }
            catch (AbandonedMutexException ex) when (ex.MutexIndex == 0) { index = 0; }
            if (index == 1) return Reply.Acknowledged;
            if (index != 0) return Reply.TimedOut;

            // The owner can acknowledge and release before the sender wakes up.
            if (!ack.WaitOne(0)) return Reply.Acquired;
            mutex.ReleaseMutex();
            return Reply.Acknowledged;
        }

        internal enum Reply { Acquired, Acknowledged, TimedOut }

        static bool TryOwnMutex(Mutex mutex, TimeSpan wait)
        {
            try { return mutex.WaitOne(wait); }
            catch (AbandonedMutexException) { return true; }
        }

        void Listen()
        {
            var handles = new WaitHandle[] { _activate, _quit, _stopListening };
            int i;
            while ((i = WaitHandle.WaitAny(handles)) < 2)
            {
                bool quit = i == 1;
                var signalled = DateTime.UtcNow;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_owned || !(MainWindow is MainWindow w) || !w.IsLoaded) return;
                    // A newer build can take over once the countdown no longer needs this window.
                    // A request that waited about as long as the newer build waits (a busy window) is stale: that build
                    // gives up soon, so closing now could leave no window at all. The margin covers closing the window.
                    bool stale = DateTime.UtcNow - signalled > ClaimTimeout - CloseMargin;
                    if (quit && !stale && w.CloseForNewerBuild()) return;
                    var command = quit ? null : CommandInbox.Take(CommandName, _commands);
                    if (command != null)
                    {
                        // Applying the command can block longer than the sender waits for a reply.
                        _ack.Set();
                        w.ApplyCommand(command);
                    }
                    w.BringToFront();
                    if (command == null) _ack.Set();
                }));
            }
        }

        /// <summary>Frees the instance lock as soon as the window is gone, before the process has finished exiting.</summary>
        void ReleaseInstance()
        {
            if (!_owned) return;
            _owned = false;
            _stopListening.Set();
            _commands?.Dispose();
            _commands = null;
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
            string detail = Log.Redact(e.Exception.Message);
            if (MainWindow is MainWindow w && w.IsLoaded)
            {
                w.ShowError("ErrUnexpected", detail, true);
                return;
            }
            // No window to report into: without one the process would linger invisibly.
            // Free the lock first, so a new start is not refused while the dialog is open.
            ReleaseInstance();
            MessageBox.Show(Loc.I["ErrUnexpected"] + Environment.NewLine + Environment.NewLine + detail,
                "Shutdown", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
