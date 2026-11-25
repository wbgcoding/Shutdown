using Shutdown.Core;
using Xunit;

namespace Shutdown.Tests
{
    [Trait("Layer", "Integration")]
    public class BootInfoTests
    {
        [Fact]
        public void Marker_is_read_and_stable_within_one_boot()
        {
            string a = BootInfo.ReadMarker(), b = BootInfo.ReadMarker();
            Assert.StartsWith("boot:", a);
            Assert.Equal(a, b);
        }
    }
}
