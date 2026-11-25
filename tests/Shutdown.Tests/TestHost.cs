using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Shutdown.Core;
using Shutdown.Ui;

namespace Shutdown.Tests
{
    sealed class FakeRunner : IShutdownRunner
    {
        public readonly List<string> Calls = new List<string>();
        public Func<string, int> Result = a => 0;
        public int Run(string args) { Calls.Add(args); return Result(args); }
    }

    static class TestEnvironment
    {
        /// <summary>Runs before any test: nothing a test does may reach the real log or shut the PC down.</summary>
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Init()
        {
            ShutdownRunner.BlockedForTests = true;
            Log.Dir = Path.Combine(Path.GetTempPath(), "ShutdownTestLog");
        }
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
}

namespace System.Runtime.CompilerServices
{
    // Not part of .NET Framework; the compiler only needs the type to exist.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    sealed class ModuleInitializerAttribute : Attribute { }
}
