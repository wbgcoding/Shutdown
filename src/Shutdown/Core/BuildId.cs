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

        const int Size = 5 * sizeof(long);

        /// <summary>Publishes this build under a shared name; the returned map must stay open while the instance runs.</summary>
        public MemoryMappedFile Publish(string name)
        {
            var map = MemoryMappedFile.CreateOrOpen(name, Size);
            using (var v = map.CreateViewAccessor(0, Size))
            {
                v.Write(0, (long)Version.Major);
                v.Write(8, (long)Version.Minor);
                v.Write(16, (long)Math.Max(Version.Build, 0));
                v.Write(24, (long)Math.Max(Version.Revision, 0));
                v.Write(32, StampTicks);
            }
            return map;
        }

        /// <summary>Reads the build a running instance published; null when it published none (an older release).</summary>
        public static BuildId? ReadPublished(string name)
        {
            try
            {
                using (var map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read))
                using (var v = map.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read))
                {
                    var version = new Version((int)v.ReadInt64(0), (int)v.ReadInt64(8), (int)v.ReadInt64(16), (int)v.ReadInt64(24));
                    return new BuildId(version, v.ReadInt64(32));
                }
            }
            catch (Exception) { return null; }
        }
    }
}
