using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace RaceIntelligence.Ingest.Api.Auth;

/// <summary>
/// The rate limiter guarding the ingest endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <b>Request count alone is the wrong unit here.</b> A request to this API is either one lap
/// result or an eight-megabyte telemetry batch, so a limit that sounds generous in requests per
/// second permits an enormous amount of decoding and writing. The concurrency limiter is what
/// actually bounds the cost — a fixed number of in-flight batches is a bounded amount of memory and
/// database pressure no matter how fast they arrive — and the token buckets bound the arrival rate
/// on top of it.
/// </para>
/// <para>
/// <b>Sizing.</b> A collector flushes on a two-second batch age or 500 samples, whichever comes
/// first; at 60 Hz the age always wins, so a healthy collector makes about one request every two
/// seconds. Five per second sustained is an order of magnitude of headroom. The burst allowance
/// covers a reconnect: a full 20 000-sample buffer drains as roughly 40 back-to-back batches, which
/// must not be throttled — a 429 during recovery is precisely when one is least wanted.
/// </para>
/// <para>
/// Partitioned on the digest of the presented key rather than the label
/// <see cref="ApiKeyFilter"/> resolves, because this is middleware and runs before endpoint
/// filters. The per-remote-address bucket is chained underneath to close the gap that would
/// otherwise let a caller rotate fabricated keys for a fresh bucket each time.
/// </para>
/// <para>
/// <b>Behind the tunnel that address is the tunnel's</b>, not the collector's, because nothing in
/// this deployment applies forwarded headers. So the per-address bucket degrades from a per-client
/// limit to one aggregate cap shared by every remote collector. Accepted deliberately: the two
/// limiters above it still partition per collector, and two collectors at roughly one request
/// every two seconds sit far under the aggregate. What is lost is attribution — a flood of
/// fabricated keys from the internet shares a bucket with real collectors rather than being
/// isolated from them. Trusting <c>X-Forwarded-For</c> to recover it would need
/// <c>KnownProxies</c>/<c>KnownNetworks</c> pinned to an address Docker assigns dynamically, and a
/// forgeable partition key is worse than an aggregate one. See ADR 0003.
/// </para>
/// <para>
/// A chained limiter rather than a named policy, because chaining is expressible only on the
/// global limiter. The partitioners therefore exempt every path outside <c>/api/v1</c> themselves:
/// <c>/health</c> and <c>/alive</c> must never be throttled, or a limited probe reports the service
/// unhealthy and something restarts it mid-race.
/// </para>
/// </remarks>
public static class IngestRateLimiting
{
    private const string GuardedPrefix = "/api/v1";

    /// <summary>Registers the ingest rate limiter.</summary>
    public static IServiceCollection AddIngestRateLimiter(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsync("Too many requests.", ct).ConfigureAwait(false);
            };

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                // Bounds in-flight decode and COPY work. The queue lets a short burst wait rather
                // than fail, which is what a backlog drain needs.
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    Guarded(context, key => RateLimitPartition.GetConcurrencyLimiter(key, _ =>
                        new ConcurrencyLimiterOptions
                        {
                            PermitLimit = 4,
                            QueueLimit = 8,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        }))),
                // ~5 requests/second sustained per collector, 300 burst.
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    Guarded(context, key => RateLimitPartition.GetTokenBucketLimiter(key, _ =>
                        new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 300,
                            TokensPerPeriod = 60,
                            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        }))),
                // The backstop against fabricated keys, which would otherwise each get a fresh
                // bucket of their own.
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    Guarded(context, _ => RateLimitPartition.GetTokenBucketLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
                        new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 600,
                            TokensPerPeriod = 120,
                            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        }))));
        });

    /// <summary>
    /// Applies <paramref name="partition"/> to requests under <c>/api/v1</c>, and exempts
    /// everything else — the health endpoints above all.
    /// </summary>
    private static RateLimitPartition<string> Guarded(
        HttpContext context,
        Func<string, RateLimitPartition<string>> partition)
    {
        if (!context.Request.Path.StartsWithSegments(GuardedPrefix))
        {
            return RateLimitPartition.GetNoLimiter(string.Empty);
        }

        return partition(CollectorKeyGate.PartitionKey(
            context.Request.Headers[CollectorKeyGate.HeaderName].ToString()));
    }
}
