using System;
using System.Windows.Shell;

namespace Shutdown.Ui
{
    internal static class TaskbarState
    {
        internal static (double ProgressValue, TaskbarItemProgressState ProgressState) For(TimeSpan? left, TimeSpan total)
        {
            if (!left.HasValue) return (0, TaskbarItemProgressState.None);

            double value = total > TimeSpan.Zero ? (double)left.Value.Ticks / total.Ticks : 0;
            return (Math.Max(0, Math.Min(1, value)), StateFor(left.Value));
        }

        internal static TaskbarItemProgressState StateFor(TimeSpan left) =>
            left <= TimeSpan.FromMinutes(1) ? TaskbarItemProgressState.Error :
            left <= TimeSpan.FromMinutes(5) ? TaskbarItemProgressState.Paused : TaskbarItemProgressState.Normal;
    }
}
