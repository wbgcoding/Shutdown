using System;
using System.Linq;
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

        Mutex _mutex;
        EventWaitHandle _activate;

        /// <summary>Set by the window tests: only the resources are wanted, no window, settings or instance lock.</summary>
        internal bool HostedByTests { get; set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (HostedByTests) { base.OnStartup(e); return; }

            DispatcherUnhandledException += OnDispatcherError;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Write("unhandled", a.ExceptionObject as Exception);

            bool newInstance = e.Args.Any(a => a == "--new-instance" || a == "--second-instance");
            if (!newInstance && !ClaimSingleInstance())
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
            w.Show();
        }

        bool ClaimSingleInstance()
        {
            _mutex = new Mutex(true, MutexName, out bool created);
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
            if (!created)
            {
                _activate.Set();
                return false;
            }
            var t = new Thread(() =>
            {
                while (_activate.WaitOne())
                    Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.BringToFront()));
            }) { IsBackground = true, Name = "activate" };
            t.Start();
            return true;
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
