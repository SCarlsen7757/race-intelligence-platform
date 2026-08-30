using System.Net;
using System.Net.Http.Json;
using RaceIntelligence.Read.Api.Contracts;
using RaceIntelligence.Read.Api.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Read.Api.Tests.Integration;

/// <summary>
/// End-to-end tests for reading sessions and laps back out of a real database.
/// </summary>
/// <remarks>
/// Everything here seeds through the ingest API first, so each test exercises the whole round trip:
/// the collector's contract in, storage, and the read contract out.
/// </remarks>
[Collection(ReadAppCollection.Name)]
public sealed class SessionReadEndpointsTests(ReadAppFixture fixture)
{
    [Fact]
    public async Task a_seeded_session_comes_back_with_its_names_resolved()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var id = await new Seed(fixture).SessionAsync(playerName: "Ayrton");

        var session = await fixture.ReadClient.GetFromJsonAsync<SessionSummaryResponse>($"/api/v1/sessions/{id}");

        session.ShouldNotBeNull();
        session.SessionId.ShouldBe(id);
        session.TrackName.ShouldBe("Suzuka");
        session.LayoutName.ShouldBe("Grand Prix");
        session.CarName.ShouldBe("Test GT3 Car");
        session.PlayerName.ShouldBe("Ayrton");
    }

    [Fact]
    public async Task an_unknown_session_is_a_404_rather_than_an_empty_body()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var response = await fixture.ReadClient.GetAsync($"/api/v1/sessions/{Guid.CreateVersion7()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task the_listing_returns_the_newest_session_first()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var anchor = DateTimeOffset.UtcNow;

        var older = await seed.SessionAsync(startedAt: anchor.AddHours(-2));
        var newer = await seed.SessionAsync(startedAt: anchor.AddHours(-1));

        var page = await fixture.ReadClient.GetFromJsonAsync<SessionPageResponse>("/api/v1/sessions?limit=200");

        page.ShouldNotBeNull();

        var ids = page.Sessions.Select(s => s.SessionId).ToList();
        ids.ShouldContain(older);
        ids.ShouldContain(newer);

        // Relative order, not absolute position: this database is shared with every other test in
        // the assembly, so asserting "newer is first overall" would make this test depend on what
        // else happened to run.
        ids.IndexOf(newer).ShouldBeLessThan(ids.IndexOf(older));
    }

    [Fact]
    public async Task the_before_cursor_excludes_what_the_previous_page_ended_on()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var anchor = DateTimeOffset.UtcNow;

        var first = await seed.SessionAsync(startedAt: anchor.AddHours(-1));
        await seed.SessionAsync(startedAt: anchor.AddHours(-3));

        var page = await fixture.ReadClient.GetFromJsonAsync<SessionPageResponse>(
            $"/api/v1/sessions?before={Uri.EscapeDataString(anchor.AddHours(-2).ToString("O"))}&limit=200");

        page.ShouldNotBeNull();
        page.Sessions.Select(s => s.SessionId).ShouldNotContain(first);
    }

    [Fact]
    public async Task a_full_page_reports_a_cursor_and_a_short_one_does_not()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        await seed.SessionAsync();
        await seed.SessionAsync();

        var full = await fixture.ReadClient.GetFromJsonAsync<SessionPageResponse>("/api/v1/sessions?limit=1");
        full.ShouldNotBeNull();
        full.Sessions.Count.ShouldBe(1);
        full.NextBefore.ShouldNotBeNull();

        // Far enough back that nothing precedes it, so the page is short and the listing ends.
        var empty = await fixture.ReadClient.GetFromJsonAsync<SessionPageResponse>(
            $"/api/v1/sessions?before={Uri.EscapeDataString(DateTimeOffset.UnixEpoch.ToString("O"))}");
        empty.ShouldNotBeNull();
        empty.Sessions.ShouldBeEmpty();
        empty.NextBefore.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public async Task a_limit_outside_the_accepted_range_is_refused(int limit)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var response = await fixture.ReadClient.GetAsync($"/api/v1/sessions?limit={limit}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task laps_come_back_in_lap_order_with_their_times_in_milliseconds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();

        // Posted out of order on purpose: the ordering must come from the query, not from the order
        // the rows happened to be written in.
        await seed.LapAsync(id, 3, TimeSpan.FromSeconds(91.5));
        await seed.LapAsync(id, 1, TimeSpan.FromSeconds(93.25));
        await seed.LapAsync(id, 2, TimeSpan.FromSeconds(92));

        var laps = await fixture.ReadClient.GetFromJsonAsync<List<LapResponse>>($"/api/v1/sessions/{id}/laps");

        laps.ShouldNotBeNull();
        laps.Select(l => l.LapNumber).ShouldBe([1, 2, 3]);
        laps[0].LapTimeMs.ShouldNotBeNull().ShouldBe(93_250, tolerance: 1);
        laps[1].LapTimeMs.ShouldNotBeNull().ShouldBe(92_000, tolerance: 1);
    }

    [Fact]
    public async Task laps_for_an_unknown_session_are_a_404_not_an_empty_list()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var response = await fixture.ReadClient.GetAsync($"/api/v1/sessions/{Guid.CreateVersion7()}/laps");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task a_session_reports_how_many_laps_and_samples_it_holds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        var seed = new Seed(fixture);
        var id = await seed.SessionAsync();
        await seed.LapAsync(id, 1);
        await seed.LapAsync(id, 2);
        await seed.TelemetryAsync(id, lapNumber: 1, count: 5);

        var session = await fixture.ReadClient.GetFromJsonAsync<SessionSummaryResponse>($"/api/v1/sessions/{id}");

        session.ShouldNotBeNull();
        session.LapCount.ShouldBe(2);
        // The number that decides whether a session is worth opening: laps without samples chart
        // nothing.
        session.SampleCount.ShouldBe(5);
    }
}
