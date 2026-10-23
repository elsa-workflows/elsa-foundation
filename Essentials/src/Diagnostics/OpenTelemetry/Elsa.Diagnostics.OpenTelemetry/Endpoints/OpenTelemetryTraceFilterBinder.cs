using System.Globalization;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Microsoft.Extensions.Primitives;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints;

/// <summary>
/// Translates raw query-string values into an <see cref="OpenTelemetryTraceFilter"/> for the live-stream
/// subscription, rejecting malformed input with <see cref="InvalidTelemetryQueryException"/> so endpoints
/// never leak raw parsing failures. Pure so it can be branch-tested.
/// </summary>
public sealed class OpenTelemetryTraceFilterBinder
{
    /// <summary>
    /// Builds a trace filter from an HTTP query collection. Recognised keys: <c>traceId</c>,
    /// <c>serviceName</c>, <c>resourceId</c>, <c>workflowInstanceId</c>, <c>status</c>, <c>from</c>,
    /// <c>to</c>, <c>search</c>, <c>take</c>. Unknown keys are ignored.
    /// </summary>
    public OpenTelemetryTraceFilter Bind(IReadOnlyDictionary<string, StringValues> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var from = ParseTimestamp(Get(query, "from"), "from");
        var to = ParseTimestamp(Get(query, "to"), "to");

        if (from is { } f && to is { } t && f > t)
            throw new InvalidTelemetryQueryException("The OpenTelemetry filter 'from' timestamp must be earlier than or equal to 'to'.");

        return new OpenTelemetryTraceFilter
        {
            TraceId = Normalize(Get(query, "traceId")),
            ServiceName = Normalize(Get(query, "serviceName")),
            ResourceId = Normalize(Get(query, "resourceId")),
            WorkflowInstanceId = Normalize(Get(query, "workflowInstanceId")),
            Status = ParseStatus(Get(query, "status")),
            From = from,
            To = to,
            Search = Normalize(Get(query, "search")),
            Take = ParseTake(Get(query, "take"))
        };
    }

    private static string? Get(IReadOnlyDictionary<string, StringValues> query, string key) =>
        query.TryGetValue(key, out var value) ? value.ToString() : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static SpanStatus? ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Enum.TryParse<SpanStatus>(value, ignoreCase: true, out var status))
            return status;

        throw new InvalidTelemetryQueryException($"Invalid span status '{value}'.");
    }

    private static DateTimeOffset? ParseTimestamp(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;

        throw new InvalidTelemetryQueryException($"Invalid '{name}' timestamp '{value}'.");
    }

    private static int? ParseTake(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var take) && take >= 0)
            return take;

        throw new InvalidTelemetryQueryException($"Invalid 'take' value '{value}'.");
    }
}
