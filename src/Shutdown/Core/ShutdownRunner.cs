using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Shutdown.Core
{
    public interface IShutdownRunner
    {
        /// <summary>Runs shutdown.exe with the given arguments and returns its exit code.</summary>
        int Run(string args);
    }

    public sealed class ShutdownRunner : IShutdownRunner
    {
        // Win32 codes shutdown.exe returns.
        public const int ErrorAccessDenied = 5;
        public const int ErrorNoShutdownInProgress = 1116;
        public const int ErrorShutdownIsScheduled = 1190;

        static string ExePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");

        /// <summary>Set by the test suite so no test can ever shut the machine down.</summary>
        public static bool BlockedForTests { get; set; }

        public int Run(string args)
        {
            if (BlockedForTests) throw new InvalidOperationException("shutdown.exe is blocked in tests: " + args);
            var psi = new ProcessStartInfo(ExePath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(15000))
                    {
                        // A hung shutdown.exe must not linger and act later.
                        try { p.Kill(); } catch (Exception) { }
                        return -1;
                    }
                    return p.ExitCode;
                }
            }
            catch (Win32Exception ex)
            {
                throw new RunnerException(ExePath, ex);
            }
        }
    }

    public sealed class RunnerException : Exception
    {
        public RunnerException(string path, Exception inner) : base(path + ": " + inner.Message, inner) { }
    }
}
