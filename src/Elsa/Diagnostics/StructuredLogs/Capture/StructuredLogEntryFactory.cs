using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Capture;

/// <summary>
/// Maps a single <c>ILogger.Log</c> call into a <see cref="StructuredLogEntry"/>, applying the configured
/// capture caps. Capture must never throw into or recurse through the host logging path, so the owning
/// provider ignores its own category and swallows sink failures.
/// </summary>
internal static class StructuredLogEntryFactory
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    public static StructuredLogEntry Create<TState>(
        StructuredLogsOptions options,
        string sourceId,
        string category,
        LogLevel level,
        EventId eventId,
        TState state,
        Exception? exception,
        string message,
        IExternalScopeProvider? scopeProvider)
    {
        var (properties, template) = ExtractProperties(state, options);
        var scopes = ExtractScopes(scopeProvider, options);
        var exceptionInfo = MapException(exception, options.MaxCapturedScopeDepth);

        return new StructuredLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = level,
            Category = category,
            EventId = eventId.Id == 0 ? null : eventId.Id,
            EventName = eventId.Name,
            Message = message,
            MessageTemplate = template,
            Properties = properties,
            Scopes = scopes,
            Exception = exceptionInfo,
            SourceId = sourceId
        };
    }

    private static (IReadOnlyList<LogProperty> Properties, string? Template) ExtractProperties<TState>(
        TState state,
        StructuredLogsOptions options)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> pairs)
            return ([], null);

        string? template = null;
        var properties = new List<LogProperty>(Math.Min(pairs.Count, options.MaxCapturedProperties));

        foreach (var pair in pairs)
        {
            if (string.Equals(pair.Key, OriginalFormatKey, StringComparison.Ordinal))
            {
                template = pair.Value?.ToString();
                continue;
            }

            if (properties.Count >= options.MaxCapturedProperties)
                continue;

            properties.Add(new LogProperty(pair.Key, Truncate(pair.Value?.ToString(), options.MaxPropertyValueLength)));
        }

        return (properties, template);
    }

    private static IReadOnlyList<LogScope> ExtractScopes(IExternalScopeProvider? scopeProvider, StructuredLogsOptions options)
    {
        if (scopeProvider is null)
            return [];

        var scopes = new List<LogScope>();
        scopeProvider.ForEachScope((scope, accumulator) =>
        {
            if (accumulator.Count >= options.MaxCapturedScopeDepth)
                return;

            if (scope is IReadOnlyList<KeyValuePair<string, object?>> keyed)
            {
                var items = new List<LogProperty>(keyed.Count);
                foreach (var pair in keyed)
                {
                    if (string.Equals(pair.Key, OriginalFormatKey, StringComparison.Ordinal))
                        continue;
                    items.Add(new LogProperty(pair.Key, Truncate(pair.Value?.ToString(), options.MaxPropertyValueLength)));
                }

                accumulator.Add(new LogScope(items));
            }
            else
            {
                accumulator.Add(new LogScope([], Truncate(scope?.ToString(), options.MaxPropertyValueLength)));
            }
        }, scopes);

        return scopes;
    }

    private static LogExceptionInfo? MapException(Exception? exception, int maxDepth)
    {
        if (exception is null)
            return null;

        return Map(exception, depth: 0, maxDepth);

        static LogExceptionInfo? Map(Exception? ex, int depth, int maxDepth)
        {
            if (ex is null || depth >= maxDepth)
                return null;

            return new LogExceptionInfo(
                ex.GetType().FullName ?? ex.GetType().Name,
                ex.Message,
                ex.StackTrace,
                Map(ex.InnerException, depth + 1, maxDepth));
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
