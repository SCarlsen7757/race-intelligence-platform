using System.Runtime.Versioning;

// RaceRoom Racing Experience, and the Windows named shared-memory ("$R3E") API this connector
// reads, only exist on Windows. Declaring that here is both accurate and lets the platform
// compatibility analyzer (CA1416) know that Windows-only BCL APIs (MemoryMappedFile.OpenExisting)
// used throughout this assembly are safe to call without per-call-site suppression.
[assembly: SupportedOSPlatform("windows")]
