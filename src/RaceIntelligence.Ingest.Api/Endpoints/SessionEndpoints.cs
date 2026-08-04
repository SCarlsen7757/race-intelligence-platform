using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RaceIntelligence.Ingest.Api.Auth;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Mapping;
using RaceIntelligence.Persistence;
using RaceIntelligence.Persistence.Mapping;
using RaceIntelligence.Persistence.Repositories;

namespace RaceIntelligence.Ingest.Api.Endpoints;

/// <summary>Maps the low-frequency, JSON session and lap endpoints under <c>/api/v1/sessions</c>.</summary>
public static class SessionEndpoints
{
    private const string UniqueViolationSqlState = "23505";

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
    private static async Task<IResult> CreateSessionAsync(
        SessionCreateRequest request,
        RaceIntelligenceDbContext db,
        GameRepository gameRepo,
        TrackRepository trackRepo,
        CarRepository carRepo,
        DriverRepository driverRepo,
        CancellationToken ct)
    {
        if (!SchemaVersion.IsSupported(request.SchemaVersion))
        {
            return ProblemResults.SchemaVersionUnsupported(request.SchemaVersion);
        }

        var existing = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SessionId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Results.Ok(new { existing.Id });
        }

        var (game, gameVersion) = await gameRepo.ResolveOrCreateAsync(
            GameVersionContractMapper.ToCore(request.GameVersion), ct).ConfigureAwait(false);

        Guid? driverId = string.IsNullOrWhiteSpace(request.PlayerName)
            ? null
            : (await driverRepo.ResolveOrCreateAsync(request.PlayerName, ct).ConfigureAwait(false)).Id;

        var (_, layout) = await trackRepo.ResolveOrCreateAsync(
            game.Id, request.TrackName, request.LayoutName, request.LayoutLengthMeters ?? 0, ct: ct).ConfigureAwait(false);

        Guid? carId = string.IsNullOrWhiteSpace(request.CarName)
            ? null
            : (await carRepo.ResolveOrCreateCarAsync(
                game.Id, request.CarName, request.CarName, request.ManufacturerName, request.CarClassName, ct).ConfigureAwait(false)).Id;

        var sessionInfo = SessionContractMapper.ToSessionInfo(request);
        var entity = SessionMapper.ToEntity(sessionInfo, gameVersion.Id, driverId, layout.Id, carId, request.SchemaVersion);

        db.Sessions.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost a race with a concurrent identical create for the same SessionId; the row
            // already exists, which is exactly the idempotent outcome this endpoint promises.
        }

        return Results.Ok(new { entity.Id });
    }

    /// <summary>Applies partial updates (end time, weather, setup, extras) to an existing session. 404 if unknown.</summary>
    private static async Task<IResult> UpdateSessionAsync(
        Guid id,
        SessionUpdateRequest request,
        RaceIntelligenceDbContext db,
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

        if (request.WeatherJson is not null)
        {
            session.Weather = JsonDocument.Parse(request.WeatherJson).RootElement;
        }

        if (request.SetupJson is not null)
        {
            session.Setup = JsonDocument.Parse(request.SetupJson).RootElement;
        }

        if (request.ExtrasJson is not null)
        {
            session.Extras = JsonDocument.Parse(request.ExtrasJson).RootElement;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { session.Id });
    }

    /// <summary>Upserts a lap summary by <c>(session id, LapNumber)</c>. 404 if the session is unknown.</summary>
    private static async Task<IResult> RecordLapAsync(
        Guid id,
        LapCompletedRequest request,
        RaceIntelligenceDbContext db,
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

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}
