using System;
using System.ComponentModel;
using System.Linq;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    [Trait("Layer", "Logic")]
    public class RunnerEdgeTests
    {
        [Fact]
        public void Runner_exception_message_has_the_path_and_the_inner_message()
        {
            var inner = new Win32Exception(2);
            var ex = new RunnerException(@"C:\Windows\System32\shutdown.exe", inner);
            Assert.Equal(@"C:\Windows\System32\shutdown.exe: " + inner.Message, ex.Message);
            Assert.Same(inner, ex.InnerException);
            Assert.IsAssignableFrom<Exception>(ex);
        }

        [Fact]
        public void Runner_exception_with_an_empty_path_still_reads_cleanly()
        {
            var ex = new RunnerException("", new InvalidOperationException("nope"));
            Assert.Equal(": nope", ex.Message);
        }

        [Fact]
        public void Exit_codes_match_what_windows_returns()
        {
            Assert.Equal(5, ShutdownRunner.ErrorAccessDenied);
            Assert.Equal(1116, ShutdownRunner.ErrorNoShutdownInProgress);
            // The timeout marker must not collide with a real exit code the app treats specially.
            Assert.Equal(-1, ShutdownRunner.TimeoutCode);
            Assert.NotEqual(ShutdownRunner.ErrorAccessDenied, ShutdownRunner.TimeoutCode);
            Assert.NotEqual(ShutdownRunner.ErrorNoShutdownInProgress, ShutdownRunner.TimeoutCode);
        }

        [Fact]
        public void Blocked_runner_refuses_every_command_and_names_it()
        {
            Assert.True(ShutdownRunner.BlockedForTests);
            foreach (var args in new[] { "/s /f /t 60", "/r /f /t 60", "/h /f", "/a" })
            {
                var ex = Assert.Throws<InvalidOperationException>(() => new ShutdownRunner().Run(args));
                Assert.Contains(args, ex.Message);
            }
        }

        [Fact]
        public void Runner_is_usable_through_the_interface()
        {
            IShutdownRunner r = new ShutdownRunner();
            Assert.Throws<InvalidOperationException>(() => r.Run("/a"));
        }
    }
}
