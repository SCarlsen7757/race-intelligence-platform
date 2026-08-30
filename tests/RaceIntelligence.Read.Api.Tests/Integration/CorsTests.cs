using RaceIntelligence.Read.Api.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Read.Api.Tests.Integration;

/// <summary>
/// The origin allowlist, which is the only thing guarding an API with no key.
/// </summary>
/// <remarks>
/// Worth asserting rather than assuming, because both failure modes are quiet. An allowlist that is
/// too narrow produces a dashboard that loads and then cannot fetch — which gets blamed on the
/// fetch. One that is too wide produces nothing visible at all.
/// </remarks>
[Collection(ReadAppCollection.Name)]
public sealed class CorsTests(ReadAppFixture fixture)
{
    [Fact]
    public async Task the_dashboard_origin_is_allowed_to_read()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        using var message = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sessions?limit=1");
        message.Headers.Add("Origin", fixture.DashboardOrigin);

        var response = await fixture.ReadClient.SendAsync(message);

        response.EnsureSuccessStatusCode();
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed).ShouldBeTrue();
        allowed.ShouldNotBeNull().ShouldContain(fixture.DashboardOrigin);
    }

    [Fact]
    public async Task another_origin_gets_no_allow_header()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        using var message = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sessions?limit=1");
        message.Headers.Add("Origin", "https://not-the-dashboard.example");

        var response = await fixture.ReadClient.SendAsync(message);

        // The request itself still succeeds — CORS is enforced by the browser, not the server — but
        // without this header no page on that origin may read the body.
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task reading_needs_no_api_key()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable");

        // No X-Api-Key on any read call in this assembly. This names that as intended rather than an
        // omission: to be useful the key would have to ship in a browser bundle, where it is not a
        // secret and only reads as one.
        var response = await fixture.ReadClient.GetAsync("/api/v1/sessions?limit=1");

        response.EnsureSuccessStatusCode();
    }
}
