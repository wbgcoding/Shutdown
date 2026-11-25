using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    [Collection("window")]
    [Trait("Layer", "Logic")]
    public class LogEdgeTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownTests-" + Guid.NewGuid().ToString("N"));
        readonly string _before = Log.Dir;

        public LogEdgeTests() { Directory.CreateDirectory(_dir); Log.Dir = Path.Combine(_dir, "logs"); }

        public void Dispose()
        {
            Log.Dir = _before;
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Write_creates_the_folder_and_a_timestamped_line()
        {
            Assert.False(Directory.Exists(Log.Dir));
            Log.Write("hello world");
            var lines = File.ReadAllLines(Log.FilePath);
            Assert.Single(lines);
            Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d  hello world$", lines[0]);
            Assert.Equal("shutdown.log", Path.GetFileName(Log.FilePath));
        }

        [Fact]
        public void Write_appends_in_order()
        {
            Log.Write("one");
            Log.Write("two");
            Log.Write("three");
            var lines = File.ReadAllLines(Log.FilePath);
            Assert.Equal(3, lines.Length);
            Assert.EndsWith("one", lines[0]);
            Assert.EndsWith("two", lines[1]);
            Assert.EndsWith("three", lines[2]);
        }

        [Fact]
        public void Write_with_an_exception_adds_its_type_message_and_stack()
        {
            Exception ex;
            try { throw new InvalidOperationException("boom detail"); }
            catch (Exception e) { ex = e; }
            Log.Write("it failed", ex);
            string text = File.ReadAllText(Log.FilePath);
            Assert.Contains("it failed", text);
            Assert.Contains("System.InvalidOperationException", text);
            Assert.Contains("boom detail", text);
            Assert.Contains("Write_with_an_exception_adds_its_type_message_and_stack", text);
            // The message line comes first, the exception on the following lines.
            Assert.True(text.IndexOf("it failed", StringComparison.Ordinal) < text.IndexOf("boom detail", StringComparison.Ordinal));
        }

        [Fact]
        public void Write_with_a_nested_exception_includes_the_inner_one()
        {
            Log.Write("outer", new RunnerException("C:\\x\\shutdown.exe", new System.ComponentModel.Win32Exception(2)));
            string text = File.ReadAllText(Log.FilePath);
            Assert.Contains("shutdown.exe", text);
            Assert.Contains("Win32Exception", text);
        }

        [Fact]
        public void Write_keeps_non_ascii_text()
        {
            Log.Write("pr\u00fcfung \u00e4\u00f6\u00fc");
            Assert.Contains("pr\u00fcfung \u00e4\u00f6\u00fc", File.ReadAllText(Log.FilePath, Encoding.UTF8));
        }

        [Fact]
        public void Write_into_an_unusable_folder_does_not_throw()
        {
            string blocker = Path.Combine(_dir, "blocker");
            File.WriteAllText(blocker, "x");
            // A file where the folder should be, and an illegal path.
            Log.Dir = Path.Combine(blocker, "sub");
            Log.Write("nobody will read this");
            Log.Write("with exception", new Exception("e"));
            Log.Dir = "bad|path?:*";
            Log.Write("still no throw");
            Assert.Equal("x", File.ReadAllText(blocker));
        }

        [Fact]
        public void Write_when_the_log_file_is_locked_does_not_throw()
        {
            Log.Write("first");
            using (new FileStream(Log.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Log.Write("second");
            Assert.Single(File.ReadAllLines(Log.FilePath));
        }

        [Fact]
        public void Dir_defaults_to_the_app_log_folder_when_unset()
        {
            Log.Dir = null;
            Assert.Equal(AppPaths.LogDir, Log.Dir);
            Assert.Equal(Path.Combine(AppPaths.LogDir, "shutdown.log"), Log.FilePath);
        }

        [Fact]
        public void Full_log_moves_to_dot_one_and_replaces_the_older_copy()
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(Log.FilePath + ".1", "OLD");
            File.WriteAllText(Log.FilePath, new string('x', 512 * 1024 + 1));
            Log.Write("fresh");
            Assert.Equal(512 * 1024 + 1, new FileInfo(Log.FilePath + ".1").Length);
            Assert.DoesNotContain("OLD", File.ReadAllText(Log.FilePath + ".1"));
            var lines = File.ReadAllLines(Log.FilePath);
            Assert.Single(lines);
            Assert.EndsWith("fresh", lines[0]);
        }

        [Fact]
        public void Log_of_exactly_the_limit_is_not_rotated_yet()
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(Log.FilePath, new string('x', 512 * 1024));
            Log.Write("more");
            Assert.False(File.Exists(Log.FilePath + ".1"));
            Assert.EndsWith("more" + Environment.NewLine, File.ReadAllText(Log.FilePath));
        }

        [Fact]
        public void Concurrent_writers_lose_no_lines()
        {
            var threads = Enumerable.Range(0, 8).Select(t => new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 25; i++) Log.Write("t" + t + "-" + i);
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            var lines = File.ReadAllLines(Log.FilePath);
            Assert.Equal(200, lines.Length);
            Assert.Equal(200, lines.Select(l => l.Substring(l.IndexOf("  ", StringComparison.Ordinal) + 2)).Distinct().Count());
        }
    }

    // Same collection as the window tests: both write the process-wide log folder.
    [Collection("window")]
    [Trait("Layer", "Logic")]
    public class LogTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "ShutdownTests-" + Guid.NewGuid().ToString("N"));
        readonly string _before = Log.Dir;

        public LogTests() { Directory.CreateDirectory(_dir); }

        public void Dispose()
        {
            Log.Dir = _before;
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Log_rotates_and_keeps_one_old_file()
        {
            Log.Dir = _dir;
            string line = new string('x', 1000);
            for (int i = 0; i < 1600; i++) Log.Write(line);
            Assert.True(File.Exists(Log.FilePath + ".1"));
            Assert.Equal(2, Directory.GetFiles(_dir, "shutdown.log*").Length);
            Assert.True(new FileInfo(Log.FilePath).Length <= 512 * 1024 + 2048);
            Assert.True(new FileInfo(Log.FilePath + ".1").Length <= 512 * 1024 + 2048);
        }
    }
}
