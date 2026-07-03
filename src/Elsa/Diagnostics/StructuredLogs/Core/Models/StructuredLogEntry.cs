using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// A single captured log event in a transport- and storage-neutral shape so the live feed, the recent
/// query, and any future persistence slice can all consume it without change.
/// </summary>
public sealed record StructuredLogEntry
{
    /// <summary>Monotonic per-host ordering id assigned by the store on append.</summary>
    public long Sequence { get; init; }

    /// <summary>When the event was emitted.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The severity of the event.</summary>
    public LogLevel Level { get; init; }

    /// <summary>The logger category name.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Optional numeric event id.</summary>
    public int? EventId { get; init; }

    /// <summary>Optional event name.</summary>
    public string? EventName { get; init; }

    /// <summary>The rendered (formatted) message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The original message template, when available.</summary>
    public string? MessageTemplate { get; init; }

    /// <summary>The bounded set of structured properties.</summary>
    public IReadOnlyList<LogProperty> Properties { get; init; } = [];

    /// <summary>The bounded chain of active scopes (outermost first).</summary>
    public IReadOnlyList<LogScope> Scopes { get; init; } = [];

    /// <summary>The captured exception, when the event carried one.</summary>
    public LogExceptionDetails? Exception { get; init; }

    /// <summary>The id of the originating <see cref="LogSource"/>.</summary>
    public string SourceId { get; init; } = string.Empty;
}
