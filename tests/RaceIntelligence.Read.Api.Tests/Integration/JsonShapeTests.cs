using System.Net.Http.Json;
using System.Text.Json;
using RaceIntelligence.Read.Api.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Read.Api.Tests.Integration;

/// <summary>
/// The shape of the JSON itself, rather than the values in it.
/// </summary>
/// <remarks>
/// <b>Absent means absent.</b> The dashboard mirrors these contracts by hand and types an unreported
/// channel as optional, which is only true if the field is genuinely missing from the payload — a
/// present <c>null</c> passes an <c>!== undefined</c> check and reads as a real value.
/// <para>
/// This is not hypothetical. Against a real session the lap picker opened on the out-lap rather than
/// the fastest lap, because every untimed lap arrived as <c>"lapTimeMs": null</c> and compared as
/// though it had a time. The fix was the serializer setting; this test is what keeps it.
/// </para>
/// </remarks>
[Collection(ReadAppCollection.Name)]
public sealed class JsonShapeTests(ReadAppFixture fixture)
{
    [Fact]
    public async Task an_untimed_lap_omits_its_time_rather_than_sending_null()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();

        // No lap time, no speeds — the shape a lap that never completed actually has.
        await seed.LapAsync(id, 1, timed: false);

        using var document = JsonDocument.Parse(
            await fixture.ReadClient.GetStringAsync($"/api/v1/sessions/{id}/laps"));

        var lap = document.RootElement[0];

        lap.TryGetProperty("lapNumber", out _).ShouldBeTrue();
        lap.TryGetProperty("lapTimeMs", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task a_timed_lap_still_carries_its_time()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.LapAsync(id, 1, lapTime: TimeSpan.FromSeconds(92.5));

        var laps = await fixture.ReadClient.GetFromJsonAsync<List<Contracts.LapResponse>>(
            $"/api/v1/sessions/{id}/laps");

        // The other half of the rule: omitting nulls must not omit real values.
        laps.ShouldNotBeNull();
        laps[0].LapTimeMs.ShouldNotBeNull().ShouldBe(92_500, tolerance: 1);
    }

    [Fact]
    public async Task a_session_omits_the_names_that_never_resolved()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var id = await new Seed(fixture).SessionAsync();

        using var document = JsonDocument.Parse(
            await fixture.ReadClient.GetStringAsync($"/api/v1/sessions/{id}"));

        // endedAtUtc is the honest case here: the seeded session was never ended, and "not ended" has
        // to stay distinguishable from "ended at some default".
        document.RootElement.TryGetProperty("endedAtUtc", out _).ShouldBeFalse();
        document.RootElement.TryGetProperty("trackName", out _).ShouldBeTrue();
    }
}
