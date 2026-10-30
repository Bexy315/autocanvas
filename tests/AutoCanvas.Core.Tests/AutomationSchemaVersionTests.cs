using AutoCanvas.Core;

namespace AutoCanvas.Core.Tests;

public sealed class AutomationSchemaVersionTests
{
    [Fact]
    public void Oldest_and_current_versions_are_supported()
    {
        AutomationSchemaVersion.IsSupported(AutomationSchemaVersion.Oldest).ShouldBeTrue();
        AutomationSchemaVersion.IsSupported(AutomationSchemaVersion.Current).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AutomationSchemaVersion.Current + 1)]
    public void Versions_outside_the_range_are_not_supported(int version) =>
        AutomationSchemaVersion.IsSupported(version).ShouldBeFalse();
}
