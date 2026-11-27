using System.Windows;

namespace Shutdown.Ui
{
    internal static class Frozen
    {
        /// <summary>Freezes a shared brush, pen or geometry so WPF can skip change tracking, and returns it.</summary>
        internal static T Of<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
