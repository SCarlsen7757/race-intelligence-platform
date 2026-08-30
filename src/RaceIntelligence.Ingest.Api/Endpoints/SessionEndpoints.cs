using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Ingest.Api.Auth;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Mapping;
using RaceIntelligence.Persistence.Core;
using RaceIntelligence.Persistence.Core.Converters;
using RaceIntelligence.Persistence.Core.Mapping;
using RaceIntelligence.Persistence.Core.Repositories;
using CoreSessions = RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Ingest.Api.Endpoints;

/// <summary>Maps the low-frequency, JSON session and lap endpoints under <c>/api/v1/sessions</c>.</summary>
public static class SessionEndpoints
{
    /// <summary>Registers the session/lap endpoints on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/sessions").AddEndpointFilter<ApiKeyFilter>();

        group.MapPost("/", CreateSessionAsync);
        group.MapPatch("/{id:guid}", UpdateSessionAsync);
        group.MapPost("/{id:guid}/laps", RecordLapAsync);

        return app;
    }

    /// <summary>
    /// Resolves (or creates) the games/tracks/cars/drivers a session references, then inserts the
    /// session itself. Idempotent on <see cref="SessionCreateRequest.SessionId"/>: a repeat call
    /// with the same id returns 200 without creating a duplicate, both for the common case (an
    /// earlier successful response was lost) and the rare concurrent-request race (caught via the
    /// primary-key unique-violation below).
    /// </summary>
    /// <remarks>
    /// Each resolve-or-create below runs its own <c>SaveChanges</c>, so without the explicit
    /// transaction this method commits four to six times on the way to one logical insert. A failure
    /// after any of them left the already-committed reference rows behind — most visibly a driver
    /// row belonging to no session at all, which once had to be scrubbed by a migration before a
    /// NOT NULL <c>game_id</c> could be added. Wrapping the sequence
    /// makes the whole thing land or none of it. The repositories' unique-violation retries still
    /// work inside it: EF Core takes a savepoint before each <c>SaveChanges</c> when a transaction
    /// is already open and rolls back to it on failure, so a caught conflict does not leave the
    /// transaction aborted.
    /// </remarks>
    private static async Task<IResult> CreateSessionAsync(
        SessionCreateRequest request,
        TelemetryDbContext db,
        GameVersionRepository versionRepo,
        IConfiguration configuration,
        TrackRepository trackRepo,
        CarRepository carRepo,
        DriverRepository driverRepo,
        CancellationToken ct)
    {
        if (!SchemaVersion.IsSupported(request.SchemaVersion))
        {
            return ProblemResults.SchemaVersionUnsupported(request.SchemaVersion);
        }

        // The sim's raw codes are carried through untranslated, but they still have to fit the
        // smallint columns that store them. Narrowing an out-of-range value wraps it into a
        // different, plausible-looking code, so it is rejected here instead.
        if (!CheckedSmallIntConverter.IsRepresentable(request.FuelUsageRate))
        {
            return ProblemResults.ValueOutOfRange(nameof(request.FuelUsageRate), request.FuelUsageRate!.Value);
        }

        if (!CheckedSmallIntConverter.IsRepresentable(request.TyreWearRate))
        {
            return ProblemResults.ValueOutOfRange(nameof(request.TyreWearRate), request.TyreWearRate!.Value);
        }

        if (!CheckedSmallIntConverter.IsRepresentable(request.SessionType))
        {
            return ProblemResults.ValueOutOfRange(nameof(request.SessionType), request.SessionType);
        }

        var existing = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SessionId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Results.Ok(new { existing.Id });
        }

        // ExtrasJson is raw client-supplied text, so parsing it is a client error when it fails, not
        // a server one. Done before the transaction opens so a doomed request costs no database work.
        CoreSessions.SessionInfo sessionInfo;
        try
        {
            sessionInfo = SessionContractMapper.ToSessionInfo(request);
        }
        catch (JsonException ex)
        {
            return ProblemResults.MalformedJson(nameof(SessionCreateRequest.ExtrasJson), ex.Message);
        }

        // This database holds one simulator's telemetry and nothing else (ADR 0001), so a post from
        // another one is not a row to store — it is a misconfigured collector, and the only useful
        // thing to do with it is say so. Accepting it would put two simulators' cars and drivers in
        // a schema whose unique keys no longer scope by game, which silently merges them.
        var expectedGameKey = configuration["Ingest:GameKey"];
        var gameVersion = request.GameVersion;

        // Case-insensitive because a game key is a lowercase convention rather than a constraint,
        // and refusing a session over the capitalisation of "RaceRoom" would be a check that costs
        // more than the mistake it catches.
        if (gameVersion is null
            || !string.Equals(expectedGameKey, gameVersion.GameKey, StringComparison.OrdinalIgnoreCase))
        {
            return ProblemResults.WrongSimulator(expectedGameKey, gameVersion?.GameKey);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var versionRow = await versionRepo.ResolveOrCreateAsync(
            GameVersionContractMapper.ToCore(gameVersion), ct).ConfigureAwait(false);

        // The repository decides for itself whether there is anything to resolve — it returns null
        // when neither a sim driver id nor a name was reported — so there is no pre-check here.
        Guid? driverId = (await driverRepo.ResolveOrCreateAsync(
            request.SimDriverId, request.PlayerName, ct).ConfigureAwait(false))?.Id;

        var (_, layout) = await trackRepo.ResolveOrCreateAsync(
            request.TrackName, request.LayoutName, request.LayoutLengthMeters ?? 0, ct: ct).ConfigureAwait(false);

        // As with the driver above, the repository decides for itself whether there is anything to
        // resolve. SimCarId is the identity; CarName is only the label shown for it.
        Guid? carId = (await carRepo.ResolveOrCreateCarAsync(
            request.SimCarId, request.CarName, request.ManufacturerName, request.CarClassName, ct).ConfigureAwait(false))?.Id;

        var entity = SessionMapper.ToEntity(sessionInfo, versionRow.Id, driverId, layout.Id, carId, request.SchemaVersion);

        db.Sessions.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Lost a race with a concurrent identical create for the same SessionId; the row
            // already exists, which is exactly the idempotent outcome this endpoint promises. The
            // winner resolved the same reference data on its way there, so discarding ours costs
            // nothing and keeps the "all or nothing" guarantee intact.
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(new { entity.Id });
    }

    /// <summary>Applies partial updates (end time, extras) to an existing session. 404 if unknown.</summary>
    private static async Task<IResult> UpdateSessionAsync(
        Guid id,
        SessionUpdateRequest request,
        TelemetryDbContext db,
        CancellationToken ct)
    {
        if (!SchemaVersion.IsSupported(request.SchemaVersion))
        {
            return ProblemResults.SchemaVersionUnsupported(request.SchemaVersion);
        }

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (session is null)
        {
            return ProblemResults.SessionNotFound(id);
        }

        if (request.EndedAtUtc is { } endedAt)
        {
            session.EndedAt = endedAt;
        }

        if (request.ExtrasJson is not null)
        {
            if (!TryParseJson(request.ExtrasJson, out var extras))
            {
                return ProblemResults.MalformedJson(nameof(SessionUpdateRequest.ExtrasJson), extras.Reason!);
            }

            session.Extras = extras.Value;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { session.Id });
    }

    /// <summary>Upserts a lap summary by <c>(session id, LapNumber)</c>. 404 if the session is unknown.</summary>
    private static async Task<IResult> RecordLapAsync(
        Guid id,
        LapCompletedRequest request,
        TelemetryDbContext db,
        CancellationToken ct)
    {
        if (!SchemaVersion.IsSupported(request.SchemaVersion))
        {
            return ProblemResults.SchemaVersionUnsupported(request.SchemaVersion);
        }

        var sessionExists = await db.Sessions.AnyAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (!sessionExists)
        {
            return ProblemResults.SessionNotFound(id);
        }

        var lapInfo = SessionContractMapper.ToLapInfo(id, request);
        var existingLap = await db.Laps
            .FirstOrDefaultAsync(l => l.SessionId == id && l.LapNumber == request.LapNumber, ct).ConfigureAwait(false);

        if (existingLap is null)
        {
            db.Laps.Add(SessionMapper.ToEntity(lapInfo));
        }
        else
        {
            existingLap.LapTime = lapInfo.LapTime;
            existingLap.FuelUsed = lapInfo.FuelUsed;
            existingLap.AvgSpeed = lapInfo.AverageSpeed;
            existingLap.MaxSpeed = lapInfo.MaxSpeed;
            existingLap.IsValid = lapInfo.IsValid;
            // QualityScore is deliberately left untouched here: the collector never sets it (it is
            // computed later by the Analysis Engine), so a re-submitted lap must not clobber an
            // already-computed score.
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { SessionId = id, request.LapNumber });
    }

    /// <summary>
    /// Parses client-supplied raw JSON text, reporting failure rather than throwing. The document is
    /// cloned and disposed instead of being kept alive by the returned element, so its pooled
    /// buffers go back to the pool.
    /// </summary>
    private static bool TryParseJson(string json, out ParsedJson parsed)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            parsed = new ParsedJson(document.RootElement.Clone(), null);
            return true;
        }
        catch (JsonException ex)
        {
            parsed = new ParsedJson(default, ex.Message);
            return false;
        }
    }

    private readonly record struct ParsedJson(JsonElement Value, string? Reason);
}
