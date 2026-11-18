using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// Spec 1229 (S-6): the variable lifecycle read model, derived when read from what spec 1221 committed (Q2): the write
/// records on each writer's activity execution, the lifecycle stamps with their commit sequences, and the alteration
/// write records. It orders every write by (commit sequence, position in the commit), marks the applied writes and the
/// current value (research R-8), links accepted overrides to the refused writes they resolve, marks concurrent
/// overwrites from scheduling provenance (research R-3), and builds the commit-ordered journal and the run-time paths
/// two instances are compared by (research R-5). It holds no values: value states are the service's.
/// </summary>
internal sealed class VariableLifecycleInspection
{
    private readonly Dictionary<string, ActivityExecutionState> _rows;
    private readonly Dictionary<string, List<ActivityExecutionState>> _children;
    private readonly Dictionary<string, string> _runTimePaths = new(StringComparer.Ordinal);
    private Dictionary<string, int>? _occurrences;
    private Dictionary<string, string>? _framePaths;

    private VariableLifecycleInspection(
        IReadOnlyList<ActivityExecutionState> rows,
        IReadOnlyList<VariableWriteEntry> entries,
        bool historyRecorded)
    {
        _rows = rows
            .GroupBy(row => row.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        _children = rows
            .Where(row => row.ParentActivityExecutionId is not null)
            .GroupBy(row => row.ParentActivityExecutionId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        Entries = entries;
        HistoryRecorded = historyRecorded;
    }

    /// <summary>Every write entry of the instance, in commit order.</summary>
    public IReadOnlyList<VariableWriteEntry> Entries { get; }

    /// <summary>False for an instance committed before write history existed: nothing it shows is a recorded absence.</summary>
    public bool HistoryRecorded { get; }

    public IReadOnlyCollection<ActivityExecutionState> Rows => _rows.Values;

    public ActivityExecutionState? FindRow(string activityExecutionId) => _rows.GetValueOrDefault(activityExecutionId);

    /// <summary>
    /// Builds the model. <paramref name="currentVersions"/> holds each variable instance's current version, keyed by
    /// <see cref="VariableWriteEntry.InstanceKeyOf"/>; without one, the last applied entry is the current value.
    /// </summary>
    public static VariableLifecycleInspection Build(
        IReadOnlyList<ActivityExecutionState> rows,
        IReadOnlyList<WorkflowAlterationJobState> alterationJobs,
        IReadOnlyDictionary<string, long>? currentVersions = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(alterationJobs);
        var entries = new List<VariableWriteEntry>();
        foreach (var row in rows)
        {
            var writes = row.VariableWrites ?? [];
            for (var index = 0; index < writes.Count; index++)
                if (IsListed(writes[index]))
                    entries.Add(new VariableWriteEntry(VariableWriteEntryId.ForActivity(row.Execution.ActivityExecutionId, index), writes[index], row, null));
        }

        foreach (var job in alterationJobs)
        foreach (var outcome in job.Outcomes)
        {
            var writes = outcome.VariableWrites ?? [];
            for (var index = 0; index < writes.Count; index++)
                if (IsListed(writes[index]))
                    entries.Add(new VariableWriteEntry(VariableWriteEntryId.ForAlteration(job.JobId, outcome.Ordinal, index), writes[index], null, job.JobId));
        }

        // Order: records without a commit sequence first (approximate, by frame revision and time), then by commit
        // sequence and, within a commit, by frame revision, time and entry.
        var ordered = entries
            .OrderBy(entry => entry.CommitSequence is null ? 0 : 1)
            .ThenBy(entry => entry.CommitSequence ?? 0)
            .ThenBy(entry => entry.Record.CommitOrder)
            .ThenBy(entry => entry.Record.OccurredAt.UtcTicks)
            .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
            .ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var entry = ordered[index];
            entry.Ordinal = index;
            entry.PositionInCommit = index > 0 && ordered[index - 1].CommitSequence == entry.CommitSequence ? ordered[index - 1].PositionInCommit + 1 : 0;
        }

        var historyRecorded = ordered.Count > 0 || rows.Any(row => row.Transitions is { Count: > 0 });
        var model = new VariableLifecycleInspection(rows, ordered, historyRecorded);
        model.MarkCurrentValues(currentVersions);
        model.LinkAcceptedOverrides();
        model.MarkConcurrentOverwrites();
        return model;
    }

    // FR-004: a write that changed nothing is not listed, unless it was judged a lost update (R-8).
    private static bool IsListed(VariableWriteRecord record) =>
        record.NewVersion != record.CurrentVersion || record.IsLostUpdate || record.Outcome == VariableCommitOutcome.AcceptedOverride;

    private void MarkCurrentValues(IReadOnlyDictionary<string, long>? currentVersions)
    {
        foreach (var group in Entries.Where(entry => entry.IsApplied).GroupBy(entry => entry.InstanceKey, StringComparer.Ordinal))
        {
            var applied = group.ToList();
            var current = currentVersions is not null && currentVersions.TryGetValue(group.Key, out var version)
                ? applied.LastOrDefault(entry => entry.Record.NewVersion == version)
                : applied[^1];
            if (current is not null)
                current.IsCurrentValue = true;
        }
    }

    // FR-004a: the successor that accepted a recorded completion names the faulted row it replaced; its accepted write
    // resolves that row's refused write to the same variable instance.
    private void LinkAcceptedOverrides()
    {
        foreach (var entry in Entries.Where(entry => entry.Record.Outcome == VariableCommitOutcome.AcceptedOverride && entry.Row is not null))
        {
            if (!entry.Row!.Metadata.TryGetValue(RuntimeMetadataKeys.RetryOfActivityExecutionId, out var faultedId))
                continue;
            entry.ResolvesEntry = Entries.LastOrDefault(candidate =>
                candidate.Row is not null &&
                StringComparer.Ordinal.Equals(candidate.Row.Execution.ActivityExecutionId, faultedId) &&
                candidate.Record.Outcome == VariableCommitOutcome.LostUpdateFault &&
                StringComparer.Ordinal.Equals(candidate.InstanceKey, entry.InstanceKey));
        }
    }

    // Research R-3: an applied write W2 that replaced the previous applied write W1 of the same variable instance is a
    // concurrent overwrite when W1 does not happen before W2's invocation. Writes of one logical invocation never are.
    private void MarkConcurrentOverwrites()
    {
        foreach (var group in Entries.Where(entry => entry.IsApplied).GroupBy(entry => entry.InstanceKey, StringComparer.Ordinal))
        {
            VariableWriteEntry? previous = null;
            foreach (var entry in group)
            {
                if (previous is not null && IsConcurrentOverwrite(previous, entry))
                    entry.Overwrote = previous;
                previous = entry;
            }
        }
    }

    private bool IsConcurrentOverwrite(VariableWriteEntry replaced, VariableWriteEntry writer)
    {
        // An operator's change acts on the state the operator inspected; it is listed, not traced as an overwrite.
        if (writer.Row is null)
            return false;
        if (StringComparer.Ordinal.Equals(replaced.LogicalInvocationId, writer.LogicalInvocationId))
            return false;
        // Without commit sequences the order is approximate, so nothing is claimed.
        if (replaced.CommitSequence is not { } replacedCommit || writer.CommitSequence is null)
            return false;
        return !HappensBefore(replaced, replacedCommit, writer.Row, writer.CommitSequence.Value);
    }

    /// <summary>
    /// Whether the write <paramref name="replaced"/>, committed at <paramref name="commit"/>, happens before the invocation
    /// of <paramref name="writerRow"/>: walking back from its scheduling over the scheduler and the scheduling causes,
    /// and from a completed container to the children that completed before it, a commit of the writer of
    /// <paramref name="replaced"/> at or after <paramref name="commit"/> is reached. The walk is bounded by
    /// <paramref name="commit"/>: an edge committed before it cannot carry the write.
    /// </summary>
    internal bool HappensBefore(VariableWriteEntry replaced, long commit, ActivityExecutionState writerRow, long writeCommit)
    {
        var reached = new Dictionary<string, long>(StringComparer.Ordinal);
        var pending = new Stack<(string RowId, long Bound)>();
        if (ScheduledCommitOf(writerRow) is not { } start)
            return false;
        // A container writes after the children that ended before its write: those ends come before it.
        if (_children.TryGetValue(writerRow.Execution.ActivityExecutionId, out var ownChildren))
            foreach (var child in ownChildren)
                if (EndCommitOf(child) is { } childEnded && childEnded <= writeCommit)
                    pending.Push((child.Execution.ActivityExecutionId, childEnded));
        // An alteration happens before an invocation an operator scheduled at or after it (a re-execution).
        if (replaced.Row is null && IsAlterationScheduled(writerRow) && start >= commit)
            return true;
        PushSchedulingEdges(writerRow, start, pending);
        while (pending.Count > 0)
        {
            var (rowId, bound) = pending.Pop();
            if (bound < commit || reached.TryGetValue(rowId, out var seen) && seen >= bound)
                continue;
            reached[rowId] = bound;
            if (!_rows.TryGetValue(rowId, out var row))
                continue;

            if (replaced.Row is not null && StringComparer.Ordinal.Equals(row.LogicalInvocationId, replaced.LogicalInvocationId))
                return true;
            if (replaced.Row is null && IsAlterationScheduled(row) && ScheduledCommitOf(row) >= commit)
                return true;

            if (ScheduledCommitOf(row) is { } scheduled && scheduled <= bound)
                PushSchedulingEdges(row, scheduled, pending);

            // A container's completion comes after the children that completed before it (a join, a Parallel).
            if (EndCommitOf(row) is { } ended && ended <= bound && _children.TryGetValue(rowId, out var children))
                foreach (var child in children)
                    if (EndCommitOf(child) is { } childEnded && childEnded <= ended)
                        pending.Push((child.Execution.ActivityExecutionId, childEnded));
        }

        return false;
    }

    private static void PushSchedulingEdges(ActivityExecutionState row, long bound, Stack<(string, long)> pending)
    {
        var scheduler = row.Provenance.SchedulingActivityExecutionId ?? row.SchedulingActivityExecutionId;
        if (!string.IsNullOrWhiteSpace(scheduler))
            pending.Push((scheduler, bound));
        foreach (var cause in row.Provenance.CausedBy ?? [])
            pending.Push((cause, bound));
        // An operator's retry continues the faulted row it replaced.
        if (row.Metadata.TryGetValue(RuntimeMetadataKeys.RetryOfActivityExecutionId, out var retried) && !string.IsNullOrWhiteSpace(retried))
            pending.Push((retried, bound));
    }

    private static bool IsAlterationScheduled(ActivityExecutionState row) =>
        row.Metadata.ContainsKey(RuntimeMetadataKeys.RetryOfActivityExecutionId) ||
        row.Provenance.SchedulingCause?.Contains("alteration", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The commit that scheduled the row: its Scheduled stamp, else its first stamp, else its range start.</summary>
    public static long? ScheduledCommitOf(ActivityExecutionState row) =>
        row.Transitions?.FirstOrDefault(stamp => stamp.Kind == ActivityLifecycleTransitionKind.Scheduled)?.CommitSequence
        ?? row.Transitions?.FirstOrDefault()?.CommitSequence
        ?? row.FirstCommitSequence;

    /// <summary>The commit that ended the row (completed, faulted, cancelled), when it ended.</summary>
    public static long? EndCommitOf(ActivityExecutionState row) =>
        row.Transitions?.LastOrDefault(stamp => stamp.Kind is ActivityLifecycleTransitionKind.Completed or ActivityLifecycleTransitionKind.Faulted or ActivityLifecycleTransitionKind.Cancelled)?.CommitSequence;

    /// <summary>
    /// Research R-5: the row's run-time path, the executable node ids from the root down to the row, each with its
    /// occurrence under its parent row (the rows of that node under the same parent, in order of their scheduling).
    /// It is the same in two instances that took the same path, whatever their row ids.
    /// </summary>
    public string RunTimePathOf(ActivityExecutionState row)
    {
        var id = row.Execution.ActivityExecutionId;
        if (_runTimePaths.TryGetValue(id, out var cached))
            return cached;
        var occurrence = OccurrenceOf(row);
        var segment = string.Create(CultureInfo.InvariantCulture, $"{row.Execution.ExecutableNodeId}[{occurrence}]");
        var path = row.ParentActivityExecutionId is { } parent && _rows.TryGetValue(parent, out var parentRow) && !ReferenceEquals(parentRow, row)
            ? $"{RunTimePathOf(parentRow)}/{segment}"
            : segment;
        _runTimePaths[id] = path;
        return path;
    }

    // Occurrences are numbered once per sibling group: the rows of one node under one parent, in scheduling order.
    private int OccurrenceOf(ActivityExecutionState row)
    {
        if (_occurrences is null)
        {
            _occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var groups = _rows.Values.GroupBy(candidate => (
                Parent: candidate.ParentActivityExecutionId is { } parent && _rows.ContainsKey(parent) ? parent : string.Empty,
                Node: candidate.Execution.ExecutableNodeId));
            foreach (var group in groups)
            {
                var index = 0;
                foreach (var sibling in group
                             .OrderBy(sibling => ScheduledCommitOf(sibling) ?? long.MaxValue)
                             .ThenBy(sibling => sibling.ExecutionSequence)
                             .ThenBy(sibling => sibling.ScheduledAt)
                             .ThenBy(sibling => sibling.Execution.ActivityExecutionId, StringComparer.Ordinal))
                    _occurrences[sibling.Execution.ActivityExecutionId] = index++;
            }
        }

        return _occurrences.GetValueOrDefault(row.Execution.ActivityExecutionId);
    }

    /// <summary>The run-time path of the frame a variable instance lives in, so two instances' variables can be matched.</summary>
    public string FramePathOf(VariableWriteEntry entry)
    {
        var frameId = entry.Record.FrameId;
        if (frameId is null)
            return $"scope:{entry.Record.ScopeId}";
        if (_framePaths is null)
        {
            _framePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in _rows.Values)
            {
                if (row.VariableFrame?.FrameId is { } container)
                    _framePaths.TryAdd(container, RunTimePathOf(row));
                if (row.IterationVariableFrame?.FrameId is { } iteration)
                    _framePaths.TryAdd(iteration, $"{RunTimePathOf(row)}#iteration");
                _framePaths.TryAdd(Elsa.Workflows.Runtime.Services.Values.RuntimeClosedScope.FrameId(row.Execution.ActivityExecutionId), $"{RunTimePathOf(row)}#scope");
            }
        }

        // The root frame and frames of rows no longer stored are matched by scope.
        return _framePaths.TryGetValue(frameId, out var path) ? path : $"scope:{entry.Record.ScopeId}";
    }

    /// <summary>
    /// The journal (FR-010): one commit per commit sequence, in order, with its transitions, input refreshes and writes.
    /// Records without a commit sequence form one approximate commit before the others.
    /// </summary>
    public IReadOnlyList<JournalCommit> BuildJournal()
    {
        var commits = new SortedDictionary<long, JournalCommit>();
        JournalCommit? approximate = null;

        JournalCommit CommitFor(long? sequence) => sequence is { } value
            ? commits.TryGetValue(value, out var existing) ? existing : commits[value] = new JournalCommit(value)
            : approximate ??= new JournalCommit(null);

        foreach (var row in _rows.Values)
        {
            foreach (var stamp in row.Transitions ?? [])
                CommitFor(stamp.CommitSequence).Transitions.Add((row, stamp));
            foreach (var refresh in row.InputRefreshes ?? [])
            {
                var stamp = row.Transitions?.FirstOrDefault(candidate => candidate.Evaluation == refresh.ReplacedByEvaluation)
                            ?? row.Transitions?.FirstOrDefault(candidate => candidate.Kind == ActivityLifecycleTransitionKind.Started);
                CommitFor(stamp?.CommitSequence).InputRefreshes.Add((row, refresh));
            }
        }

        // FR-010: a commit lists the writes it applied; refused and equal-value lost updates stay in the write history.
        foreach (var entry in Entries.Where(entry => entry.IsApplied))
            CommitFor(entry.CommitSequence).Writes.Add(entry);

        var result = new List<JournalCommit>(commits.Count + 1);
        if (approximate is not null)
            result.Add(approximate);
        result.AddRange(commits.Values);
        foreach (var commit in result)
            commit.Transitions.Sort((left, right) =>
            {
                var bySequence = left.Row.ExecutionSequence.CompareTo(right.Row.ExecutionSequence);
                return bySequence != 0 ? bySequence : StringComparer.Ordinal.Compare(left.Row.Execution.ActivityExecutionId, right.Row.Execution.ActivityExecutionId);
            });
        return result;
    }

    /// <summary>The signature of a journal commit two instances are compared by: its transitions by run-time path.</summary>
    public string SignatureOf(JournalCommit commit) =>
        string.Join('\n', commit.Transitions
            .Select(transition => $"{RunTimePathOf(transition.Row)}:{transition.Stamp.Kind}")
            .Order(StringComparer.Ordinal));
}

/// <summary>One journal commit: the transitions, input refreshes and writes one commit sequence committed.</summary>
internal sealed class JournalCommit(long? commitSequence)
{
    public long? CommitSequence { get; } = commitSequence;
    public List<(ActivityExecutionState Row, ActivityLifecycleStamp Stamp)> Transitions { get; } = [];
    public List<(ActivityExecutionState Row, ActivityInputRefresh Refresh)> InputRefreshes { get; } = [];
    public List<VariableWriteEntry> Writes { get; } = [];
}

/// <summary>One write-history entry: a write record of an activity execution or of an alteration job outcome.</summary>
internal sealed class VariableWriteEntry(string entryId, VariableWriteRecord record, ActivityExecutionState? row, string? alterationJobId)
{
    public string EntryId { get; } = entryId;
    public VariableWriteRecord Record { get; } = record;

    /// <summary>The writer's row; null for an alteration.</summary>
    public ActivityExecutionState? Row { get; } = row;

    public string? AlterationJobId { get; } = alterationJobId;
    public long? CommitSequence => Record.CommitSequence is >= 0 and var sequence ? sequence : null;
    public bool OrderApproximate => CommitSequence is null;
    public string InstanceKey { get; } = InstanceKeyOf(record.FrameId, record.ScopeId, record.VariableKey);

    /// <summary>The writer's logical invocation (ADR 0092 decision 8); an alteration is its own.</summary>
    public string LogicalInvocationId => Row?.LogicalInvocationId ?? $"alteration:{AlterationJobId}";

    /// <summary>Research R-8: the write changed the version. A refused <c>Fault</c> write never applied.</summary>
    public bool IsApplied => Record.Outcome != VariableCommitOutcome.LostUpdateFault && Record.NewVersion != Record.CurrentVersion;

    public int Ordinal { get; set; }
    public int PositionInCommit { get; set; }
    public bool IsCurrentValue { get; set; }
    public VariableWriteEntry? Overwrote { get; set; }
    public VariableWriteEntry? ResolvesEntry { get; set; }

    /// <summary>A variable instance: its frame and key, or, for a record without a frame, its scope and key.</summary>
    public static string InstanceKeyOf(string? frameId, string scopeId, string variableKey) =>
        frameId is not null ? $"frame\u001f{frameId}\u001f{variableKey}" : $"scope\u001f{scopeId}\u001f{variableKey}";
}

/// <summary>Opaque, route-safe write-history entry ids: the owner (activity execution or alteration) and the record.</summary>
internal static class VariableWriteEntryId
{
    private const char Separator = '\u001f';

    public static string ForActivity(string activityExecutionId, int index) => Encode($"a{Separator}{activityExecutionId}{Separator}{index.ToString(CultureInfo.InvariantCulture)}");

    public static string ForAlteration(string jobId, int ordinal, int index) =>
        Encode($"j{Separator}{jobId}{Separator}{ordinal.ToString(CultureInfo.InvariantCulture)}{Separator}{index.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Decodes an entry id; null when it is not one.</summary>
    public static (string Kind, string OwnerId, int Ordinal, int Index)? Decode(string? entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId) || entryId.Length > 2048)
            return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(entryId)).Split(Separator);
            return parts switch
            {
                ["a", var owner, var index] when owner.Length > 0 && int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var i) => ("activityExecution", owner, 0, i),
                ["j", var owner, var ordinal, var index] when owner.Length > 0 &&
                    int.TryParse(ordinal, NumberStyles.None, CultureInfo.InvariantCulture, out var o) &&
                    int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var i) => ("alteration", owner, o, i),
                _ => null
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Encode(string value) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(value));
}

/// <summary>
/// FR-014: an opaque page cursor of a commit sequence and a position within the commit. Approximate records (no commit
/// sequence) come first and use bucket 0.
/// </summary>
internal readonly record struct VariableLifecyclePageCursor(int Bucket, long CommitSequence, int Position)
{
    public static VariableLifecyclePageCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split(':');
            if (parts is ["v1", var bucket, var sequence, var position] &&
                int.TryParse(bucket, NumberStyles.None, CultureInfo.InvariantCulture, out var b) && b is 0 or 1 &&
                long.TryParse(sequence, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var s) &&
                int.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out var p))
                return new VariableLifecyclePageCursor(b, s, p);
        }
        catch (FormatException)
        {
        }

        throw new ArgumentException("The cursor is not valid for this read.", nameof(cursor));
    }

    public string Encode() => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"v1:{Bucket}:{CommitSequence}:{Position}")));

    public static VariableLifecyclePageCursor Of(long? commitSequence, int position) => new(commitSequence is null ? 0 : 1, commitSequence ?? 0, position);

    /// <summary>Whether an item at (<paramref name="commitSequence"/>, <paramref name="position"/>) comes after this cursor.</summary>
    public bool IsBefore(long? commitSequence, int position)
    {
        var other = Of(commitSequence, position);
        return other.Bucket != Bucket ? other.Bucket > Bucket
            : other.CommitSequence != CommitSequence ? other.CommitSequence > CommitSequence
            : other.Position > Position;
    }
}
