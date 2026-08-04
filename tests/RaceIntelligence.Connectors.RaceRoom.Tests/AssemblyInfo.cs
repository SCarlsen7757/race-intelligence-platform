using System.Runtime.Versioning;

// The connector under test is Windows-only (see its own AssemblyInfo.cs) because RaceRoom's
// shared-memory API only exists on Windows; this test project uses those types directly.
[assembly: SupportedOSPlatform("windows")]
