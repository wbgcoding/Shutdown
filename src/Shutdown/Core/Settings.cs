using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

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
        [DataMember(Order = 5)] public bool Force { get; set; } = true;
        [DataMember(Order = 6)] public int Hours { get; set; } = 1;
        [DataMember(Order = 7)] public int Minutes { get; set; } = 0;
        [DataMember(Order = 8)] public double? Left { get; set; }
        [DataMember(Order = 9)] public double? Top { get; set; }
        /// <summary>A schedule handed to Windows, so the countdown survives closing the app.</summary>
        [DataMember(Order = 10)] public string PendingAction { get; set; }
        [DataMember(Order = 11)] public string PendingTarget { get; set; }

        [OnDeserializing]
        void OnDeserializing(StreamingContext c)
        {
            Version = CurrentVersion; Theme = "system";
            Action = nameof(PowerAction.Shutdown); Mode = nameof(TimeMode.Countdown);
            Force = true; Hours = 1; Minutes = 0;
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
                    // Unknown future format: start fresh instead of guessing.
                    if (s == null || s.Version > Settings.CurrentVersion) return new Settings();
                    return s;
                }
            }
            catch (Exception ex)
            {
                Log.Write("settings load failed", ex);
                return new Settings();
            }
        }

        public void Save(Settings s)
        {
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
            }
            catch (Exception ex)
            {
                Log.Write("settings save failed", ex);
            }
        }
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
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + what +
                        (ex != null ? Environment.NewLine + ex : "") + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
