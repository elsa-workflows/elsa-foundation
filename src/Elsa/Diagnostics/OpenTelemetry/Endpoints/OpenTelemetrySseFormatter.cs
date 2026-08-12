using System.Text;
using Elsa.Api.FastEndpoints.Sse;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints;

/// <summary>
/// Formats <see cref="OpenTelemetryStreamItem"/> values into Server-Sent Events frames. Pure string
/// production so the SSE wire contract (event / data framing) can be branch-tested without an HTTP host.
/// Each item carries a typed <c>event:</c> name (<c>resource</c>, <c>trace</c>, <c>metric</c>, <c>log</c>,
/// or <c>dropped</c>); items are not sequenced, so no <c>id:</c> / <c>Last-Event-ID</c> resume is offered.
/// </summary>
public sealed class OpenTelemetrySseFormatter : ISseStreamFormatter<OpenTelemetryStreamItem>
{
    private readonly OpenTelemetryStreamItemSerializer _serializer;

    public OpenTelemetrySseFormatter(OpenTelemetryStreamItemSerializer serializer) => _serializer = serializer;

    /// <summary>
    /// Formats a stream item into its typed SSE frame, dispatching on whichever payload the item carries.
    /// </summary>
    public string Format(OpenTelemetryStreamItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Resource is { } resource)
            return Frame("resource", _serializer.Serialize(resource));

        if (item.Trace is { } trace)
            return Frame("trace", _serializer.Serialize(trace));

        if (item.MetricPoint is { } point)
            return Frame("metric", _serializer.Serialize(point));

        if (item.Log is { } log)
            return Frame("log", _serializer.Serialize(log));

        if (item.DroppedItems is { } dropped)
            return Frame("dropped", _serializer.Serialize(dropped));

        throw new ArgumentException("Stream item carries no payload.", nameof(item));
    }

    /// <summary>Returns the SSE comment heartbeat that keeps idle connections open.</summary>
    public string Heartbeat() => ": keep-alive\n\n";

    private static string Frame(string eventName, string data)
    {
        var builder = new StringBuilder();
        builder.Append("event: ").Append(eventName).Append('\n');
        builder.Append("data: ").Append(data).Append("\n\n");
        return builder.ToString();
    }
}
