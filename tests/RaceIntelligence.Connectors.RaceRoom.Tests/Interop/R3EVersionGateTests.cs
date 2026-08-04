using System.Runtime.CompilerServices;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests.Interop;

/// <summary>
/// Verifies <see cref="R3EVersionGate.TryValidate"/>: this connector's struct layout is only
/// known to match major version 3 of RaceRoom's shared-memory API, minor 5 or later (newer minors
/// only append trailing fields, which this connector safely ignores).
/// </summary>
public class R3EVersionGateTests
{
    [Theory]
    [InlineData(2, 5, false)] // major too old -- layout not guaranteed to match.
    [InlineData(4, 5, false)] // major too new -- layout not guaranteed to match.
    [InlineData(3, 5, true)] // exactly the minimum supported version.
    [InlineData(3, 6, true)] // newer minor within the same major -- forward compatible.
    [InlineData(3, 4, false)] // minor older than the minimum this connector was written against.
    public void TryValidate_ChecksMajorAndMinorVersion(int major, int minor, bool expectedValid)
    {
        var raw = new R3ESharedRawBuilder().WithVersion(major, minor).Build();
        byte[] bytes = raw.ToBytes();

        using var view = new FakeSharedMemoryView(bytes);
        bool result = R3EVersionGate.TryValidate(view, out int actualMajor, out int actualMinor, out string? failureReason);

        result.ShouldBe(expectedValid);
        actualMajor.ShouldBe(major);
        actualMinor.ShouldBe(minor);
        if (expectedValid)
        {
            failureReason.ShouldBeNull();
        }
        else
        {
            failureReason.ShouldNotBeNull();
        }
    }

    [Fact]
    public void TryValidate_ViewSmallerThanStructSize_IsRefused()
    {
        var raw = new R3ESharedRawBuilder().WithVersion(3, 5).Build();
        byte[] fullBytes = raw.ToBytes();

        // A valid version header (>= 8 bytes) but the view is truncated one byte short of the full
        // struct size -- the header alone is not enough to trust the rest of the read.
        byte[] truncated = fullBytes[..^1];

        using var view = new FakeSharedMemoryView(truncated);
        bool result = R3EVersionGate.TryValidate(view, out int actualMajor, out int actualMinor, out string? failureReason);

        result.ShouldBeFalse();
        actualMajor.ShouldBe(3);
        actualMinor.ShouldBe(5);
        failureReason.ShouldNotBeNull();
        failureReason.ShouldContain(Unsafe.SizeOf<R3ESharedRaw>().ToString());
    }

    [Fact]
    public void TryValidate_ViewTooSmallForVersionHeader_IsRefused()
    {
        byte[] tooSmall = new byte[] { 1, 2, 3 }; // fewer than the 8 bytes needed for major+minor.

        using var view = new FakeSharedMemoryView(tooSmall);
        bool result = R3EVersionGate.TryValidate(view, out int actualMajor, out int actualMinor, out string? failureReason);

        result.ShouldBeFalse();
        actualMajor.ShouldBe(0);
        actualMinor.ShouldBe(0);
        failureReason.ShouldNotBeNull();
    }
}
