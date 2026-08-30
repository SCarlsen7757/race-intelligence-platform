using RaceIntelligence.RaceRoom.Channels;

namespace RaceIntelligence.RaceRoom.Telemetry;

/// <summary>
/// Every channel by name, column and group — the list the read API validates <c>?channels=</c>
/// against and the list tests assert the schema and the dashboard's contracts against.
/// </summary>
[GeneratedFromChannels(ChannelArtifact.Manifest)]
public static partial class RaceRoomChannels;
