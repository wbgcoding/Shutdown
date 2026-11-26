using System;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    [Trait("Layer", "Logic")]
    public class LogPrivacyTests
    {
        static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        [Fact]
        public void Redact_replaces_profile_at_the_start()
        {
            Assert.Equal(@"%USERPROFILE%\file.txt", Log.Redact(Profile + @"\file.txt"));
        }

        [Fact]
        public void Redact_replaces_profile_in_the_middle_regardless_of_case()
        {
            string profile = Profile;
            string differentCase = profile.ToUpperInvariant();
            if (differentCase == profile) differentCase = profile.ToLowerInvariant();

            Assert.Equal(@"Copy %USERPROFILE%\source.txt to %USERPROFILE%\target.txt failed.",
                Log.Redact("Copy " + profile + @"\source.txt to " + differentCase + @"\target.txt failed."));
        }

        [Theory]
        [InlineData("Access denied.")]
        [InlineData(null)]
        [InlineData("")]
        public void Redact_keeps_text_without_a_profile_path(string text)
        {
            Assert.Equal(text, Log.Redact(text));
        }
    }
}
