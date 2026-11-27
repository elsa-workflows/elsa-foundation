using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>Projects only the structural activity vocabulary committed by the Native model, including authored Flowchart.</summary>
internal static class KernelHistoryJournal
{
    internal const string Policy = "kernel-activity-journal-v1";
    internal const string Category = nameof(RuntimeHistoryEventCategory.ActivityLifecycle);
    private static readonly IReadOnlyDictionary<NativeJournalKind, string> EventNames = new Dictionary<NativeJournalKind, string>
    {
        [NativeJournalKind.Started] = "ActivityStarted",
        [NativeJournalKind.Completed] = "ActivityCompleted",
        [NativeJournalKind.Faulted] = "ActivityFaulted",
        [NativeJournalKind.Cancelled] = "ActivityCancelled"
    }.ToFrozenDictionary();

    internal static IReadOnlyList<string> SupportedKinds { get; } = EventNames.Values.Order(StringComparer.Ordinal).ToArray();

    internal static RuntimeHistoryEventView? Project(string run, long sequence, long revision, int ordinal, string entry, long recordedAt)
    {
        using var document = JsonDocument.Parse(entry);
        if (!NativeStructuralJournal.Read(document.RootElement, out var record)) return null;
        if (record is null) throw new JsonException("The Native structural journal record could not be decoded.");
        if (!EventNames.TryGetValue(record.Kind, out var name)) return null;

        var commit = RuntimeHistoryIdentity.CreateId(run, Policy, revision.ToString(CultureInfo.InvariantCulture));
        var eventId = RuntimeHistoryIdentity.CreateId(commit, name, sequence.ToString(CultureInfo.InvariantCulture));
        return new(eventId, run, sequence, SchemaVersion: 1, name, Category,
            nameof(RuntimeHistoryAuthority.CommittedTransition), DateTimeOffset.FromUnixTimeMilliseconds(record.At),
            DateTimeOffset.FromUnixTimeMilliseconds(recordedAt), record.Frame, record.Node,
            CheckpointId: commit, CheckpointOrdinal: ordinal, PhysicalCommitId: commit,
            BookmarkId: null, IncidentId: null, AttemptId: null, ParentActivityExecutionId: record.Parent,
            ExecutionScopeId: null, IterationId: null, Causation: null, FrozenDictionary<string, string>.Empty)
        {
            OccurredAtKnown = true,
            ActivityAncestryKnown = true
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
