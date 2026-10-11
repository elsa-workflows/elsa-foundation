using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>
/// Translates raw query-string values into a <see cref="StructuredLogFilter"/>, rejecting malformed input
/// with a domain exception so endpoints never leak raw parsing failures. Pure so it can be branch-tested.
/// </summary>
public sealed class StructuredLogFilterBinder
{
    /// <summary>
    /// Builds a filter from optional query values. <paramref name="take"/> is parsed for recent queries;
    /// invalid <paramref name="minLevel"/> or <paramref name="take"/> raise <see cref="InvalidLogQueryException"/>.
    /// </summary>
    public StructuredLogFilter Bind(string? minLevel, string? category, string? source, string? take)
    {
        return new StructuredLogFilter
        {
            MinimumLevel = ParseLevel(minLevel),
            Category = NormalizeText(category),
            SourceId = NormalizeText(source),
            MaxCount = ParseTake(take)
        };
    }

    private static LogLevel? ParseLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) && level != LogLevel.None)
            return level;

        throw new InvalidLogQueryException($"Invalid log level '{value}'.");
    }

    private static int? ParseTake(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, out var take) && take >= 0)
            return take;

        throw new InvalidLogQueryException($"Invalid 'take' value '{value}'.");
    }

    private static string? NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
