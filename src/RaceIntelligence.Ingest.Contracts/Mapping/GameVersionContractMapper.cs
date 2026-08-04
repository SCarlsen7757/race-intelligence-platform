using RaceIntelligence.Core.Games;

namespace RaceIntelligence.Ingest.Contracts.Mapping;

/// <summary>Converts <see cref="GameVersionDto"/> to/from the canonical <see cref="GameVersionIdentity"/>.</summary>
public static class GameVersionContractMapper
{
    /// <summary>Converts a wire game-version DTO into its canonical Core form.</summary>
    public static GameVersionIdentity ToCore(GameVersionDto dto) => new()
    {
        Game = new GameIdentity(dto.GameKey, dto.GameName),
        GameVersion = dto.GameVersion,
        ApiVersionMajor = dto.ApiVersionMajor,
        ApiVersionMinor = dto.ApiVersionMinor,
        ConnectorVersion = dto.ConnectorVersion,
    };

    /// <summary>Converts a canonical game-version identity into its wire DTO form.</summary>
    public static GameVersionDto ToDto(GameVersionIdentity identity) => new(
        identity.Game.Key,
        identity.Game.Name,
        identity.GameVersion,
        identity.ApiVersionMajor,
        identity.ApiVersionMinor,
        identity.ConnectorVersion);
}
