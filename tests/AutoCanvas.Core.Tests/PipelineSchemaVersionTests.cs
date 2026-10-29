using AutoCanvas.Core;

namespace AutoCanvas.Core.Tests;

public sealed class PipelineSchemaVersionTests
{
    [Fact]
    public void Oldest_and_current_versions_are_supported()
    {
        PipelineSchemaVersion.IsSupported(PipelineSchemaVersion.Oldest).ShouldBeTrue();
        PipelineSchemaVersion.IsSupported(PipelineSchemaVersion.Current).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PipelineSchemaVersion.Current + 1)]
    public void Versions_outside_the_range_are_not_supported(int version) =>
        PipelineSchemaVersion.IsSupported(version).ShouldBeFalse();
}
