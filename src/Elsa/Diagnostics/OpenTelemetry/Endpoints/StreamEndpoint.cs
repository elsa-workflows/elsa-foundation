using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints;

/// <summary>
/// Streams live telemetry (resources, traces, metric points, OTLP logs) to a client as Server-Sent
/// Events. Supports filtered subscriptions and periodic heartbeats. Thin adapter: SSE framing lives in
/// <see cref="OpenTelemetrySseFormatter"/>, query parsing in <see cref="OpenTelemetryTraceFilterBinder"/>,
/// and fan-out in <see cref="IOpenTelemetryLiveFeed"/>. The live feed carries no durable sequence, so this
/// endpoint offers no <c>Last-Event-ID</c> resume.
/// </summary>
internal sealed class StreamEndpoint(
    IOpenTelemetryLiveFeed feed,
    OpenTelemetryTraceFilterBinder binder,
    OpenTelemetrySseFormatter formatter,
    IOptions<OpenTelemetryDiagnosticsOptions> options) : ElsaEndpointWithoutRequest
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public override void Configure()
    {
        Get(options.Value.StreamPath);
        ConfigurePermissions(OpenTelemetryPermissions.Policy);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        OpenTelemetryTraceFilter filter;
        try
        {
            filter = binder.Bind(BuildQuery(HttpContext.Request.Query));
        }
        catch (InvalidTelemetryQueryException e)
        {
            await Send.StringAsync(e.Message, 400, cancellation: ct);
            return;
        }

        var response = HttpContext.Response;
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
        await response.Body.FlushAsync(ct);

        try
        {
            await StreamLiveAsync(response, filter, ct);
        }
        catch (OperationCanceledException)
        {
            // The client disconnected; the feed unsubscribe is handled when the enumerator is disposed.
        }
    }

    private async Task StreamLiveAsync(HttpResponse response, OpenTelemetryTraceFilter filter, CancellationToken ct)
    {
        await using var enumerator = feed.SubscribeAsync(filter, ct).GetAsyncEnumerator(ct);
        var moveNext = enumerator.MoveNextAsync().AsTask();

        while (true)
        {
            // Linked CTS so the heartbeat delay can be cancelled the moment an item wins the race,
            // releasing its timer instead of leaving it registered until it fires (no timer pile-up under load).
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var completed = await Task.WhenAny(moveNext, Task.Delay(HeartbeatInterval, heartbeatCts.Token));

            if (completed != moveNext)
            {
                await response.WriteAsync(formatter.Heartbeat(), ct);
                await response.Body.FlushAsync(ct);
                continue;
            }

            await heartbeatCts.CancelAsync();

            if (!await moveNext)
                break;

            await response.WriteAsync(formatter.Format(enumerator.Current), ct);
            await response.Body.FlushAsync(ct);
            moveNext = enumerator.MoveNextAsync().AsTask();
        }
    }

    private static Dictionary<string, StringValues> BuildQuery(IQueryCollection query)
    {
        var result = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query)
            result[pair.Key] = pair.Value;
        return result;
    }
}
