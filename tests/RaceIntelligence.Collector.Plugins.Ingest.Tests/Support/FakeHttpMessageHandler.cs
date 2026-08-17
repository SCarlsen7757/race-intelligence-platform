using System.Net;

namespace RaceIntelligence.Collector.Plugins.Ingest.Tests.Support;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that records every request it sees and answers with a
/// caller-supplied responder, for driving <see cref="Collector.Upload.IngestClient"/> without a
/// real network call.
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Creates a handler that always returns <paramref name="statusCode"/> with an empty body.</summary>
    public static FakeHttpMessageHandler ReturningStatus(HttpStatusCode statusCode) =>
        new(_ => new HttpResponseMessage(statusCode));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
