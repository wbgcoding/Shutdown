using System;
using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    public class BuildIdTests
    {
        static BuildId B(string v, long stamp) => new BuildId(new Version(v), stamp);

        [Theory]
        [InlineData("1.2.0.0", 5, "1.1.0.0", 9, true)]   // higher version wins over a later stamp
        [InlineData("1.1.0.0", 9, "1.2.0.0", 5, false)]
        [InlineData("1.2.0.0", 9, "1.2.0.0", 5, true)]   // same version: the later build wins
        [InlineData("1.2.0.0", 5, "1.2.0.0", 9, false)]
        [InlineData("1.2.0.0", 5, "1.2.0.0", 5, false)]  // the same build never replaces itself
        [InlineData("1.2.0.1", 0, "1.2.0.0", 0, true)]
        public void Newer_means_higher_version_then_later_stamp(string a, long sa, string b, long sb, bool newer) =>
            Assert.Equal(newer, B(a, sa).IsNewerThan(B(b, sb)));

        [Fact]
        public void A_null_version_counts_as_oldest() =>
            Assert.True(B("0.0.0.1", 0).IsNewerThan(new BuildId(null, 0)));

        [Fact]
        public void Published_build_reads_back_exactly()
        {
            string name = @"Local\BGCoding.Shutdown.Test." + Guid.NewGuid().ToString("N");
            var id = B("3.4.5.6", 638700000000000000);
            using (id.Publish(name, 4242))
            {
                var read = BuildId.ReadPublished(name, out int pid);
                Assert.True(read.HasValue);
                Assert.Equal(4242, pid);
                Assert.Equal(id.Version, read.Value.Version);
                Assert.Equal(id.StampTicks, read.Value.StampTicks);
                Assert.False(read.Value.IsNewerThan(id));
                Assert.False(id.IsNewerThan(read.Value));
            }
        }

        [Fact]
        public void Nothing_published_reads_as_null() =>
            Assert.Null(BuildId.ReadPublished(@"Local\BGCoding.Shutdown.Test." + Guid.NewGuid().ToString("N")));

        [Fact]
        public void A_map_in_another_layout_reads_as_null()
        {
            string name = @"Local\BGCoding.Shutdown.Test." + Guid.NewGuid().ToString("N");
            using (var map = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(name, 7 * sizeof(long)))
            {
                using (var v = map.CreateViewAccessor()) v.Write(0, 1L);
                Assert.Null(BuildId.ReadPublished(name, out int pid));
                Assert.Equal(0, pid);
            }
        }

        [Fact]
        public void Current_build_has_the_exe_write_time()
        {
            var id = BuildId.Current();
            Assert.NotNull(id.Version);
            Assert.True(id.StampTicks > 0);
        }
    }
}
