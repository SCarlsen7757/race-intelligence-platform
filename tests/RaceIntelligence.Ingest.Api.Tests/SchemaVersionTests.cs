using RaceIntelligence.Ingest.Contracts;
using Shouldly;

namespace RaceIntelligence.Ingest.Api.Tests;

/// <summary>Pure, no-Docker unit tests for <see cref="SchemaVersion.IsSupported"/>.</summary>
public sealed class SchemaVersionTests
{
    [Fact]
    public void Current_version_is_supported()
    {
        SchemaVersion.IsSupported(SchemaVersion.Current).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Any_other_version_is_rejected_outright_not_coerced(int version)
    {
        version.ShouldNotBe(SchemaVersion.Current);
        SchemaVersion.IsSupported(version).ShouldBeFalse();
    }
}
