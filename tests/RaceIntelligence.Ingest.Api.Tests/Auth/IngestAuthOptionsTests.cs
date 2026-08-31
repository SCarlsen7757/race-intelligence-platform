using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RaceIntelligence.Ingest.Api.Auth;
using Shouldly;

namespace RaceIntelligence.Ingest.Api.Tests.Auth;

/// <summary>
/// Covers how <c>Ingest:ApiKeys</c> arrives from configuration.
/// </summary>
/// <remarks>
/// The binding is the part most likely to break silently: AppHost and compose both set these as
/// environment variables (<c>Ingest__ApiKeys__local-collector</c>), and a map that failed to bind
/// would leave the gate with no keys and reject every collector with a 401 that looks like a client
/// problem.
/// </remarks>
public sealed class IngestAuthOptionsTests
{
    private static IOptions<IngestAuthOptions> Bind(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<IngestAuthOptions>()
            .Bind(configuration.GetSection(IngestAuthOptions.SectionName))
            .ValidateDataAnnotations();

        return services.BuildServiceProvider().GetRequiredService<IOptions<IngestAuthOptions>>();
    }

    /// <summary>
    /// The shape AppHost and compose actually set, with <c>__</c> as the separator.
    /// </summary>
    [Fact]
    public void Environment_style_keys_bind_into_the_map()
    {
        var options = Bind(
            ("Ingest:ApiKeys:local-collector", "first-key"),
            ("Ingest:ApiKeys:friend-gaming-pc", "second-key"));

        options.Value.ApiKeys.Count.ShouldBe(2);
        options.Value.ApiKeys["local-collector"].ShouldBe("first-key");
        options.Value.ApiKeys["friend-gaming-pc"].ShouldBe("second-key");
    }

    /// <summary>
    /// An ingest API with no keys can accept nothing, so it must refuse to start rather than reject
    /// every upload with something that looks like a collector's fault.
    /// </summary>
    [Fact]
    public void No_configured_keys_fails_validation()
    {
        var failure = Should.Throw<OptionsValidationException>(
            () => Bind(("Ingest:GameKey", "raceroom")).Value);

        failure.Message.ShouldContain("Ingest:ApiKeys");
    }
}
