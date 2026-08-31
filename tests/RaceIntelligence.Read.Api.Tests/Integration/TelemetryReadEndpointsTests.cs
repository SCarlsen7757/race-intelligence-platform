using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RaceIntelligence.RaceRoom.Telemetry;
using RaceIntelligence.Read.Api.Contracts;
using RaceIntelligence.Read.Api.Endpoints;
using RaceIntelligence.Read.Api.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Read.Api.Tests.Integration;

/// <summary>End-to-end tests for reading stored telemetry back out of a real database.</summary>
[Collection(ReadAppCollection.Name)]
public sealed class TelemetryReadEndpointsTests(ReadAppFixture fixture)
{
    /// <summary>Reads the given query and returns the one lap it is expected to hold.</summary>
    /// <remarks>
    /// The response is keyed by lap even for a single lap — see <see cref="TelemetryResponse"/> —
    /// so most tests here want the one entry rather than the envelope.
    /// </remarks>
    private async Task<LapSamplesResponse> OneLapAsync(string query)
    {
        var response = await fixture.ReadClient.GetFromJsonAsync<TelemetryResponse>(query);

        response.ShouldNotBeNull();
        return response.Laps.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task a_lap_comes_back_with_every_sample_in_sequence_order()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 2, count: 25);

        var response = await fixture.ReadClient.GetFromJsonAsync<TelemetryResponse>(
            $"/api/v1/sessions/{id}/telemetry?lap=2");

        response.ShouldNotBeNull();
        response.SessionId.ShouldBe(id);

        var lap = response.Laps.ShouldHaveSingleItem();
        lap.LapNumber.ShouldBe(2);
        lap.Samples.Count.ShouldBe(25);
        lap.Samples.Select(s => s.SequenceNumber).ShouldBeInOrder();
    }

    [Fact]
    public async Task the_values_that_went_in_are_the_values_that_come_back()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 10);

        var lap = await OneLapAsync($"/api/v1/sessions/{id}/telemetry?lap=1");

        // The whole point of the round trip: MessagePack in through the collector's contract, a
        // binary COPY into Postgres, JSON out through this one, and the numbers survive.
        foreach (var sample in lap.Samples)
        {
            sample.Speed.ShouldBe(Seed.SpeedFor(sample.SequenceNumber), tolerance: 0.001f);
            sample.Throttle.ShouldNotBeNull().ShouldBe(Seed.ThrottleFor(sample.SequenceNumber), tolerance: 0.001f);
            sample.LapNumber.ShouldBe(1);
            sample.Gear.ShouldBe((short)4);
        }
    }

    /// <summary>
    /// The default response is the canonical fields and nothing else.
    /// </summary>
    /// <remarks>
    /// A sample is a hundred and seventy-five columns. Returning all of them by default would put
    /// about 650 bytes on the wire per sample, several thousand times a lap, for a chart that plots
    /// three — so the extra channels are asked for by name and absent otherwise.
    /// </remarks>
    [Fact]
    public async Task a_lap_carries_no_extra_channels_unless_they_are_asked_for()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 3);

        var lap = await OneLapAsync($"/api/v1/sessions/{id}/telemetry?lap=1");
        lap.Samples.ShouldAllBe(sample => sample.Channels == null);
    }

    [Fact]
    public async Task the_channels_asked_for_come_back_under_their_own_names()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 3);

        var lap = await OneLapAsync($"/api/v1/sessions/{id}/telemetry?lap=1&channels=tyreGripFl,camberFl");

        var channels = lap.Samples[0].Channels.ShouldNotBeNull();
        channels.Keys.ShouldBe(["tyreGripFl", "camberFl"], ignoreOrder: true);

        // Seeded values, round-tripped through a binary COPY and a projected read. Deserialised as
        // JsonElement because the map is `object?` on the contract: which channels are in it is the
        // caller's choice, so there is no member for a converter to bind to a type.
        ((JsonElement)channels["tyreGripFl"]!).GetSingle().ShouldBe(0.97f, tolerance: 0.001f);
        ((JsonElement)channels["camberFl"]!).GetSingle().ShouldBe(-0.06f, tolerance: 0.001f);
    }

    /// <summary>
    /// A group resolves to every channel in it, because that is how a widget asks: a suspension
    /// chart wants "suspension", not a list of names it would have to keep in step with the
    /// manifest.
    /// </summary>
    /// <remarks>
    /// Asserted on what came back rather than on the whole group, because most of the group is
    /// unreported in the seeded data and an unreported channel is omitted — the same rule the rest
    /// of this wire follows. What the group buys is that <c>camberFl</c> arrives without having been
    /// named, and that nothing outside the group does.
    /// </remarks>
    [Fact]
    public async Task a_group_name_resolves_to_the_channels_in_it_and_no_others()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 2);

        var lap = await OneLapAsync($"/api/v1/sessions/{id}/telemetry?lap=1&channels=suspension");

        var channels = lap.Samples[0].Channels.ShouldNotBeNull();

        channels.Keys.ShouldContain("camberFl");
        channels.Keys.ShouldNotContain("tyreGripFl");
        channels.Keys.ShouldBeSubsetOf(RaceRoomChannels.ByGroup["suspension"]);
    }

    /// <summary>
    /// A misspelling is refused by name. Silently returning fewer channels would draw a chart with a
    /// line missing and say nothing about why.
    /// </summary>
    [Fact]
    public async Task an_unknown_channel_is_a_400_naming_it()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();

        using var response = await fixture.ReadClient.GetAsync(
            $"/api/v1/sessions/{id}/telemetry?lap=1&channels=tyreGripFl,tyreGrpFl");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("tyreGrpFl");
    }

    [Fact]
    public async Task only_the_requested_lap_comes_back()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 5, startSequence: 0);
        await seed.TelemetryAsync(id, lapNumber: 2, count: 7, startSequence: 100);

        var lap = await OneLapAsync($"/api/v1/sessions/{id}/telemetry?lap=2");

        lap.Samples.Count.ShouldBe(7);
        lap.Samples.ShouldAllBe(s => s.LapNumber == 2);
    }

    [Fact]
    public async Task omitting_the_lap_is_refused_rather_than_defaulted()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var id = await new Seed(fixture).SessionAsync();

        var response = await fixture.ReadClient.GetAsync($"/api/v1/sessions/{id}/telemetry");

        // Defaulting to lap 1 would answer a question nobody asked, convincingly.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task a_lap_with_no_samples_is_a_404_rather_than_an_empty_chart()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 3);

        var response = await fixture.ReadClient.GetAsync($"/api/v1/sessions/{id}/telemetry?lap=99");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task telemetry_for_an_unknown_session_is_a_404()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var response = await fixture.ReadClient.GetAsync(
            $"/api/v1/sessions/{Guid.CreateVersion7()}/telemetry?lap=1");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The overlay this endpoint exists for: several laps, one round trip, each kept separate.
    /// </summary>
    [Fact]
    public async Task several_laps_come_back_keyed_and_in_ascending_order()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 5, startSequence: 0);
        await seed.TelemetryAsync(id, lapNumber: 2, count: 7, startSequence: 100);
        await seed.TelemetryAsync(id, lapNumber: 3, count: 4, startSequence: 200);

        var response = await fixture.ReadClient.GetFromJsonAsync<TelemetryResponse>(
            $"/api/v1/sessions/{id}/telemetry?lap=3&lap=1");

        response.ShouldNotBeNull();
        response.Laps.Select(l => l.LapNumber).ShouldBe([1, 3]);
        response.Laps[0].Samples.Count.ShouldBe(5);
        response.Laps[1].Samples.Count.ShouldBe(4);
        response.Laps.ShouldAllBe(l => l.Samples.All(s => s.LapNumber == l.LapNumber));
    }

    /// <summary>
    /// A hand-written URL spells the list with commas; one assembled in code repeats the parameter.
    /// Both mean the same thing, and duplicates collapse.
    /// </summary>
    [Fact]
    public async Task laps_can_be_comma_separated_and_repeats_collapse()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 3, startSequence: 0);
        await seed.TelemetryAsync(id, lapNumber: 2, count: 3, startSequence: 100);

        var response = await fixture.ReadClient.GetFromJsonAsync<TelemetryResponse>(
            $"/api/v1/sessions/{id}/telemetry?lap=2,1&lap=2");

        response.ShouldNotBeNull();
        response.Laps.Select(l => l.LapNumber).ShouldBe([1, 2]);
    }

    /// <summary>
    /// The channels stay attached to the right samples across a lap boundary — the one thing
    /// positional alignment between the two queries could get wrong, and the reason both order by
    /// <c>(lap_number, sequence_number)</c>.
    /// </summary>
    [Fact]
    public async Task channels_stay_aligned_with_their_samples_across_laps()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 4, startSequence: 0);
        await seed.TelemetryAsync(id, lapNumber: 2, count: 4, startSequence: 100);

        var response = await fixture.ReadClient.GetFromJsonAsync<TelemetryResponse>(
            $"/api/v1/sessions/{id}/telemetry?lap=1,2&channels=camberFl");

        response.ShouldNotBeNull();

        foreach (var lap in response.Laps)
        {
            foreach (var sample in lap.Samples)
            {
                var channels = sample.Channels.ShouldNotBeNull();
                ((JsonElement)channels["camberFl"]!).GetSingle().ShouldBe(-0.06f, tolerance: 0.001f);
            }
        }
    }

    /// <summary>
    /// Every missing lap is named at once. Fixing an overlay one round trip at a time is the
    /// experience this avoids.
    /// </summary>
    [Fact]
    public async Task a_404_names_every_lap_that_has_no_samples()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 3);

        using var response = await fixture.ReadClient.GetAsync(
            $"/api/v1/sessions/{id}/telemetry?lap=1,98,99");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("98");
        body.ShouldContain("99");
    }

    /// <summary>
    /// "Read the session by naming every lap" is the request this endpoint's whole design refuses,
    /// arriving by a different door.
    /// </summary>
    [Fact]
    public async Task naming_more_laps_than_the_ceiling_is_refused()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var id = await new Seed(fixture).SessionAsync();

        var laps = string.Join(",", Enumerable.Range(1, TelemetryReadEndpoints.MaxLapsPerRequest + 1));

        using var response = await fixture.ReadClient.GetAsync(
            $"/api/v1/sessions/{id}/telemetry?lap={laps}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task a_lap_that_is_not_a_number_is_refused_by_name()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var id = await new Seed(fixture).SessionAsync();

        using var response = await fixture.ReadClient.GetAsync(
            $"/api/v1/sessions/{id}/telemetry?lap=1,two");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("two");
    }

    [Fact]
    public async Task the_sampled_lap_list_names_only_laps_that_have_telemetry()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();

        // A lap row with no samples, and two laps with samples but no lap row. These lists diverge
        // in practice — a lap row is written when a lap completes, samples arrive throughout — and
        // a picker offering laps to chart wants the sampled one.
        await seed.LapAsync(id, 1);
        await seed.TelemetryAsync(id, lapNumber: 4, count: 3, startSequence: 0);
        await seed.TelemetryAsync(id, lapNumber: 2, count: 3, startSequence: 50);

        var laps = await fixture.ReadClient.GetFromJsonAsync<List<int>>(
            $"/api/v1/sessions/{id}/telemetry/laps");

        laps.ShouldNotBeNull();
        laps.ShouldBe([2, 4]);
    }
}
