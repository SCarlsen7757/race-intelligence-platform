namespace RaceIntelligence.Identity.Api.Contracts;

/// <summary>What a caller sends to assert a new person.</summary>
/// <param name="DisplayName">What to call them in the registry. A label, not an identity.</param>
public sealed record CreatePersonRequest(string? DisplayName);

/// <summary>What a caller sends to claim one simulator identity for a person.</summary>
/// <param name="SimKey">Which simulator issued the id — the same game key the collector uses.</param>
/// <param name="SimDriverId">The simulator's own id for the driver, exactly as it reports it.</param>
public sealed record CreateAliasRequest(string? SimKey, string? SimDriverId);

/// <summary>One simulator identity, as the registry reports it.</summary>
public sealed record AliasResponse(string SimKey, string SimDriverId, DateTimeOffset CreatedAt);

/// <summary>One person and every simulator they are known in.</summary>
public sealed record PersonResponse(
    Guid Id,
    string DisplayName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AliasResponse> Aliases);
