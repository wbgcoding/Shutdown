using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Shutdown.Core
{
    [DataContract]
    public sealed class Settings
    {
        public const int CurrentVersion = 1;

        [DataMember(Order = 0)] public int Version { get; set; } = CurrentVersion;
        /// <summary>system, light or dark. Not shown in the UI, editable in the file.</summary>
        [DataMember(Order = 2)] public string Theme { get; set; } = "system";
        [DataMember(Order = 3)] public string Action { get; set; } = nameof(PowerAction.Shutdown);
        [DataMember(Order = 4)] public string Mode { get; set; } = nameof(TimeMode.Countdown);
        [DataMember(Order = 6)] public int Hours { get; set; } = 1;
        [DataMember(Order = 7)] public int Minutes { get; set; } = 0;
        [DataMember(Order = 8)] public double? Left { get; set; }
        [DataMember(Order = 9)] public double? Top { get; set; }
        /// <summary>A schedule handed to Windows, so the countdown survives closing the app.</summary>
        [DataMember(Order = 10)] public string PendingAction { get; set; }
        /// <summary>Windows uptime in milliseconds at the target; unlike a clock time it survives setting the clock.</summary>
        [DataMember(Order = 11)] public long? PendingDue { get; set; }
        /// <summary>Marker of the Windows start the schedule was made in (BootInfo); a new start drops the schedule.</summary>
        [DataMember(Order = 12)] public string PendingBoot { get; set; }

        /// <summary>Forgets the stored schedule; true when there was one to forget.</summary>
        public bool ClearPending()
        {
            if (PendingAction == null && PendingDue == null && PendingBoot == null) return false;
            PendingAction = null;
            PendingDue = null;
            PendingBoot = null;
            return true;
        }

        [OnDeserializing]
        void OnDeserializing(StreamingContext c)
        {
            Version = CurrentVersion; Theme = "system";
            Action = nameof(PowerAction.Shutdown); Mode = nameof(TimeMode.Countdown);
            Hours = 1; Minutes = 0;
        }
    }

    public sealed class SettingsStore
    {
        public const string FileName = "Shutdown.settings.json";
        public string FilePath { get; }

        public SettingsStore(string filePath) { FilePath = filePath; }

        /// <summary>Always %APPDATA%\Shutdown, never beside the exe.</summary>
        public static SettingsStore Default() => new SettingsStore(Path.Combine(AppPaths.AppData, FileName));

        public Settings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new Settings();
                using (var fs = File.OpenRead(FilePath))
                {
                    var s = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs);
                    if (s == null) return new Settings();
                    // Unknown future format: start fresh instead of guessing, and keep the file for the newer version.
                    if (s.Version > Settings.CurrentVersion)
                    {
                        Log.Write("settings file is from a newer version, not saving over it");
                        KeepFile = true;
                        return new Settings();
                    }
                    return s;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Locked or not readable right now, not damaged: defaults for this run, but never saved over it.
                Log.Write("settings file not readable, not saving over it", ex);
                KeepFile = true;
                return new Settings();
            }
            catch (Exception ex)
            {
                Log.Write("settings load failed", ex);
                // The next save overwrites the file with defaults: keep the damaged one for the user to repair.
                try { if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bad", true); }
                catch (Exception copyEx) { Log.Write("settings backup failed", copyEx); }
                return new Settings();
            }
        }

        /// <summary>Set when the file on disk belongs to a newer version or could not be read; saving then leaves it alone.</summary>
        public bool KeepFile { get; private set; }

        /// <summary>False when nothing was written (file of a newer version, or a write error).</summary>
        public bool Save(Settings s)
        {
            if (KeepFile) return false;
            try
            {
                s.Version = Settings.CurrentVersion;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string tmp = FilePath + ".new";
                using (var ms = new MemoryStream())
                {
                    using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                        new DataContractJsonSerializer(typeof(Settings)).WriteObject(w, s);
                    File.WriteAllBytes(tmp, ms.ToArray());
                }
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                _saveFailing = false;
                return true;
            }
            catch (Exception ex)
            {
                // Every input saves; a folder that stays unwritable is logged once, not on every step.
                if (!_saveFailing) Log.Write("settings save failed", ex);
                _saveFailing = true;
                return false;
            }
        }

        bool _saveFailing;
    }

    public static class AppPaths
    {
        public static string AppData =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shutdown");

        public static string LogDir => Path.Combine(AppData, "logs");
    }

    public static class Log
    {
        const long MaxBytes = 512 * 1024;
        static readonly object Gate = new object();

        static string _dir;
        /// <summary>The log folder; tests point it elsewhere.</summary>
        internal static string Dir { get => _dir ?? AppPaths.LogDir; set => _dir = value; }

        public static string FilePath => Path.Combine(Dir, "shutdown.log");

        internal static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile)) return text;
            return Regex.Replace(text, Regex.Escape(profile), "%USERPROFILE%",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static void Write(string what, Exception ex = null)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    // Rotate: the full log moves to .1 (replacing the older one), so both stay under 1 MB.
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = FilePath + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +"  " + what +
                        (ex != null ? Environment.NewLine + Redact(ex.ToString()) : "") + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
