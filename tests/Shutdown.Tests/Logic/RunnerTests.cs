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

        [Fact]
        public void Suspend_runner_is_blocked_in_tests()
        {
            Assert.True(SuspendRunner.BlockedForTests);
            ISuspendRunner runner = new SuspendRunner();
            Assert.Throws<InvalidOperationException>(runner.Run);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        public void Sleep_is_allowed_for_either_power_model(bool suspendAllowed, bool modernStandby, bool expected)
        {
            Assert.Equal(expected, SuspendRunner.IsSleepAllowed(() => suspendAllowed, () => modernStandby));
        }

        [Fact]
        public void Sleep_stays_enabled_if_the_power_capability_query_throws()
        {
            Assert.True(SuspendRunner.IsSleepAllowed(() => false, () => throw new EntryPointNotFoundException()));
        }

        [Theory]
        [InlineData(false, true, 1, 0)]
        [InlineData(false, false, 1, 1)]
        [InlineData(true, true, 0, 1)]
        public void Sleep_picks_the_power_path(bool modernStandby, bool canSuspend, int expectedSuspend, int expectedDisplayOff)
        {
            int suspend = 0;
            int displayOff = 0;
            SuspendRunner.Run(modernStandby, () => { suspend++; return canSuspend; }, () => displayOff++);
            Assert.Equal(expectedSuspend, suspend);
            Assert.Equal(expectedDisplayOff, displayOff);
        }
    }
}
