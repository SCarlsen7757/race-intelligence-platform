using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RaceIntelligence.Collector.Plugins.Ingest;
using RaceIntelligence.Collector.Plugins.Ingest.Upload;
using Shouldly;

namespace RaceIntelligence.Collector.Plugins.Ingest.Tests;

/// <summary>
/// Covers what <see cref="IngestPlugin.Register"/> configures on the ingest <c>HttpClient</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Upload.IngestClientTests"/> cannot cover any of this: they construct an
/// <see cref="HttpClient"/> by hand around a fake handler, so they see nothing the DI registration
/// sets. That left the base address, the API key header and the HTTP version untested — the wiring
/// most likely to be silently wrong, because a mistake in it looks like a server problem.
/// </para>
/// </remarks>
public sealed class IngestPluginRegistrationTests
{
    private const string BaseUrl = "https://race-ingest.example.com/";
    private const string ApiKey = "test-collector-key";

    private static readonly KeyValuePair<string, string?>[] Settings =
    [
        new("Collector:Ingest:Enabled", "true"),
        new("Collector:Ingest:BaseUrl", BaseUrl),
        new("Collector:Ingest:ApiKey", ApiKey),
    ];

    /// <summary>
    /// Proves the resilience overrides actually reach the handler, rather than merely binding to a
    /// name nothing reads.
    /// </summary>
    /// <remarks>
    /// Asserting the options object back would prove nothing — it would read the same name this
    /// test wrote. So this drives a real request through the pipeline against a handler that never
    /// responds, and checks it is still going well after the framework's default 30-second total
    /// would have abandoned it. If the override bound to the wrong name, the default would win and
    /// the request would give up early.
    /// </remarks>
    [Fact]
    public async Task The_ingest_client_waits_longer_than_the_framework_default()
    {
        using var neverResponds = new BlockingHandler();

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(Settings);
        new IngestPlugin().Register(builder);
        builder.Services.AddHttpClient(nameof(IIngestClient))
            .ConfigurePrimaryHttpMessageHandler(() => neverResponds);

        var client = builder.Build().Services
            .GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IIngestClient));

        var request = client.GetAsync("api/v1/sessions", TestContext.Current.CancellationToken);
        var finishedEarly = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(35), TestContext.Current.CancellationToken));

        finishedEarly.ShouldNotBe(
            request,
            "the request gave up inside the framework's default 30s total, so the configured 120s never took effect.");
    }

    /// <summary>A handler that accepts a request and never answers it.</summary>
    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    private static HttpClient Client()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(Settings);

        new IngestPlugin().Register(builder);

        // The typed client's name is the interface name, which is what
        // AddHttpClient<TClient, TImplementation> registers it under.
        return builder.Build().Services
            .GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IIngestClient));
    }

    /// <summary>
    /// A remote collector points at a public origin rather than a LAN address, so the configured
    /// base address has to survive registration untouched — including its trailing slash, without
    /// which the relative request paths would replace the last segment rather than append to it.
    /// </summary>
    [Fact]
    public void The_configured_base_address_is_used() =>
        Client().BaseAddress.ShouldBe(new Uri(BaseUrl));

    [Fact]
    public void The_api_key_is_sent_as_a_header() =>
        Client().DefaultRequestHeaders.GetValues("X-Api-Key").ShouldHaveSingleItem().ShouldBe(ApiKey);

    /// <summary>
    /// HTTP/2 is requested so the tunnel leg negotiates h2 over TLS, and
    /// <see cref="HttpVersionPolicy.RequestVersionOrLower"/> so the cleartext LAN leg — which
    /// serves HTTP/1.1 only — downgrades instead of failing.
    /// </summary>
    [Fact]
    public void Http2_is_requested_but_not_required()
    {
        var client = Client();

        client.DefaultRequestVersion.ShouldBe(HttpVersion.Version20);
        client.DefaultVersionPolicy.ShouldBe(HttpVersionPolicy.RequestVersionOrLower);
    }
}
