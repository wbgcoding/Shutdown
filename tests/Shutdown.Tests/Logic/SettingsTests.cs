using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    /// <summary>Settings store edge cases. Load and Save log failures, so this shares the log folder collection.</summary>
    [Collection("window")]
    [Trait("Layer", "Logic")]
    public class SettingsEdgeTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownTests-" + Guid.NewGuid().ToString("N"));
        string FilePath => Path.Combine(_dir, SettingsStore.FileName);

        public SettingsEdgeTests() { Directory.CreateDirectory(_dir); }

        public void Dispose()
        {
            try
            {
                foreach (var f in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_dir, true);
            }
            catch { }
        }

        SettingsStore Store() => new SettingsStore(FilePath);

        [Fact]
        public void New_settings_have_the_documented_defaults()
        {
            var s = new Settings();
            Assert.Equal(Settings.CurrentVersion, s.Version);
            Assert.Equal("system", s.Theme);
            Assert.Equal("Shutdown", s.Action);
            Assert.Equal("Countdown", s.Mode);
            Assert.Equal(1, s.Hours);
            Assert.Equal(0, s.Minutes);
            Assert.Null(s.Left);
            Assert.Null(s.Top);
            Assert.Null(s.PendingAction);
            Assert.Null(s.PendingDue);
            Assert.Null(s.PendingBoot);
        }

        [Fact]
        public void Default_actions_and_modes_name_real_enum_values()
        {
            var s = new Settings();
            Assert.True(Enum.IsDefined(typeof(PowerAction), s.Action));
            Assert.True(Enum.IsDefined(typeof(TimeMode), s.Mode));
        }

        [Fact]
        public void Current_version_is_one()
        {
            // Raising it needs a migration for files already out there.
            Assert.Equal(1, Settings.CurrentVersion);
        }

        [Fact]
        public void Missing_file_gives_defaults_and_creates_nothing()
        {
            var store = Store();
            var s = store.Load();
            Assert.Equal(1, s.Hours);
            Assert.False(store.KeepFile);
            Assert.False(File.Exists(FilePath));
            Assert.Empty(Directory.GetFileSystemEntries(_dir));
        }

        [Fact]
        public void Save_writes_the_current_version_whatever_the_object_says()
        {
            var s = new Settings { Version = 0 };
            Assert.True(Store().Save(s));
            Assert.Equal(Settings.CurrentVersion, s.Version);
            Assert.Matches("\"Version\":\\s*1\\b", File.ReadAllText(FilePath));
            Assert.Equal(1, Store().Load().Version);
        }

        [Fact]
        public void Save_creates_missing_folders()
        {
            var path = Path.Combine(_dir, "a", "b", "c", SettingsStore.FileName);
            var store = new SettingsStore(path);
            Assert.True(store.Save(new Settings { Hours = 4 }));
            Assert.True(File.Exists(path));
            Assert.Equal(4, store.Load().Hours);
        }

        [Fact]
        public void Save_leaves_no_temp_file_on_create_or_overwrite()
        {
            var store = Store();
            Assert.True(store.Save(new Settings { Hours = 1 }));
            Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
            Assert.True(store.Save(new Settings { Hours = 2 }));
            Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
            Assert.Equal(2, store.Load().Hours);
        }

        [Fact]
        public void Overwrite_replaces_the_content_completely()
        {
            var store = Store();
            store.Save(new Settings { Hours = 12, Minutes = 30, Theme = "dark", PendingAction = "Restart", PendingDue = 5, PendingBoot = "boot:1" });
            store.Save(new Settings());
            var back = store.Load();
            Assert.Equal(1, back.Hours);
            Assert.Equal(0, back.Minutes);
            Assert.Equal("system", back.Theme);
            Assert.Null(back.PendingAction);
            Assert.Null(back.PendingDue);
            Assert.Null(back.PendingBoot);
        }

        [Fact]
        public void A_stale_temp_file_from_a_crashed_save_is_replaced()
        {
            File.WriteAllText(FilePath + ".new", "garbage");
            Assert.True(Store().Save(new Settings { Hours = 7 }));
            Assert.False(File.Exists(FilePath + ".new"));
            Assert.Equal(7, Store().Load().Hours);
        }

        [Fact]
        public void Saved_file_is_utf8_json_without_a_byte_order_mark()
        {
            Store().Save(new Settings { PendingBoot = "boot:\u00e4\u00f6" });
            var bytes = File.ReadAllBytes(FilePath);
            Assert.Equal((byte)'{', bytes[0]);
            var text = new UTF8Encoding(false).GetString(bytes);
            Assert.StartsWith("{", text);
            Assert.Equal("boot:\u00e4\u00f6", Store().Load().PendingBoot);

            File.WriteAllText(FilePath, "{\"Version\":1,\"Hours\":9}", new UTF8Encoding(false));
            Assert.Equal(9, Store().Load().Hours);
        }

        [Fact]
        public void Damaged_file_is_kept_aside_before_it_gets_overwritten()
        {
            const string damaged = "{\"Version\":1,\"Hours\":3,\"Theme\":\"dark\" \"Mode\":\"At\"}";
            File.WriteAllText(FilePath, damaged);
            var store = Store();
            Assert.Equal(1, store.Load().Hours);
            Assert.Equal(damaged, File.ReadAllText(FilePath + ".bad"));
            Assert.True(store.Save(new Settings()));
            Assert.Equal(damaged, File.ReadAllText(FilePath + ".bad"));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n")]
        [InlineData("[]")]
        [InlineData("42")]
        [InlineData("\"text\"")]
        [InlineData("{")]
        [InlineData("{\"Version\":1,")]
        [InlineData("{\"Hours\":\"many\"}")]
        [InlineData("not json at all")]
        public void Unusable_file_content_gives_defaults_and_keeps_saving_possible(string content)
        {
            File.WriteAllText(FilePath, content);
            var store = Store();
            var s = store.Load();
            Assert.Equal(1, s.Hours);
            Assert.Equal("system", s.Theme);
            Assert.Equal("Countdown", s.Mode);
            Assert.False(store.KeepFile);
            Assert.True(store.Save(s));
            Assert.Equal(1, Store().Load().Hours);
        }

        [Fact]
        public void Empty_object_gives_defaults_not_zero_values()
        {
            File.WriteAllText(FilePath, "{}");
            var s = Store().Load();
            Assert.Equal(1, s.Version);
            Assert.Equal(1, s.Hours);
            Assert.Equal(0, s.Minutes);
            Assert.Equal("system", s.Theme);
            Assert.Equal("Shutdown", s.Action);
            Assert.Equal("Countdown", s.Mode);
        }

        [Fact]
        public void Unknown_fields_are_ignored_and_known_ones_kept()
        {
            File.WriteAllText(FilePath,
                "{\"Version\":1,\"Hours\":3,\"Extra\":{\"a\":[1,2,3]},\"Minutes\":20,\"Another\":\"x\",\"Theme\":\"dark\"}");
            var store = Store();
            var s = store.Load();
            Assert.Equal(3, s.Hours);
            Assert.Equal(20, s.Minutes);
            Assert.Equal("dark", s.Theme);
            Assert.False(store.KeepFile);
        }

        [Fact]
        public void Optional_values_roundtrip_and_clear_again()
        {
            var store = Store();
            var s = new Settings
            {
                Left = -1234.5678, Top = 0.25,
                PendingAction = "Restart", PendingDue = 7200000, PendingBoot = "boot:7",
            };
            Assert.True(store.Save(s));
            var back = store.Load();
            Assert.Equal(-1234.5678, back.Left);
            Assert.Equal(0.25, back.Top);
            Assert.Equal("Restart", back.PendingAction);
            Assert.Equal(7200000L, back.PendingDue);
            Assert.Equal("boot:7", back.PendingBoot);

            back.Left = null; back.Top = null; back.PendingAction = null; back.PendingDue = null; back.PendingBoot = null;
            Assert.True(store.Save(back));
            var cleared = store.Load();
            Assert.Null(cleared.Left);
            Assert.Null(cleared.Top);
            Assert.Null(cleared.PendingAction);
            Assert.Null(cleared.PendingDue);
            Assert.Null(cleared.PendingBoot);
        }

        [Fact]
        public void Extreme_numbers_roundtrip()
        {
            var store = Store();
            store.Save(new Settings { Hours = int.MaxValue, Minutes = int.MinValue, PendingDue = long.MaxValue, Left = double.MaxValue });
            var back = store.Load();
            Assert.Equal(int.MaxValue, back.Hours);
            Assert.Equal(int.MinValue, back.Minutes);
            Assert.Equal(long.MaxValue, back.PendingDue);
            Assert.Equal(double.MaxValue, back.Left);
        }

        [Fact]
        public void Explicit_json_null_for_optional_fields_loads_as_null()
        {
            File.WriteAllText(FilePath, "{\"Left\":null,\"Top\":null,\"PendingAction\":null,\"PendingDue\":null,\"PendingBoot\":null}");
            var s = Store().Load();
            Assert.Null(s.Left);
            Assert.Null(s.PendingDue);
            Assert.Null(s.PendingAction);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(99)]
        [InlineData(int.MaxValue)]
        public void Newer_file_gives_defaults_sets_keep_file_and_is_never_written(int version)
        {
            string content = "{\"Version\":" + version + ",\"Theme\":\"dark\",\"Hours\":9,\"NewThing\":true}";
            File.WriteAllText(FilePath, content);
            var before = File.ReadAllBytes(FilePath);
            var store = Store();
            var s = store.Load();
            Assert.True(store.KeepFile);
            Assert.Equal("system", s.Theme);
            Assert.Equal(1, s.Hours);
            s.Hours = 5;
            Assert.False(store.Save(s));
            Assert.False(store.Save(s));
            Assert.Equal(before, File.ReadAllBytes(FilePath));
            Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(-5)]
        public void Current_or_older_file_does_not_set_keep_file(int version)
        {
            File.WriteAllText(FilePath, "{\"Version\":" + version + ",\"Hours\":6}");
            var store = Store();
            Assert.Equal(6, store.Load().Hours);
            Assert.False(store.KeepFile);
            Assert.True(store.Save(new Settings { Hours = 8 }));
            Assert.Equal(8, Store().Load().Hours);
        }

        [Fact]
        public void Fresh_store_starts_without_keep_file()
        {
            Assert.False(Store().KeepFile);
        }

        [Fact]
        public void Save_into_a_path_that_is_a_directory_returns_false_without_throwing()
        {
            Directory.CreateDirectory(FilePath);
            var store = Store();
            Assert.False(store.Save(new Settings()));
            Assert.True(Directory.Exists(FilePath));
            // Loading from it is no failure either.
            Assert.Equal(1, store.Load().Hours);
        }

        [Fact]
        public void A_save_that_keeps_failing_is_logged_once_until_one_succeeds()
        {
            string before = Log.Dir;
            Log.Dir = Path.Combine(_dir, "logs");
            try
            {
                int Logged() => File.Exists(Log.FilePath)
                    ? Regex.Matches(File.ReadAllText(Log.FilePath), "settings save failed").Count : 0;
                Directory.CreateDirectory(FilePath);
                var store = Store();
                for (int i = 0; i < 5; i++) Assert.False(store.Save(new Settings()));
                Assert.Equal(1, Logged());
                Directory.Delete(FilePath);
                Assert.True(store.Save(new Settings()));
                File.Delete(FilePath);
                Directory.CreateDirectory(FilePath);
                Assert.False(store.Save(new Settings()));
                Assert.Equal(2, Logged());
            }
            finally { Log.Dir = before; }
        }

        [Fact]
        public void Save_below_a_file_returns_false_without_throwing()
        {
            string blocker = Path.Combine(_dir, "blocker");
            File.WriteAllText(blocker, "x");
            var store = new SettingsStore(Path.Combine(blocker, "sub", SettingsStore.FileName));
            Assert.False(store.Save(new Settings()));
            Assert.Equal("x", File.ReadAllText(blocker));
        }

        [Theory]
        [InlineData("bad|name?.json")]
        [InlineData("bad\0name.json")]
        public void Save_to_an_invalid_file_name_returns_false_without_throwing(string name)
        {
            // Plain concatenation: Path.Combine itself rejects some of these characters.
            var store = new SettingsStore(_dir + Path.DirectorySeparatorChar + name);
            Assert.False(store.Save(new Settings()));
            Assert.Equal(1, store.Load().Hours);
        }

        [Fact]
        public void Save_of_a_read_only_file_returns_false_and_leaves_it_alone()
        {
            var store = Store();
            Assert.True(store.Save(new Settings { Hours = 2 }));
            File.SetAttributes(FilePath, FileAttributes.ReadOnly);
            Assert.False(store.Save(new Settings { Hours = 9 }));
            Assert.Equal(2, Store().Load().Hours);
        }

        [Fact]
        public void Load_of_a_locked_file_gives_defaults_without_throwing()
        {
            Store().Save(new Settings { Hours = 5 });
            using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var s = Store().Load();
                Assert.Equal(1, s.Hours);
            }
            Assert.Equal(5, Store().Load().Hours);
        }

        [Fact]
        public void A_file_that_was_locked_while_loading_is_never_saved_over()
        {
            Store().Save(new Settings { Hours = 5, PendingAction = "Shutdown", PendingDue = 1234 });
            var store = Store();
            using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                store.Load();
            // The file is intact, only unreadable for a moment: defaults must not replace it or its schedule.
            Assert.False(store.Save(new Settings { Hours = 9 }));
            var back = Store().Load();
            Assert.Equal(5, back.Hours);
            Assert.Equal("Shutdown", back.PendingAction);
            Assert.False(File.Exists(FilePath + ".bad"));
        }

        [Fact]
        public void Two_loads_give_independent_objects()
        {
            Store().Save(new Settings { Hours = 5 });
            var a = Store().Load();
            var b = Store().Load();
            a.Hours = 99;
            Assert.Equal(5, b.Hours);
        }

        [Fact]
        public void Default_store_lives_under_appdata_shutdown_and_never_beside_the_exe()
        {
            var store = SettingsStore.Default();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.Equal(Path.Combine(appData, "Shutdown", "Shutdown.settings.json"), store.FilePath);
            Assert.Equal("Shutdown.settings.json", SettingsStore.FileName);
            Assert.False(store.FilePath.StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void App_paths_are_below_appdata_shutdown()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.Equal(Path.Combine(appData, "Shutdown"), AppPaths.AppData);
            Assert.Equal(Path.Combine(appData, "Shutdown", "logs"), AppPaths.LogDir);
        }

        [Fact]
        public void File_path_is_kept_as_given()
        {
            Assert.Equal(FilePath, Store().FilePath);
        }
    }

    [Trait("Layer", "Logic")]
    public class SettingsTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownTests-" + Guid.NewGuid().ToString("N"));
        string File_ => Path.Combine(_dir, SettingsStore.FileName);

        public SettingsTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void Roundtrip_keeps_values()
        {
            var store = new SettingsStore(File_);
            var s = new Settings { Theme = "dark", Hours = 3, Minutes = 15, Action = "Restart",
                                   PendingAction = "Shutdown", PendingDue = 7200000, PendingBoot = "boot:7" };
            store.Save(s);
            store.Save(s);
            var back = store.Load();
            Assert.Equal("dark", back.Theme);
            Assert.Equal(3, back.Hours);
            Assert.Equal(15, back.Minutes);
            Assert.Equal("Restart", back.Action);
            Assert.Equal("Shutdown", back.PendingAction);
            Assert.Equal(7200000L, back.PendingDue);
            Assert.Equal("boot:7", back.PendingBoot);
            Assert.False(File.Exists(File_ + ".new"));
        }

        [Fact]
        public void Missing_fields_get_defaults()
        {
            File.WriteAllText(File_, "{\"Version\":1,\"Theme\":\"light\"}");
            var s = new SettingsStore(File_).Load();
            Assert.Equal(1, s.Hours);
            Assert.Equal("light", s.Theme);
            Assert.Equal("Countdown", s.Mode);
        }

        [Fact]
        public void File_from_1_0_with_the_force_switch_still_loads()
        {
            File.WriteAllText(File_, "{\"Version\":1,\"Force\":false,\"Hours\":2,\"Minutes\":45}");
            var s = new SettingsStore(File_).Load();
            Assert.Equal(2, s.Hours);
            Assert.Equal(45, s.Minutes);
        }

        [Fact]
        public void Corrupt_or_future_file_falls_back_to_defaults()
        {
            File.WriteAllText(File_, "{ not json");
            Assert.Equal("system", new SettingsStore(File_).Load().Theme);
            File.WriteAllText(File_, "{\"Version\":99,\"Theme\":\"dark\"}");
            Assert.Equal("system", new SettingsStore(File_).Load().Theme);
        }

        [Fact]
        public void Future_file_is_not_overwritten()
        {
            const string future = "{\"Version\":99,\"Theme\":\"dark\"}";
            File.WriteAllText(File_, future);
            var store = new SettingsStore(File_);
            var s = store.Load();
            s.Hours = 5;
            store.Save(s);
            Assert.Equal(future, File.ReadAllText(File_));
        }

        [Fact]
        public void Corrupt_file_is_replaced_on_save()
        {
            File.WriteAllText(File_, "{ not json");
            var store = new SettingsStore(File_);
            store.Save(store.Load());
            Assert.Contains("\"Version\"", File.ReadAllText(File_));
        }
    }
}
