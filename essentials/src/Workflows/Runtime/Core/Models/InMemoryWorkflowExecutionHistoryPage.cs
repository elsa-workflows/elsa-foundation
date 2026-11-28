using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Canonical bounded history paging for snapshots and in-memory state.</summary>
internal static class InMemoryWorkflowExecutionHistoryPage
{
    internal static WorkflowExecutionStatePage Read(
        IEnumerable<WorkflowExecutionState> states,
        WorkflowExecutionStatePageQuery query,
        string? cursorScope = null)
    {
        var comparer = Comparer<WorkflowExecutionState>.Create(WorkflowExecutionStateHistory.Compare);
        var candidates = new SortedSet<WorkflowExecutionState>(comparer);
        var cursor = query.Cursor is null ? null : DecodeCursor(query.Cursor, query, cursorScope);
        var limit = checked(query.PageSize + 1);
        long total = 0;

        foreach (var state in states)
        {
            if (!WorkflowExecutionStateHistory.Matches(state, query))
                continue;

            total++;
            if (cursor is not null)
            {
                var timestamp = cursor.SortTimestamp.CompareTo(WorkflowExecutionStateHistory.SortTimestamp(state));
                var comparison = timestamp != 0
                    ? timestamp
                    : StringComparer.Ordinal.Compare(state.WorkflowExecutionId, cursor.WorkflowExecutionId);
                if (comparison <= 0)
                    continue;
            }

            candidates.Add(state);
            if (candidates.Count <= limit)
                continue;
            candidates.Remove(candidates.Max!);
        }

        var hasExtra = candidates.Count > query.PageSize;
        if (hasExtra)
            candidates.Remove(candidates.Max!);

        var items = candidates.ToArray();
        return new(
            items,
            hasExtra && items.Length > 0
                ? EncodeCursor(items[^1], query, cursorScope)
                : null,
            hasExtra,
            total);
    }

    private static string EncodeCursor(
        WorkflowExecutionState state,
        WorkflowExecutionStatePageQuery query, string? cursorScope)
    {
        var value = string.Join(
            '|',
            "v1",
            WorkflowExecutionStateHistory.SortTimestamp(state).UtcTicks.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(state.WorkflowExecutionId)),
            PageCursorScope(query, cursorScope));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static InMemoryCursor DecodeCursor(
        string cursor,
        WorkflowExecutionStatePageQuery query, string? cursorScope)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|');
            if (parts.Length != 4 ||
                parts[0] != "v1" ||
                !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !StringComparer.Ordinal.Equals(parts[3], PageCursorScope(query, cursorScope)))
            {
                throw new FormatException();
            }

            return new(
                new DateTimeOffset(ticks, TimeSpan.Zero),
                Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException(
                "The workflow execution history cursor is invalid or does not belong to this query.",
                nameof(cursor),
                exception);
        }
    }

    private static string PageCursorScope(WorkflowExecutionStatePageQuery query, string? cursorScope)
    {
        var scope = WorkflowExecutionStateHistory.Scope(query);
        return cursorScope is null
            ? scope
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}:{cursorScope}")));
    }

    private sealed record InMemoryCursor(DateTimeOffset SortTimestamp, string WorkflowExecutionId);
}
