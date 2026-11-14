using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;

namespace Shutdown.Core
{
    /// <summary>
    /// Identifies a build of this app: the assembly version, then the exe's write time for copies that share a
    /// version (local builds). A start of a newer build replaces the running instance.
    /// </summary>
    public readonly struct BuildId
    {
        public readonly Version Version;
        public readonly long StampTicks;

        public BuildId(Version version, long stampTicks)
        {
            Version = version ?? new Version(0, 0);
            StampTicks = stampTicks;
        }

        /// <summary>The running exe's own build.</summary>
        public static BuildId Current()
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(BuildId).Assembly;
            long stamp = 0;
            try { stamp = File.GetLastWriteTimeUtc(asm.Location).Ticks; }
            catch (Exception) { }
            return new BuildId(asm.GetName().Version, stamp);
        }

        public bool IsNewerThan(BuildId other)
        {
            int c = Version.CompareTo(other.Version);
            return c != 0 ? c > 0 : StampTicks > other.StampTicks;
        }

        // Layout of the shared map: a layout id first, so a later change of the fields can be told apart, then the
        // version, the write time and the process id of the running instance.
        const long Layout = 0x5348_4454_0001; // "SHDT", layout 1
        const int Size = 7 * sizeof(long);

        /// <summary>Publishes this build under a shared name; the returned map must stay open while the instance runs.</summary>
        public MemoryMappedFile Publish(string name, int processId)
        {
            var map = MemoryMappedFile.CreateOrOpen(name, Size);
            try
            {
                using (var v = map.CreateViewAccessor(0, Size))
                {
                    v.Write(8, (long)Version.Major);
                    v.Write(16, (long)Version.Minor);
                    v.Write(24, (long)Math.Max(Version.Build, 0));
                    v.Write(32, (long)Math.Max(Version.Revision, 0));
                    v.Write(40, StampTicks);
                    v.Write(48, (long)processId);
                    v.Write(0, Layout);
                }
                return map;
            }
            catch
            {
                map.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Reads the build a running instance published, with its process id; null when it published none (an older
        /// release) or in another layout.
        /// </summary>
        public static BuildId? ReadPublished(string name, out int processId)
        {
            processId = 0;
            try
            {
                using (var map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read))
                using (var v = map.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read))
                {
                    if (v.ReadInt64(0) != Layout) return null;
                    var version = new Version((int)v.ReadInt64(8), (int)v.ReadInt64(16), (int)v.ReadInt64(24), (int)v.ReadInt64(32));
                    processId = (int)v.ReadInt64(48);
                    return new BuildId(version, v.ReadInt64(40));
                }
            }
            catch (Exception) { return null; }
        }

        public static BuildId? ReadPublished(string name) => ReadPublished(name, out _);
    }
}
