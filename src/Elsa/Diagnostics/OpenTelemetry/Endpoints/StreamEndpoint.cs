using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Extensions;
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
    OpenTelemetrySseStreamWriter streamWriter,
    IOptions<OpenTelemetryDiagnosticsOptions> options) : ElsaEndpointWithoutRequest
{
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
        response.StartServerSentEventStream();
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
        await streamWriter.StreamAsync(response, feed.SubscribeAsync(filter, ct), ct);
    }

    private static Dictionary<string, StringValues> BuildQuery(IQueryCollection query)
    {
        var result = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query)
            result[pair.Key] = pair.Value;
        return result;
    }
}
