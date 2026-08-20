using System.Net;
using System.Net.Http.Json;
using RaceIntelligence.Ingest.Api.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Ingest.Api.Tests.Integration;

/// <summary>
/// The identity registry's HTTP surface, against the real service.
/// </summary>
/// <remarks>
/// <b>Living in the ingest API's test project is a wart, and a deliberate one.</b>
/// <see cref="AspireAppFixture"/> boots the whole AppHost graph — which now includes the identity
/// service — and it lives here. The alternatives were to boot a second Aspire graph in the identity
/// suite, which doubles the slowest thing in the test run, or to lift the fixture into a shared
/// test-support project, which is a refactor of this project rather than a part of the registry.
/// Worth doing when a third service needs it; not worth doing for the second.
/// <para>
/// The registry's actual rules — one identity to at most one person, cascade on delete, ids compared
/// as text — are asserted against the database in <c>RaceIntelligence.Identity.Tests</c>, where they
/// belong. What is tested here is only what the HTTP layer adds: which failures become which status
/// codes, and that the key is required.
/// </para>
/// </remarks>
[Collection(AspireAppCollection.Name)]
public sealed class IdentityEndpointsTests(AspireAppFixture fixture)
{
    private void SkipWithoutApp()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }
    }

    /// <summary>A unique suffix per test, so one running service can host every case.</summary>
    private static string Unique() => Guid.CreateVersion7().ToString("N")[..12];

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Api-Key", AspireAppFixture.IdentityApiKey);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<Guid> CreatePersonAsync(string displayName)
    {
        var response = await fixture.IdentityClient.SendAsync(
            Request(HttpMethod.Post, "/api/v1/people", new { displayName }));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<PersonPayload>();
        created.ShouldNotBeNull();
        return created.Id;
    }

    [Fact]
    public async Task A_person_is_created_and_read_back()
    {
        SkipWithoutApp();

        var name = $"Created {Unique()}";
        var id = await CreatePersonAsync(name);

        var response = await fixture.IdentityClient.SendAsync(Request(HttpMethod.Get, $"/api/v1/people/{id}"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var person = await response.Content.ReadFromJsonAsync<PersonPayload>();
        person.ShouldNotBeNull();
        person.DisplayName.ShouldBe(name);
        person.Aliases.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_claim_is_recorded_against_the_person_who_made_it()
    {
        SkipWithoutApp();

        var suffix = Unique();
        var id = await CreatePersonAsync($"Claimant {suffix}");

        var response = await fixture.IdentityClient.SendAsync(Request(
            HttpMethod.Post,
            $"/api/v1/people/{id}/aliases",
            new { simKey = $"raceroom-{suffix}", simDriverId = "4242" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var person = await response.Content.ReadFromJsonAsync<PersonPayload>();
        person.ShouldNotBeNull();
        person.Aliases.Single().SimDriverId.ShouldBe("4242");
    }

    /// <summary>
    /// The one status code worth arguing about. A second claim on the same simulator identity is a
    /// conflict rather than an overwrite: the earlier assertion is a human's, and silently
    /// reassigning it would rewrite that on the strength of a later request with no record.
    /// </summary>
    [Fact]
    public async Task Claiming_an_identity_someone_else_holds_is_a_conflict()
    {
        SkipWithoutApp();

        var suffix = Unique();
        var sim = $"raceroom-{suffix}";
        var first = await CreatePersonAsync($"First {suffix}");
        var second = await CreatePersonAsync($"Second {suffix}");

        var claim = new { simKey = sim, simDriverId = "4242" };

        (await fixture.IdentityClient.SendAsync(Request(HttpMethod.Post, $"/api/v1/people/{first}/aliases", claim)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var conflict = await fixture.IdentityClient.SendAsync(
            Request(HttpMethod.Post, $"/api/v1/people/{second}/aliases", claim));

        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>Releasing a claim frees the identity, or a mis-assertion would be permanent.</summary>
    [Fact]
    public async Task A_released_identity_can_be_claimed_by_someone_else()
    {
        SkipWithoutApp();

        var suffix = Unique();
        var sim = $"raceroom-{suffix}";
        var wrong = await CreatePersonAsync($"Wrong {suffix}");
        var right = await CreatePersonAsync($"Right {suffix}");
        var claim = new { simKey = sim, simDriverId = "4242" };

        await fixture.IdentityClient.SendAsync(Request(HttpMethod.Post, $"/api/v1/people/{wrong}/aliases", claim));

        var released = await fixture.IdentityClient.SendAsync(
            Request(HttpMethod.Delete, $"/api/v1/people/{wrong}/aliases/{sim}/4242"));
        released.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reclaimed = await fixture.IdentityClient.SendAsync(
            Request(HttpMethod.Post, $"/api/v1/people/{right}/aliases", claim));
        reclaimed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Claiming_for_a_person_who_does_not_exist_is_a_404()
    {
        SkipWithoutApp();

        var response = await fixture.IdentityClient.SendAsync(Request(
            HttpMethod.Post,
            $"/api/v1/people/{Guid.CreateVersion7()}/aliases",
            new { simKey = $"raceroom-{Unique()}", simDriverId = "4242" }));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Releasing_a_claim_nobody_made_is_a_404()
    {
        SkipWithoutApp();

        var id = await CreatePersonAsync($"Nothing claimed {Unique()}");

        var response = await fixture.IdentityClient.SendAsync(
            Request(HttpMethod.Delete, $"/api/v1/people/{id}/aliases/raceroom/never-claimed"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_person_needs_a_name(string displayName)
    {
        SkipWithoutApp();

        var response = await fixture.IdentityClient.SendAsync(
            Request(HttpMethod.Post, "/api/v1/people", new { displayName }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_claim_needs_both_halves_of_the_identity()
    {
        SkipWithoutApp();

        var id = await CreatePersonAsync($"Incomplete {Unique()}");

        (await fixture.IdentityClient.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/people/{id}/aliases", new { simKey = "", simDriverId = "4242" })))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await fixture.IdentityClient.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/people/{id}/aliases", new { simKey = "raceroom", simDriverId = "" })))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The half of the unmapped worklist this service can answer, scoped to one simulator — the
    /// other simulator's claims must not appear in it, or the diff on the far side is against the
    /// wrong set.
    /// </summary>
    [Fact]
    public async Task Claimed_identities_are_listed_for_one_simulator_only()
    {
        SkipWithoutApp();

        var suffix = Unique();
        var raceroom = $"raceroom-{suffix}";
        var id = await CreatePersonAsync($"Listed {suffix}");

        await fixture.IdentityClient.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/people/{id}/aliases", new { simKey = raceroom, simDriverId = "4242" }));
        await fixture.IdentityClient.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/people/{id}/aliases", new { simKey = $"iracing-{suffix}", simDriverId = "881109" }));

        var response = await fixture.IdentityClient.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/aliases?simKey={raceroom}"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var claimed = await response.Content.ReadFromJsonAsync<List<AliasPayload>>();
        claimed.ShouldNotBeNull();
        claimed.Select(a => a.SimDriverId).ShouldBe(["4242"]);
    }

    /// <summary>
    /// The registry holds the only unrebuildable state in the platform, so an unauthenticated write
    /// reaching it would matter more here than anywhere else.
    /// </summary>
    [Fact]
    public async Task Every_endpoint_refuses_a_request_with_no_key()
    {
        SkipWithoutApp();

        var unkeyed = await fixture.IdentityClient.PostAsJsonAsync(
            "/api/v1/people", new { displayName = "No key" });

        unkeyed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var read = await fixture.IdentityClient.GetAsync("/api/v1/people");
        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private sealed record AliasPayload(string SimKey, string SimDriverId, DateTimeOffset CreatedAt);

    private sealed record PersonPayload(
        Guid Id,
        string DisplayName,
        DateTimeOffset CreatedAt,
        IReadOnlyList<AliasPayload> Aliases);
}
