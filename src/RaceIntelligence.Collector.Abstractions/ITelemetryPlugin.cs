using Microsoft.Extensions.Hosting;

namespace RaceIntelligence.Collector.Abstractions;

/// <summary>
/// One destination the collector can send telemetry to, contributing its own services to the host.
/// </summary>
/// <remarks>
/// <para>
/// The collector reads the simulator and dispatches what it reads. Everything that <i>sends</i> data
/// anywhere — archiving it to the ingest API, publishing it to the live hub — is a plugin, so a
/// deployment can run any subset of them and adding a new destination costs a plugin rather than a
/// change to the collect loop.
/// </para>
/// <para>
/// <b>There is deliberately no uniform sink interface.</b> The archive path is buffered, ordered and
/// retried; a sample it drops is data lost forever. The live path is conflating and newest-wins; a
/// frame it keeps instead of dropping is a stale frame shown to a race engineer as current. Those
/// are opposite definitions of correct behaviour, and one interface spanning both would force one
/// path to adopt the other's failure mode. Plugins therefore share a <i>lifecycle</i> — this type —
/// and implement whichever of the observer interfaces they consume, each bringing its own delivery
/// semantics.
/// </para>
/// <para>
/// Equally deliberate: no <c>StartAsync</c>/<c>StopAsync</c> here. A plugin needing background work
/// registers an <see cref="IHostedService"/> in <see cref="Register"/>, which already has a
/// lifecycle the host orders correctly on shutdown. A second lifecycle alongside it would be two
/// shutdown orders to keep in agreement, and the interesting ordering rule — consumers stop after
/// the producer, so they can drain what is still pending — lives in that one.
/// </para>
/// </remarks>
public interface ITelemetryPlugin
{
    /// <summary>
    /// Stable identifier, used as the configuration sub-section under <c>Collector</c> and as the
    /// name on the command line (<c>--plugin ingest</c>). Lowercase, no spaces.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Adds this plugin's services to the host. Called only when the plugin is enabled, so an
    /// implementation never needs to check.
    /// </summary>
    /// <remarks>
    /// Register observer implementations against the interfaces this plugin consumes
    /// (<see cref="ISampleObserver"/> and friends) so the collect loop can resolve them, plus any
    /// <see cref="IHostedService"/> the plugin needs for background work.
    /// </remarks>
    void Register(IHostApplicationBuilder builder);
}
