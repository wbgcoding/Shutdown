using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Shutdown.Core
{
    internal static class CommandInbox
    {
        internal enum PostResult { Unavailable, Busy, Posted }

        internal const int Capacity = 1024;
        const int HeaderSize = sizeof(int);
        const int InvalidLength = -1;
        static readonly byte[] EmptyPayload = new byte[Capacity - HeaderSize];

        internal static MemoryMappedFile Create(string name)
        {
            var map = MemoryMappedFile.CreateOrOpen(name, Capacity);
            try
            {
                using (var view = map.CreateViewAccessor(0, Capacity))
                    Clear(view);
                return map;
            }
            catch
            {
                map.Dispose();
                throw;
            }
        }

        // The sender holds the request mutex through the acknowledgement.
        internal static PostResult Post(string name, string[] args)
        {
            try
            {
                using (var map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite))
                using (var view = map.CreateViewAccessor(0, Capacity))
                {
                    if (view.ReadInt32(0) != 0) return PostResult.Busy;

                    // NUL cannot occur in a Windows argument, so it preserves argument boundaries.
                    string payload = string.Join("\0", args);
                    int bytes = Encoding.Unicode.GetByteCount(payload);
                    if (bytes == 0 || bytes > Capacity - HeaderSize)
                    {
                        view.Write(0, InvalidLength);
                        return PostResult.Posted;
                    }
                    var buffer = Encoding.Unicode.GetBytes(payload);
                    view.WriteArray(HeaderSize, buffer, 0, buffer.Length);
                    view.Write(0, buffer.Length);
                    return PostResult.Posted;
                }
            }
            catch (FileNotFoundException) { return PostResult.Unavailable; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Write("command could not be posted", ex);
                return PostResult.Unavailable;
            }
        }

        internal static Command Take(MemoryMappedFile map)
        {
            if (map == null) return null;
            using (var view = map.CreateViewAccessor(0, Capacity))
            {
                int bytes = view.ReadInt32(0);
                if (bytes == 0) return null;
                try
                {
                    if (bytes < 0 || bytes > Capacity - HeaderSize || bytes % 2 != 0) return CommandLine.Invalid;
                    var buffer = new byte[bytes];
                    view.ReadArray(HeaderSize, buffer, 0, buffer.Length);
                    return CommandLine.Parse(Encoding.Unicode.GetString(buffer).Split('\0'));
                }
                finally { Clear(view); }
            }
        }

        static void Clear(MemoryMappedViewAccessor view)
        {
            view.WriteArray(HeaderSize, EmptyPayload, 0, EmptyPayload.Length);
            view.Write(0, 0);
        }
    }
}
