using System;
using System.Threading;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    [Trait("Layer", "Logic")]
    public class CommandLineTests
    {
        static Command Parse(string line) => CommandLine.Parse(line.Split(' '));

        [Fact]
        public void No_arguments_leave_the_window_as_it_is()
        {
            var command = CommandLine.Parse(Array.Empty<string>());
            Assert.Null(command.Action);
            Assert.Null(command.CountdownMinutes);
            Assert.Null(command.At);
            Assert.False(command.Abort);
            Assert.Null(command.Error);
        }

        [Theory]
        [InlineData("--shutdown", PowerAction.Shutdown)]
        [InlineData("/RESTART", PowerAction.Restart)]
        [InlineData("--Hibernate", PowerAction.Hibernate)]
        [InlineData("/sLeEp", PowerAction.Sleep)]
        public void Action_switches_ignore_case(string line, PowerAction action)
        {
            var command = Parse(line);
            Assert.Equal(action, command.Action);
            Assert.Null(command.CountdownMinutes);
            Assert.Null(command.At);
            Assert.False(command.Abort);
            Assert.Null(command.Error);
        }

        [Theory]
        [InlineData("--in 1", 1)]
        [InlineData("--in 90", 90)]
        [InlineData("/IN 1:30", 90)]
        [InlineData("--in 24:05", 1445)]
        [InlineData("--in 5256000", 5256000)]
        [InlineData("--in 87600:00", 5256000)]
        public void Countdown_accepts_minutes_or_hours_and_minutes(string line, int minutes)
        {
            var command = Parse(line);
            Assert.Equal(minutes, command.CountdownMinutes);
            Assert.Null(command.Action);
            Assert.Null(command.At);
            Assert.Null(command.Error);
        }

        [Fact]
        public void Action_can_follow_the_time_switch()
        {
            var command = Parse("/at 07:45 --restart");
            Assert.Equal(PowerAction.Restart, command.Action);
            Assert.Equal(new TimeSpan(7, 45, 0), command.At);
            Assert.Null(command.CountdownMinutes);
            Assert.Null(command.Error);
        }

        [Theory]
        [InlineData("--at 00:00", 0, 0)]
        [InlineData("/AT 23:59", 23, 59)]
        public void At_accepts_a_time_without_an_action(string line, int hour, int minute)
        {
            var command = Parse(line);
            Assert.Equal(new TimeSpan(hour, minute, 0), command.At);
            Assert.Null(command.Action);
            Assert.Null(command.Error);
        }

        [Theory]
        [InlineData(11, 59, 59, 0)]
        [InlineData(12, 0, 0, 1)]
        [InlineData(12, 0, 1, 1)]
        [InlineData(23, 59, 59, 1)]
        public void At_rolls_to_tomorrow_once_the_time_passes(int hour, int minute, int second, int days)
        {
            var now = new DateTime(2026, 10, 16, hour, minute, second, DateTimeKind.Local);
            Assert.Equal(now.Date.AddDays(days), CommandLine.TargetDate(Parse("--at 12:00").At.Value, now));
        }

        [Theory]
        [InlineData("--abort")]
        [InlineData("/ABORT")]
        public void Abort_is_valid_on_its_own(string line)
        {
            var command = Parse(line);
            Assert.True(command.Abort);
            Assert.Null(command.Action);
            Assert.Null(command.CountdownMinutes);
            Assert.Null(command.At);
            Assert.Null(command.Error);
        }

        [Theory]
        [InlineData("--unknown")]
        [InlineData("-restart")]
        [InlineData("restart")]
        [InlineData("--shutdown /shutdown")]
        [InlineData("--shutdown --restart")]
        [InlineData("--restart --abort")]
        [InlineData("--abort --in 30")]
        [InlineData("--abort --abort")]
        [InlineData("--in")]
        [InlineData("--at")]
        [InlineData("--in --restart")]
        [InlineData("--in 30 --at 12:00")]
        [InlineData("--at 12:00 --in 30")]
        [InlineData("--in 1 --in 2")]
        [InlineData("--at 12:00 --at 13:00")]
        [InlineData("--in 0")]
        [InlineData("--in -1")]
        [InlineData("--in 1.5")]
        [InlineData("--in 1:3")]
        [InlineData("--in 1:60")]
        [InlineData("--in :30")]
        [InlineData("--in 0:00")]
        [InlineData("--in 5256001")]
        [InlineData("--in 2147483647:00")]
        [InlineData("--at 9:00")]
        [InlineData("--at 24:00")]
        [InlineData("--at 12:60")]
        [InlineData("--at 12:00:00")]
        [InlineData("--at 1200")]
        public void Invalid_arguments_return_usage_and_no_action(string line)
        {
            var command = Parse(line);
            Assert.Equal(CommandLine.Usage, command.Error);
            Assert.Null(command.Action);
            Assert.Null(command.CountdownMinutes);
            Assert.Null(command.At);
            Assert.False(command.Abort);
        }

        [Fact]
        public void Inbox_signals_even_when_busy_and_clears_the_command_after_reading()
        {
            string name = NewMapName();
            var args = new[] { "--restart", "/in", "1:30" };
            using var activate = new AutoResetEvent(false);
            Assert.Equal(CommandInbox.PostResult.Unavailable, CommandInbox.Post(name, args, activate));
            Assert.False(activate.WaitOne(0));
            using (var map = CommandInbox.Create(name))
            {
                Assert.Equal(CommandInbox.PostResult.Posted, CommandInbox.Post(name, args, activate));
                Assert.True(activate.WaitOne(0));
                Assert.Equal(CommandInbox.PostResult.Busy, CommandInbox.Post(name, new[] { "--abort" }, activate));
                Assert.True(activate.WaitOne(0));
                var command = CommandInbox.Take(name, map);
                Assert.Equal(PowerAction.Restart, command.Action);
                Assert.Equal(90, command.CountdownMinutes);
                Assert.Null(CommandInbox.Take(name, map));
                Assert.Equal(CommandInbox.PostResult.Posted, CommandInbox.Post(name, new[] { "--abort" }, activate));
                Assert.True(CommandInbox.Take(name, map).Abort);
            }
            Assert.Equal(CommandInbox.PostResult.Unavailable, CommandInbox.Post(name, args, activate));
        }

        [Fact]
        public void Withdrawn_command_cannot_be_replayed_on_the_next_activation()
        {
            string name = NewMapName();
            using var map = CommandInbox.Create(name);
            using var activate = new AutoResetEvent(false);
            Assert.Equal(CommandInbox.PostResult.Posted, CommandInbox.Post(name, new[] { "--abort" }, activate));

            CommandInbox.Withdraw(name);

            Assert.Null(CommandInbox.Take(name, map));
            Assert.Equal(CommandInbox.PostResult.Posted, CommandInbox.Post(name, new[] { "--restart" }, activate));
            Assert.Equal(PowerAction.Restart, CommandInbox.Take(name, map).Action);
        }

        [Fact]
        public void Inbox_limits_the_length_of_the_utf16_message()
        {
            string name = NewMapName();
            using (var map = CommandInbox.Create(name))
            {
                using var activate = new AutoResetEvent(false);
                string value = new string('0', 504) + "1";
                Assert.Equal(CommandInbox.PostResult.Posted, CommandInbox.Post(name, new[] { "--in", value }, activate));
                Assert.Equal(1, CommandInbox.Take(name, map).CountdownMinutes);
                Assert.Equal(CommandInbox.PostResult.Posted, CommandInbox.Post(name, new[] { "--in", value + "0" }, activate));
                Assert.Equal(CommandLine.Usage, CommandInbox.Take(name, map).Error);
                Assert.Null(CommandInbox.Take(name, map));
            }
        }

        static string NewMapName() => @"Local\BGCoding.Shutdown.Test." + Guid.NewGuid().ToString("N");
    }
}
