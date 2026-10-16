using System;
using System.Diagnostics.Eventing.Reader;

namespace Shutdown.Core
{
    /// <summary>Tells whether Windows has started anew since a schedule was handed to it.</summary>
    public static class BootInfo
    {
        const string Query = "*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot'] and EventID=27]]";
        const int ResumeFromHibernate = 2;

        /// <summary>
        /// Identifies the current Windows start, or null when the event log cannot be read. Unlike the
        /// uptime, it changes on a Fast Startup boot, and unlike a boot instant derived from the wall clock,
        /// it does not move when the clock is set. Resuming from hibernate keeps Windows' pending
        /// shutdown, so it keeps the marker too.
        /// </summary>
        public static string ReadMarker()
        {
            try
            {
                using (var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, Query) { ReverseDirection = true }))
                {
                    for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent())
                        using (e)
                        {
                            // Kernel-Boot 27 "The boot type was ...": 0 cold boot, 1 Fast Startup, 2 resume from hibernate.
                            if (e.Properties.Count > 0 && Convert.ToInt32(e.Properties[0].Value) == ResumeFromHibernate) continue;
                            return e.RecordId.HasValue ? "boot:" + e.RecordId.Value : null;
                        }
                }
            }
            catch (Exception ex)
            {
                Log.Write("boot marker not readable", ex);
            }
            return null;
        }
    }
}
