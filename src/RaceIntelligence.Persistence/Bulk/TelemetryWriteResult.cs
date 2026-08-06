namespace RaceIntelligence.Persistence.Bulk;

/// <summary>The outcome of a bulk telemetry write. See <see cref="NpgsqlTelemetryWriter.WriteAsync"/>.</summary>
/// <param name="Inserted">Number of samples actually inserted as new rows.</param>
/// <param name="Duplicates">Number of samples skipped because their primary key already existed.</param>
public sealed record TelemetryWriteResult(int Inserted, int Duplicates);
