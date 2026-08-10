using System.Runtime.CompilerServices;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests.Interop;

/// <summary>
/// Verifies <see cref="R3EVersionGate.TryValidate"/>. The connector supports major version 3 only,
/// and within it decides on the block's own layout self-description rather than on the minor
/// version number — so any major-3 build whose layout matches is accepted whatever minor it
/// reports, and any build whose layout has moved is refused even if its minor looks new enough.
/// </summary>
public class R3EVersionGateTests
{
    [Theory]
    [InlineData(2, 5, false)] // major too old -- layout not guaranteed to match.
    [InlineData(4, 5, false)] // major too new -- layout not guaranteed to match.
    [InlineData(3, 5, true)] // the minor this connector's structs were transcribed from.
    [InlineData(3, 6, true)] // newer minor, same layout -- accepted.
    [InlineData(3, 9, true)] // much newer minor, same layout -- still accepted.
    [InlineData(3, 4, true)] // older minor, same layout -- accepted, where the old minor floor refused it.
    [InlineData(3, 0, true)] // oldest conceivable minor, same layout -- accepted.
    public void TryValidate_AcceptsAnyMajor3MinorWhoseLayoutMatches(int major, int minor, bool expectedValid)
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
    public void TryValidate_MovedDriverDataOffset_IsRefusedEvenOnANewerMinor()
    {
        // A future build that inserts a field ahead of num_cars pushes all_drivers_offset out. Every
        // field this connector reads would shift, so a plausible-looking minor must not save it.
        var raw = new R3ESharedRawBuilder()
            .WithVersion(3, 7)
            .WithLayoutSelfDescription(R3EVersionGate.ExpectedAllDriversOffset + 4, R3EVersionGate.ExpectedDriverDataSize)
            .Build();

        using var view = new FakeSharedMemoryView(raw.ToBytes());
        bool result = R3EVersionGate.TryValidate(view, out _, out _, out string? failureReason);

        result.ShouldBeFalse();
        failureReason.ShouldNotBeNull();
        failureReason.ShouldContain((R3EVersionGate.ExpectedAllDriversOffset + 4).ToString());
        failureReason.ShouldContain(R3EVersionGate.ExpectedAllDriversOffset.ToString());
    }

    [Fact]
    public void TryValidate_ChangedDriverDataSize_IsRefused()
    {
        // Growth inside the driver array leaves all_drivers_offset untouched, so driver_data_size is
        // the only signal that the layout moved at all.
        var raw = new R3ESharedRawBuilder()
            .WithVersion(3, 7)
            .WithLayoutSelfDescription(R3EVersionGate.ExpectedAllDriversOffset, R3EVersionGate.ExpectedDriverDataSize + 8)
            .Build();

        using var view = new FakeSharedMemoryView(raw.ToBytes());
        bool result = R3EVersionGate.TryValidate(view, out _, out _, out string? failureReason);

        result.ShouldBeFalse();
        failureReason.ShouldNotBeNull();
        failureReason.ShouldContain((R3EVersionGate.ExpectedDriverDataSize + 8).ToString());
    }

    [Theory]
    [InlineData(5, true)] // no self-description, but the minor clears the fallback floor.
    [InlineData(6, true)]
    [InlineData(4, false)] // no self-description and an older minor -- nothing left to trust.
    public void TryValidate_BlockWithoutSelfDescription_FallsBackToTheMinorFloor(int minor, bool expectedValid)
    {
        var raw = new R3ESharedRawBuilder()
            .WithVersion(3, minor)
            .WithLayoutSelfDescription(0, 0)
            .Build();

        using var view = new FakeSharedMemoryView(raw.ToBytes());
        bool result = R3EVersionGate.TryValidate(view, out _, out _, out string? failureReason);

        result.ShouldBe(expectedValid);
        if (expectedValid)
        {
            failureReason.ShouldBeNull();
        }
        else
        {
            failureReason.ShouldNotBeNull();
            failureReason.ShouldContain(R3EVersionGate.MinimumMinor.ToString());
        }
    }

    [Fact]
    public void ExpectedLayout_MatchesTheHandComputedOffsetsFromR3ECs()
    {
        // Pinned so the gate's notion of "matching layout" cannot drift with an accidental struct
        // edit: these are the same numbers R3ESharedRawLayoutTests derives from the published header.
        R3EVersionGate.ExpectedAllDriversOffset.ShouldBe(2_008);
        R3EVersionGate.ExpectedDriverDataSize.ShouldBe(328);
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
