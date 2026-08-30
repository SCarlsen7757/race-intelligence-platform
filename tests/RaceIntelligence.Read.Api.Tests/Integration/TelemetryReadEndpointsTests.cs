using System.Net;
using System.Net.Http.Json;
using RaceIntelligence.Read.Api.Contracts;
using RaceIntelligence.Read.Api.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Read.Api.Tests.Integration;

/// <summary>End-to-end tests for reading a stored lap's telemetry back out of a real database.</summary>
[Collection(ReadAppCollection.Name)]
public sealed class TelemetryReadEndpointsTests(ReadAppFixture fixture)
{
    [Fact]
    public async Task a_lap_comes_back_with_every_sample_in_sequence_order()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 2, count: 25);

        var lap = await fixture.ReadClient.GetFromJsonAsync<LapTelemetryResponse>(
            $"/api/v1/sessions/{id}/telemetry?lap=2");

        lap.ShouldNotBeNull();
        lap.SessionId.ShouldBe(id);
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

        var lap = await fixture.ReadClient.GetFromJsonAsync<LapTelemetryResponse>(
            $"/api/v1/sessions/{id}/telemetry?lap=1");

        lap.ShouldNotBeNull();

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

    [Fact]
    public async Task only_the_requested_lap_comes_back()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.TelemetryAsync(id, lapNumber: 1, count: 5, startSequence: 0);
        await seed.TelemetryAsync(id, lapNumber: 2, count: 7, startSequence: 100);

        var lap = await fixture.ReadClient.GetFromJsonAsync<LapTelemetryResponse>(
            $"/api/v1/sessions/{id}/telemetry?lap=2");

        lap.ShouldNotBeNull();
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
