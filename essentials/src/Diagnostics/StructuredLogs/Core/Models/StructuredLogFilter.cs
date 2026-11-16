using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// Selection criteria applied to both recent-history queries and live subscriptions. <see cref="Matches"/>
/// is pure so it can be branch-tested independently of any store.
/// </summary>
public sealed record StructuredLogFilter
{
    /// <summary>Only entries at or above this level match. Null means no level constraint.</summary>
    public LogLevel? MinimumLevel { get; init; }

    /// <summary>Only entries whose category equals this value match. Null means no category constraint.</summary>
    public string? Category { get; init; }

    /// <summary>Only entries from this source match. Null means no source constraint.</summary>
    public string? SourceId { get; init; }

    /// <summary>Upper bound for a recent query; ignored by the live feed.</summary>
    public int? MaxCount { get; init; }

    /// <summary>An empty filter that matches every entry.</summary>
    public static StructuredLogFilter None { get; } = new();

    /// <summary>
    /// Returns true when <paramref name="entry"/> satisfies every present criterion. Absent criteria do
    /// not constrain the result.
    /// </summary>
    public bool Matches(StructuredLogEntry entry)
    {
        if (MinimumLevel is { } minimumLevel && entry.Level < minimumLevel)
            return false;

        if (!string.IsNullOrEmpty(Category) && !string.Equals(entry.Category, Category, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(SourceId) && !string.Equals(entry.SourceId, SourceId, StringComparison.Ordinal))
            return false;

        return true;
    }
}
