using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>Projects only the structural activity vocabulary committed by the Native model, including authored Flowchart.</summary>
internal static class KernelHistoryJournal
{
    internal const string Policy = "kernel-activity-journal-v1";
    internal const string Category = nameof(RuntimeHistoryEventCategory.ActivityLifecycle);
    private static readonly IReadOnlyDictionary<char, string> EventNames = new Dictionary<char, string>
    {
        ['b'] = "ActivityStarted",
        ['c'] = "ActivityCompleted",
        ['f'] = "ActivityFaulted",
        ['x'] = "ActivityCancelled"
    }.ToFrozenDictionary();

    internal static IReadOnlyList<string> SupportedKinds { get; } = EventNames.Values.Order(StringComparer.Ordinal).ToArray();

    internal static RuntimeHistoryEventView? Project(string run, long sequence, long revision, int ordinal, string entry, long recordedAt,
        ref DateTimeOffset? occurredAt)
    {
        using var document = JsonDocument.Parse(entry);
        if (document.RootElement.ValueKind != JsonValueKind.String ||
            document.RootElement.GetString() is not { Length: > 2 } text || text[1] != ':')
            return null;
        if (text[0] == 't')
        {
            // The foundation can prepend its own lifecycle entry before the model records its clock.
            if (occurredAt is null && long.TryParse(text.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
                occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            return null;
        }
        if (!EventNames.TryGetValue(text[0], out var name)) return null;

        var delimiter = text.IndexOf(':', 2);
        if (delimiter <= 2 || delimiter == text.Length - 1) return null;
        var activity = text[2..delimiter];
        var node = text[(delimiter + 1)..];
        string? parent = null;
        var ancestryKnown = text[0] == 'b';
        if (ancestryKnown)
        {
            // Begin includes a parent field, even for roots. The node itself can contain a colon.
            var parentDelimiter = node.LastIndexOf(':');
            if (parentDelimiter <= 0) return null;
            parent = parentDelimiter == node.Length - 1 ? null : node[(parentDelimiter + 1)..];
            node = node[..parentDelimiter];
        }

        var commit = RuntimeHistoryIdentity.CreateId(run, Policy, revision.ToString(CultureInfo.InvariantCulture));
        var eventId = RuntimeHistoryIdentity.CreateId(commit, name, ordinal.ToString(CultureInfo.InvariantCulture));
        return new(eventId, run, sequence, SchemaVersion: 1, name, Category,
            nameof(RuntimeHistoryAuthority.CommittedTransition), occurredAt, DateTimeOffset.FromUnixTimeMilliseconds(recordedAt),
            activity, node, CheckpointId: commit, CheckpointOrdinal: ordinal, PhysicalCommitId: commit,
            BookmarkId: null, IncidentId: null, AttemptId: null, ParentActivityExecutionId: parent,
            ExecutionScopeId: null, IterationId: null, Causation: null, FrozenDictionary<string, string>.Empty)
        {
            OccurredAtKnown = occurredAt.HasValue,
            ActivityAncestryKnown = ancestryKnown
        };
    }

    internal static bool Matches(RuntimeHistoryQuery query, RuntimeHistoryEventView item) =>
        (query.ActivityExecutionId is null || query.ActivityExecutionId == item.ActivityExecutionId ||
            query.IncludeDescendants && query.ActivityExecutionId == item.ParentActivityExecutionId) &&
        query.BookmarkId is null &&
        (query.Category is null or RuntimeHistoryEventCategory.ActivityLifecycle) &&
        (query.Name is null || query.Name == item.Name) &&
        (string.IsNullOrEmpty(query.Search) || item.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ||
            item.ActivityExecutionId?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true ||
            item.ExecutableNodeId?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true);
}
