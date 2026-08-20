using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Identity;
using RaceIntelligence.Identity.Api.Auth;
using RaceIntelligence.Identity.Api.Contracts;
using RaceIntelligence.Identity.Entities;
using RaceIntelligence.Identity.Repositories;

namespace RaceIntelligence.Identity.Api.Endpoints;

/// <summary>
/// The registry's whole surface: people, and the simulator identities each of them holds.
/// </summary>
/// <remarks>
/// Small on purpose. This is hand-curated state maintained by one person, so the endpoints are the
/// operations a human doing that work performs — assert a person, claim an id for them, release a
/// claim, and read back what is claimed. There is no search, no paging and no bulk import, because
/// there is nothing yet to import from and the table is measured in tens.
/// <para>
/// <b>There is no "create person from driver id" convenience</b>, which is the obvious thing to want
/// and the thing that would quietly turn an asserted registry into a derived one. Deciding that a
/// driver id belongs to a person is the human's job; this service records the decision.
/// </para>
/// </remarks>
public static class PersonEndpoints
{
    public static void MapPersonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").AddEndpointFilter<ApiKeyFilter>();

        group.MapGet("/people", ListPeopleAsync);
        group.MapGet("/people/{id:guid}", GetPersonAsync);
        group.MapPost("/people", CreatePersonAsync);
        group.MapPost("/people/{id:guid}/aliases", CreateAliasAsync);
        group.MapDelete("/people/{id:guid}/aliases/{simKey}/{simDriverId}", DeleteAliasAsync);
        group.MapGet("/aliases", ListAliasesAsync);
    }

    private static async Task<IResult> ListPeopleAsync(PersonRepository people, CancellationToken ct)
    {
        var rows = await people.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(rows.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetPersonAsync(Guid id, PersonRepository people, CancellationToken ct)
    {
        var person = await people.FindAsync(id, ct).ConfigureAwait(false);
        return person is null ? ProblemResults.PersonNotFound(id) : Results.Ok(ToResponse(person));
    }

    private static async Task<IResult> CreatePersonAsync(
        CreatePersonRequest request,
        PersonRepository people,
        IdentityDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName))
        {
            return ProblemResults.Required(nameof(request.DisplayName));
        }

        var person = people.Add(displayName, clock.GetUtcNow());
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/v1/people/{person.Id}", ToResponse(person));
    }

    private static async Task<IResult> CreateAliasAsync(
        Guid id,
        CreateAliasRequest request,
        PersonRepository people,
        IdentityDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var simKey = request.SimKey?.Trim();
        var simDriverId = request.SimDriverId?.Trim();

        if (string.IsNullOrEmpty(simKey))
        {
            return ProblemResults.Required(nameof(request.SimKey));
        }

        if (string.IsNullOrEmpty(simDriverId))
        {
            return ProblemResults.Required(nameof(request.SimDriverId));
        }

        // Checked before the insert so an unknown person is a 404 rather than a foreign-key error
        // surfacing as a 500. The claim's own uniqueness is left to the database — see below.
        if (await people.FindAsync(id, ct).ConfigureAwait(false) is null)
        {
            return ProblemResults.PersonNotFound(id);
        }

        people.AddAlias(id, simKey, simDriverId, clock.GetUtcNow());

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Someone already claims this simulator identity. Reported rather than reassigned: the
            // earlier assertion is a human's and outranks this one until they release it.
            return ProblemResults.AliasAlreadyClaimed(simKey, simDriverId);
        }

        var person = await people.FindAsync(id, ct).ConfigureAwait(false);
        return person is null ? ProblemResults.PersonNotFound(id) : Results.Ok(ToResponse(person));
    }

    private static async Task<IResult> DeleteAliasAsync(
        Guid id,
        string simKey,
        string simDriverId,
        PersonRepository people,
        IdentityDbContext db,
        CancellationToken ct)
    {
        if (!await people.RemoveAliasAsync(id, simKey, simDriverId, ct).ConfigureAwait(false))
        {
            return ProblemResults.AliasNotFound(id, simKey, simDriverId);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>
    /// Which simulator identities are already claimed in one simulator.
    /// </summary>
    /// <remarks>
    /// Half of the unmapped-driver worklist, and the half this service can answer honestly. The
    /// other half — which of a simulator's drivers nobody has claimed — is a diff against that
    /// simulator's own <c>drivers</c> table, which this service deliberately cannot reach. Whoever
    /// holds that table asks for this set and does the subtraction.
    /// </remarks>
    private static async Task<IResult> ListAliasesAsync(string? simKey, PersonRepository people, CancellationToken ct)
    {
        var key = simKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return ProblemResults.Required(nameof(simKey));
        }

        var aliases = await people.ListAliasesAsync(key, ct).ConfigureAwait(false);
        return Results.Ok(aliases.Select(ToResponse).ToList());
    }

    private static AliasResponse ToResponse(PersonSimAlias alias) =>
        new(alias.SimKey, alias.SimDriverId, alias.CreatedAt);

    private static PersonResponse ToResponse(Person person) => new(
        person.Id,
        person.DisplayName,
        person.CreatedAt,
        person.Aliases
            .OrderBy(a => a.SimKey, StringComparer.Ordinal)
            .ThenBy(a => a.SimDriverId, StringComparer.Ordinal)
            .Select(ToResponse)
            .ToList());
}
