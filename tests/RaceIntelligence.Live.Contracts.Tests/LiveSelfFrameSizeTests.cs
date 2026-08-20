using MessagePack;
using RaceIntelligence.Live.Contracts.Mapping;
using RaceIntelligence.Live.Contracts.Publish;
using RaceIntelligence.Live.Contracts.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Live.Contracts.Tests;

/// <summary>
/// Guards the size of the one message that is sent sixty times a second.
/// </summary>
/// <remarks>
/// Every other message on this wire is low-rate enough that its size is a rounding error.
/// <see cref="LiveSelfFrame"/> is not: it is serialised once per publisher poll and fanned out to
/// every viewer following that car, so a field added here is multiplied by sixty and again by the
/// audience. This is the test that makes that cost visible at the moment it is incurred, rather
/// than a year later as a bandwidth bill nobody can attribute.
/// </remarks>
public sealed class LiveSelfFrameSizeTests
{
    /// <summary>
    /// A generous ceiling, not a target. The frame is well under this; the number exists so that
    /// adding a per-wheel group — which is what took it over 250 bytes before the tyre channels
    /// moved to their own rate class — fails here and has to be argued for rather than merged.
    /// </summary>
    private const int MaxReasonableBytes = 200;

    [Fact]
    public void The_frame_sent_sixty_times_a_second_stays_small()
    {
        var frame = LiveStandingsContractMapper.ToSelfFrame(LiveDtoFactory.FullyPopulatedSample(), "4242");

        byte[] bytes = MessagePackSerializer.Serialize<LivePublisherMessage>(
            frame, LiveMessagePackOptions.Default, TestContext.Current.CancellationToken);

        bytes.Length.ShouldBeLessThan(MaxReasonableBytes, $"actual: {bytes.Length} bytes");
    }
}
