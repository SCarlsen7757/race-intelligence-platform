using System.Net.Http.Json;
using MessagePack;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Telemetry;

namespace RaceIntelligence.Read.Api.Tests.Support;

/// <summary>
/// Puts a session, its laps and its telemetry into the database the way the collector does.
/// </summary>
/// <remarks>
/// <b>Seeds over HTTP through the real ingest API rather than writing rows.</b> Inserting directly
/// would be faster and would test less: the thing worth asserting is that what a collector posts is
/// what a dashboard reads back, across two services, two contracts and the promotion of RaceRoom's
/// channels at the storage boundary. A fixture that writes its own rows can only prove the read API
/// agrees with the fixture.
/// </remarks>
internal sealed class Seed(ReadAppFixture fixture)
{
    /// <summary>The simulator the ingest API is configured for. A session claiming another is refused.</summary>
    public const string GameKey = "raceroom";

    /// <summary>Creates a session and returns its id.</summary>
    public async Task<Guid> SessionAsync(DateTimeOffset? startedAt = null, string? playerName = null)
    {
        var id = Guid.CreateVersion7();

        var request = new SessionCreateRequest(
            SchemaVersion: RaceIntelligence.Ingest.Contracts.SchemaVersion.Current,
            SessionId: id,
            // Unique per session so tests never contend on one game_versions row.
            GameVersion: new GameVersionDto(GameKey, "RaceRoom Racing Experience", "1.2.3", 1, 0, $"test-{Guid.NewGuid():N}"),
            Capabilities: 0,
            TrackName: "Suzuka",
            LayoutName: "Grand Prix",
            LayoutLengthMeters: 5807f,
            SessionType: 3,
            StartedAtUtc: startedAt ?? DateTimeOffset.UtcNow,
            PlayerName: playerName ?? "Test Driver",
            CarName: "Test GT3 Car",
            CarClassName: "GT3",
            ManufacturerName: "Acme Motors",
            ExtrasJson: null);

        var response = await PostJsonAsync("/api/v1/sessions", request);
        response.EnsureSuccessStatusCode();

        return id;
    }

    /// <summary>
    /// Records one lap against a session.
    /// </summary>
    /// <remarks>
    /// <paramref name="lapTime"/> defaults to a plausible time rather than to null, but passing an
    /// explicit null is meaningful and used: a lap that never completed has no time, and that case
    /// is what the JSON-shape tests are about.
    /// </remarks>
    public async Task LapAsync(
        Guid sessionId,
        int lapNumber,
        TimeSpan? lapTime = null,
        bool isValid = true,
        bool timed = true)
    {
        var request = new LapCompletedRequest(
            SchemaVersion: RaceIntelligence.Ingest.Contracts.SchemaVersion.Current,
            LapNumber: lapNumber,
            LapTime: timed ? lapTime ?? TimeSpan.FromSeconds(92.5 + lapNumber) : null,
            FuelUsed: 2.4f,
            AverageSpeed: 62.5f,
            MaxSpeed: 78.25f,
            IsValid: isValid);

        var response = await PostJsonAsync($"/api/v1/sessions/{sessionId}/laps", request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Uploads <paramref name="count"/> telemetry samples for one lap, with values that vary by
    /// sequence number so a test can tell one sample from another.
    /// </summary>
    public async Task TelemetryAsync(Guid sessionId, int lapNumber, int count, long startSequence = 0)
    {
        var anchor = DateTimeOffset.UtcNow;
        var samples = new List<TelemetrySampleDto>(count);

        for (int i = 0; i < count; i++)
        {
            long sequence = startSequence + i;
            samples.Add(Sample(sessionId, sequence, lapNumber, anchor.AddMilliseconds(sequence * 20)));
        }

        var batch = new TelemetryBatchRequest(
            RaceIntelligence.Ingest.Contracts.SchemaVersion.Current,
            sessionId,
            startSequence,
            startSequence + count - 1,
            samples);

        byte[] bytes = MessagePackSerializer.Serialize(batch, TelemetryMessagePackOptions.Default);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/telemetry:batch")
        {
            Content = new ByteArrayContent(bytes),
        };
        message.Headers.Add("X-Api-Key", ReadAppFixture.IngestApiKey);

        var response = await fixture.IngestClient.SendAsync(message);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The speed a sample at <paramref name="sequence"/> is seeded with, so a test can assert on the exact value.</summary>
    public static float SpeedFor(long sequence) => 40f + sequence;

    /// <summary>The throttle a sample at <paramref name="sequence"/> is seeded with.</summary>
    public static float ThrottleFor(long sequence) => (sequence % 10) / 10f;

    private static TelemetrySampleDto Sample(Guid sessionId, long sequence, int lapNumber, DateTimeOffset timestamp) => new()
    {
        SessionId = sessionId,
        SequenceNumber = sequence,
        Timestamp = timestamp,
        SimulationTime = sequence * 0.1,
        Speed = SpeedFor(sequence),
        Throttle = ThrottleFor(sequence),
        Brake = 0f,
        Clutch = 0.25f,
        Steering = 0.1f,
        Gear = 4,
        EngineRpm = 6500f,
        FuelLeft = 40.2f,
        LapNumber = lapNumber,
        Sector = 1,
        Position = 3,
        WheelSpeedFrontLeft = 45.1f,
        WheelSpeedFrontRight = 45.2f,
        WheelSpeedRearLeft = 44.9f,
        WheelSpeedRearRight = 45.0f,
        SuspensionTravelFrontLeft = 0.05f,
        SuspensionTravelFrontRight = 0.05f,
        SuspensionTravelRearLeft = 0.06f,
        SuspensionTravelRearRight = 0.06f,
        TyrePressureFrontLeft = 180f,
        TyrePressureFrontRight = 180f,
        TyrePressureRearLeft = 175f,
        TyrePressureRearRight = 175f,
        TyreWearFrontLeft = 0.1f,
        TyreWearFrontRight = 0.1f,
        TyreWearRearLeft = 0.12f,
        TyreWearRearRight = 0.12f,
        TyreTemperatureFrontLeft = Temperature(),
        TyreTemperatureFrontRight = Temperature(),
        TyreTemperatureRearLeft = Temperature(),
        TyreTemperatureRearRight = Temperature(),
        TrackPositionFraction = 0.42f,
        Extras = "{}",
    };

    private static TyreTemperatureDto Temperature() =>
        new() { Inner = 85, Middle = 90, Outer = 88, Optimal = 90, Cold = 70, Hot = 110 };

    private async Task<HttpResponseMessage> PostJsonAsync<T>(string path, T body)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-Api-Key", ReadAppFixture.IngestApiKey);
        return await fixture.IngestClient.SendAsync(message);
    }
}
