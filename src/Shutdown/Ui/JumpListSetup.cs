using System.Windows;
using System.Windows.Shell;
using Shutdown.Core;

namespace Shutdown.Ui
{
    internal static class JumpListSetup
    {
        internal static void Register(Application app) =>
            JumpList.SetJumpList(app, Build(typeof(JumpListSetup).Assembly.Location));

        internal static JumpList Build(string exePath) => new JumpList
        {
            JumpItems =
            {
                Task(Loc.I["JumpIn30"], "--shutdown --in 30", exePath),
                Task(Loc.I["JumpIn60"], "--shutdown --in 60", exePath),
                Task(Loc.I["Abort"], "--abort", exePath)
            }
        };

        static JumpTask Task(string title, string arguments, string exePath) => new JumpTask
        {
            Title = title,
            Arguments = arguments,
            ApplicationPath = exePath,
            IconResourcePath = exePath,
            IconResourceIndex = 0
        };
    }
}
