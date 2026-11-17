using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>
/// Spec 1229 (S-6): the variable lifecycle reads of one instance: the variables, the write history with its concurrent
/// overwrites, the commit-ordered journal, the comparison of two instances, and the payload read of a write's values.
/// Structure follows the existing activity-execution inspection permission; values are value states, and a payload is
/// released only by <see cref="ReadPayloadAsync"/> under the payload permission (Q8, FR-013).
/// </summary>
internal sealed class VariableLifecycleInspectionService(
    IWorkflowExecutionStateStore workflowExecutions,
    IActivityExecutionStateStore activityExecutions,
    IActivityExecutionInspectionStore inspections,
    IWorkflowAlterationStore alterations,
    IDurableValueStateStore durableValues,
    IActivityInspectionContextAsync authorization,
    IActivityExecutionValuePayloadAuditSink auditSink,
    TimeProvider timeProvider) : IVariableLifecycleInspectionService
{
    private const int RowBatch = 500;
    public const int DefaultTake = 100;
    public const int MaximumTake = 1000;

    public async Task<IReadOnlyList<VariableSummaryView>?> ListVariablesAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(workflowExecutionId, cancellationToken) is not { } loaded)
            return null;
        var (model, frames, canInspectSensitive) = (loaded.Model, loaded.Frames, loaded.CanInspectSensitive);
        var snapshots = await SnapshotsForAsync(workflowExecutionId, model.Entries.Where(entry => entry.IsCurrentValue), cancellationToken);
        var lastApplied = model.Entries
            .Where(entry => entry.IsApplied)
            .GroupBy(entry => entry.InstanceKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var result = new List<VariableSummaryView>();
        foreach (var frame in frames)
        foreach (var key in frame.Frame.Values.Keys.Order(StringComparer.Ordinal))
        {
            var version = frame.Frame.VersionOf(key);
            var instanceKey = VariableWriteEntry.InstanceKeyOf(frame.Frame.FrameId, frame.Frame.ScopeId, key);
            var current = model.Entries.LastOrDefault(entry => entry.IsCurrentValue && entry.InstanceKey == instanceKey);
            lastApplied.TryGetValue(instanceKey, out var last);
            result.Add(new VariableSummaryView(
                frame.Frame.ScopeId,
                frame.Frame.FrameId,
                key,
                frame.Kind,
                version.Version,
                last is not null ? WriterOf(last) : version.Writer is { } writer ? WriterOf(writer) : null,
                last?.CommitSequence,
                current is not null
                    ? WrittenValueState(current, snapshots, canInspectSensitive)
                    : new VariableValueStateView(ValueStates.Omitted, version.Digest, null, false),
                model.HistoryRecorded));
        }

        return result;
    }

    public async Task<VariableWriteHistoryPageView?> ListWritesAsync(VariableWriteHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cursor = VariableLifecyclePageCursor.Decode(query.Cursor);
        var take = Take(query.Take);
        if (await LoadAsync(query.WorkflowExecutionId, cancellationToken) is not { } loaded)
            return null;
        var filtered = loaded.Model.Entries.Where(entry => Matches(entry, query)).ToList();
        var page = filtered
            .Where(entry => cursor is not { } after || after.IsBefore(entry.CommitSequence, entry.PositionInCommit))
            .Take(take + 1)
            .ToList();
        var hasMore = page.Count > take;
        if (hasMore)
            page.RemoveAt(page.Count - 1);
        var views = await ToViewsAsync(query.WorkflowExecutionId, page, loaded.CanInspectSensitive, cancellationToken);
        return new VariableWriteHistoryPageView(
            views,
            hasMore ? VariableLifecyclePageCursor.Of(page[^1].CommitSequence, page[^1].PositionInCommit).Encode() : null,
            query.Overwrites == true ? filtered.Count : null);
    }

    public async Task<JournalPageView?> ListJournalAsync(string workflowExecutionId, string? cursor, int? take, CancellationToken cancellationToken)
    {
        var after = VariableLifecyclePageCursor.Decode(cursor);
        var size = Take(take);
        if (await LoadAsync(workflowExecutionId, cancellationToken) is not { } loaded)
            return null;
        var commits = loaded.Model.BuildJournal()
            .Where(commit => after is not { } position || position.IsBefore(commit.CommitSequence, 0))
            .Take(size + 1)
            .ToList();
        var hasMore = commits.Count > size;
        if (hasMore)
            commits.RemoveAt(commits.Count - 1);
        var views = new List<JournalCommitView>(commits.Count);
        foreach (var commit in commits)
            views.Add(await JournalViewAsync(workflowExecutionId, loaded.Model, commit, loaded.CanInspectSensitive, cancellationToken));
        return new JournalPageView(views, hasMore ? VariableLifecyclePageCursor.Of(commits[^1].CommitSequence, 0).Encode() : null);
    }

    public async Task<InstanceComparisonResult> CompareAsync(string workflowExecutionId, string otherWorkflowExecutionId, CancellationToken cancellationToken)
    {
        if (await LoadAsync(workflowExecutionId, cancellationToken) is not { } left ||
            await LoadAsync(otherWorkflowExecutionId, cancellationToken) is not { } right)
            return InstanceComparisonResult.NotFound;
        var executable = WorkflowExecutableIdentityComparer.Format(left.Workflow.PinnedExecutable);
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(left.Workflow.PinnedExecutable, right.Workflow.PinnedExecutable))
            return new InstanceComparisonResult(InstanceComparisonOutcome.DifferentExecutable, new InstanceComparisonView(
                workflowExecutionId, otherWorkflowExecutionId, executable, false, "differentExecutable", null, [], false));

        var partial = !left.Workflow.Status.IsTerminal() || !right.Workflow.Status.IsTerminal();
        // Matched transitions, commit by commit: the first commit whose transitions differ by run-time path.
        var leftJournal = left.Model.BuildJournal();
        var rightJournal = right.Model.BuildJournal();
        InstanceDivergenceView? divergence = null;
        var shared = Math.Min(leftJournal.Count, rightJournal.Count);
        for (var index = 0; index < shared && divergence is null; index++)
        {
            if (!StringComparer.Ordinal.Equals(left.Model.SignatureOf(leftJournal[index]), right.Model.SignatureOf(rightJournal[index])))
                divergence = new InstanceDivergenceView(index,
                    StructureOnly(left.Model, leftJournal[index]),
                    StructureOnly(right.Model, rightJournal[index]));
        }

        // A run still going compares what it has committed; a finished one with more commits diverges where it goes on.
        if (divergence is null && !partial && leftJournal.Count != rightJournal.Count)
            divergence = new InstanceDivergenceView(shared,
                shared < leftJournal.Count ? StructureOnly(left.Model, leftJournal[shared]) : null,
                shared < rightJournal.Count ? StructureOnly(right.Model, rightJournal[shared]) : null);

        var leftVariables = DigestsByVariable(left.Model);
        var rightVariables = DigestsByVariable(right.Model);
        var variables = leftVariables.Keys.Union(rightVariables.Keys)
            .Order(StringComparer.Ordinal)
            .Select(key =>
            {
                var l = leftVariables.GetValueOrDefault(key) ?? VariableDigests.Empty(key);
                var r = rightVariables.GetValueOrDefault(key) ?? VariableDigests.Empty(key);
                return new VariableComparisonView(
                    l.FramePath ?? r.FramePath!,
                    l.VariableKey ?? r.VariableKey!,
                    !StringComparer.Ordinal.Equals(l.Final, r.Final),
                    !l.Written.SequenceEqual(r.Written, StringComparer.Ordinal),
                    l.Written.Count,
                    r.Written.Count);
            })
            .ToArray();
        return new InstanceComparisonResult(InstanceComparisonOutcome.Compared, new InstanceComparisonView(
            workflowExecutionId, otherWorkflowExecutionId, executable, true, null, divergence, variables, partial));
    }

    public async Task<VariableWritePayloadResult> ReadPayloadAsync(string workflowExecutionId, string entryId, string? value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var which = value ?? PayloadValues.Written;
        if (which is not (PayloadValues.Written or PayloadValues.Overwritten or PayloadValues.Refused) ||
            VariableWriteEntryId.Decode(entryId) is not { } owner)
            return VariableWritePayloadResult.NotFound;
        var workflow = await workflowExecutions.FindAsync(workflowExecutionId, cancellationToken);
        if (workflow is null || !await authorization.CanInspectStructureAsync(workflow, cancellationToken))
            return VariableWritePayloadResult.NotFound;

        VariableWriteRecord? record;
        ActivityExecutionState? row = null;
        if (owner.Kind == "activityExecution")
        {
            row = await activityExecutions.FindAsync(workflowExecutionId, owner.OwnerId, cancellationToken);
            record = row?.VariableWrites is { } writes && owner.Index < writes.Count ? writes[owner.Index] : null;
        }
        else
        {
            var job = (await alterations.ListVariableWriteJobsAsync(workflowExecutionId, cancellationToken))
                .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.JobId, owner.OwnerId));
            var writes = job?.Outcomes.FirstOrDefault(outcome => outcome.Ordinal == owner.Ordinal)?.VariableWrites;
            record = writes is not null && owner.Index < writes.Count ? writes[owner.Index] : null;
        }

        if (record is null)
            return VariableWritePayloadResult.NotFound;

        var auditActivity = row?.Execution.ActivityExecutionId ?? $"alteration:{owner.OwnerId}";
        var evidenceId = $"{entryId}:{which}";
        var auditSubject = authorization.AuditSubject;
        var profile = await authorization.GetAuthorizationProfileAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(auditSubject))
            return VariableWritePayloadResult.Denied;
        if (!await authorization.CanResolveSensitiveValuePayloadsAsync(workflow, cancellationToken))
        {
            await AuditAsync("denied");
            return VariableWritePayloadResult.Denied;
        }

        JsonElement? payload = null;
        var captureMode = "Payload";
        switch (which)
        {
            case PayloadValues.Written when row is not null:
                var snapshot = FindWrittenSnapshot(await inspections.FindAsync(workflowExecutionId, row.Execution.ActivityExecutionId, cancellationToken), record);
                if (snapshot is not null && ActivityExecutionInspectionDisclosure.WithheldKind(snapshot) is null &&
                    snapshot.CaptureMode is RuntimePayloadCaptureMode.Payload or RuntimePayloadCaptureMode.DiagnosticSnapshot)
                {
                    payload = snapshot.Payload;
                    captureMode = snapshot.CaptureMode.ToString();
                }
                break;
            case PayloadValues.Overwritten:
                payload = InlineValue(record.OverwrittenValue);
                break;
            case PayloadValues.Refused:
                payload = InlineValue(record.RejectedValue);
                break;
        }

        if (payload is not { } released)
        {
            await AuditAsync("unavailable");
            return VariableWritePayloadResult.Unavailable;
        }

        await AuditAsync("resolved");
        return new VariableWritePayloadResult(VariableWritePayloadOutcome.Resolved, new VariableWritePayloadView(entryId, which, captureMode, released.Clone()));

        ValueTask AuditAsync(string outcome) => auditSink.RecordAsync(new ActivityExecutionValuePayloadAuditRecord(
            workflowExecutionId, auditActivity, evidenceId, authorization.TenantScope, profile, auditSubject,
            authorization.RequestCorrelationId, outcome, timeProvider.GetUtcNow()), cancellationToken);
    }

    // --- loading --------------------------------------------------------------------------------------------------

    private async Task<LoadedInstance?> LoadAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var workflow = await workflowExecutions.FindAsync(workflowExecutionId, cancellationToken);
        if (workflow is null || !await authorization.CanInspectStructureAsync(workflow, cancellationToken))
            return null;
        var canInspectSensitive = await authorization.CanInspectSensitiveValuesAsync(workflow, cancellationToken);

        // Rows by their commit-sequence range, then the rows without one (older rows, commits outside a lease).
        var rows = new List<ActivityExecutionState>();
        ActivityExecutionStateCommitSequenceKey? after = null;
        while (true)
        {
            var batch = await activityExecutions.ListByCommitSequenceAsync(
                new ActivityExecutionStateCommitSequenceQuery(workflowExecutionId, 0, RowBatch, after), cancellationToken);
            rows.AddRange(batch);
            if (batch.Count < RowBatch)
                break;
            after = new ActivityExecutionStateCommitSequenceKey(batch[^1].FirstCommitSequence!.Value, batch[^1].Execution.ActivityExecutionId);
        }

        rows.AddRange(await activityExecutions.ListWithoutCommitSequenceAsync(workflowExecutionId, cancellationToken));
        var jobs = await alterations.ListVariableWriteJobsAsync(workflowExecutionId, cancellationToken);
        var frames = await FramesAsync(workflow, rows, cancellationToken);
        var currentVersions = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var frame in frames)
        foreach (var key in frame.Frame.Values.Keys)
            currentVersions[VariableWriteEntry.InstanceKeyOf(frame.Frame.FrameId, frame.Frame.ScopeId, key)] = frame.Frame.VersionOf(key).Version;
        return new LoadedInstance(workflow, VariableLifecycleInspection.Build(rows, jobs, currentVersions), frames, canInspectSensitive);
    }

    // The variable instances that exist now: the root frame, each row's container and iteration frame, and each closed
    // scope's variable rows (with their version, digest and writer kept on the row).
    private async Task<IReadOnlyList<(VariableFrameState Frame, string Kind)>> FramesAsync(
        WorkflowExecutionState workflow,
        IReadOnlyList<ActivityExecutionState> rows,
        CancellationToken cancellationToken)
    {
        var frames = new Dictionary<string, (VariableFrameState Frame, string Kind)>(StringComparer.Ordinal);
        if (workflow.RootVariableFrame is { } root)
            frames[root.FrameId] = (root, "root");
        foreach (var row in rows.OrderBy(row => row.ExecutionSequence))
        {
            if (row.IterationVariableFrame is { } iteration)
                frames[iteration.FrameId] = (iteration, "iteration");
            if (row.VariableFrame is { } container)
                frames[container.FrameId] = (container, container.Kind == VariableFrameKind.Root ? "root" : "container");
        }

        var boundaries = rows.Where(RuntimeClosedScope.IsClosedBoundary).ToArray();
        if (boundaries.Length > 0)
        {
            var values = await durableValues.ListAllDurableValueStatesAsync(workflow.WorkflowExecutionId, cancellationToken);
            foreach (var boundary in boundaries)
            {
                var frame = RuntimeClosedScope.CreateFrame(boundary, values);
                frames[frame.FrameId] = (frame, "closedScope");
            }
        }

        return frames.Values.ToArray();
    }

    // --- views ----------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<VariableWriteEntryView>> ToViewsAsync(
        string workflowExecutionId,
        IReadOnlyList<VariableWriteEntry> entries,
        bool canInspectSensitive,
        CancellationToken cancellationToken)
    {
        var snapshots = await SnapshotsForAsync(workflowExecutionId, entries, cancellationToken);
        return entries.Select(entry => ToView(entry, snapshots, canInspectSensitive)).ToArray();
    }

    private static VariableWriteEntryView ToView(
        VariableWriteEntry entry,
        IReadOnlyDictionary<string, ActivityExecutionInspectionProjection?> snapshots,
        bool canInspectSensitive)
    {
        var record = entry.Record;
        var refused = record.Outcome == VariableCommitOutcome.LostUpdateFault
            ? EnvelopeState(entry, record.RejectedValue, PayloadValues.Refused, canInspectSensitive)
            : null;
        return new VariableWriteEntryView(
            entry.EntryId,
            record.ScopeId,
            record.FrameId,
            record.VariableKey,
            WriterOf(entry),
            SourceName(record.Writer.Source),
            OutcomeName(record.Outcome),
            record.BaseVersion,
            record.CurrentVersion,
            record.NewVersion,
            entry.CommitSequence,
            record.CommitOrder,
            entry.OrderApproximate,
            entry.IsApplied,
            entry.IsCurrentValue,
            entry.Overwrote is { } overwritten
                ? new VariableOverwriteView(overwritten.EntryId, WriterOf(overwritten), overwritten.CommitSequence, record.IsLostUpdate || record.Outcome == VariableCommitOutcome.AcceptedOverride)
                : null,
            refused ?? WrittenValueState(entry, snapshots, canInspectSensitive),
            record.IsLostUpdate || record.Outcome == VariableCommitOutcome.AcceptedOverride
                ? EnvelopeState(entry, record.OverwrittenValue, PayloadValues.Overwritten, canInspectSensitive)
                : null,
            refused,
            entry.ResolvesEntry?.EntryId,
            record.OccurredAt);
    }

    private async Task<JournalCommitView> JournalViewAsync(
        string workflowExecutionId,
        VariableLifecycleInspection model,
        JournalCommit commit,
        bool canInspectSensitive,
        CancellationToken cancellationToken)
    {
        var structure = StructureOnly(model, commit);
        return structure with { Writes = await ToViewsAsync(workflowExecutionId, commit.Writes, canInspectSensitive, cancellationToken) };
    }

    // A journal commit without its writes' value states: what a comparison shows (digests only, FR-012).
    private static JournalCommitView StructureOnly(VariableLifecycleInspection model, JournalCommit commit)
    {
        var occurredAt = commit.Writes.Select(write => (DateTimeOffset?)write.Record.OccurredAt)
            .Concat(commit.InputRefreshes.Select(refresh => (DateTimeOffset?)refresh.Refresh.RefreshedAt))
            .Min();
        return new JournalCommitView(
            commit.CommitSequence,
            commit.CommitSequence is null,
            occurredAt,
            commit.Transitions.Select(transition => new JournalTransitionView(
                transition.Row.Execution.ActivityExecutionId,
                transition.Row.Execution.ExecutableNodeId,
                model.RunTimePathOf(transition.Row),
                Camel(transition.Stamp.Kind.ToString()),
                transition.Stamp.BranchId,
                transition.Stamp.ExecutionPathId,
                transition.Stamp.Evaluation)).ToArray(),
            commit.InputRefreshes.Select(refresh => new JournalInputRefreshView(
                refresh.Row.Execution.ActivityExecutionId,
                refresh.Refresh.Superseded.EvaluationNumber,
                refresh.Refresh.ReplacedByEvaluation,
                refresh.Refresh.Reason,
                refresh.Refresh.Changes.Select(change => change.VariableKey).ToArray())).ToArray(),
            commit.Writes.Select(write => new VariableWriteEntryView(
                write.EntryId, write.Record.ScopeId, write.Record.FrameId, write.Record.VariableKey, WriterOf(write),
                SourceName(write.Record.Writer.Source), OutcomeName(write.Record.Outcome), write.Record.BaseVersion,
                write.Record.CurrentVersion, write.Record.NewVersion, write.CommitSequence, write.Record.CommitOrder,
                write.OrderApproximate, write.IsApplied, write.IsCurrentValue,
                write.Overwrote is { } overwritten ? new VariableOverwriteView(overwritten.EntryId, WriterOf(overwritten), overwritten.CommitSequence, write.Record.IsLostUpdate) : null,
                new VariableValueStateView(ValueStates.Omitted, write.Record.Digest, null, false),
                null, null, write.ResolvesEntry?.EntryId, write.Record.OccurredAt)).ToArray());
    }

    private async Task<IReadOnlyDictionary<string, ActivityExecutionInspectionProjection?>> SnapshotsForAsync(
        string workflowExecutionId,
        IEnumerable<VariableWriteEntry> entries,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ActivityExecutionInspectionProjection?>(StringComparer.Ordinal);
        foreach (var row in entries.Where(entry => entry.Row is not null && entry.IsApplied).Select(entry => entry.Row!.Execution.ActivityExecutionId).Distinct(StringComparer.Ordinal))
            result[row] = await inspections.FindAsync(workflowExecutionId, row, cancellationToken);
        return result;
    }

    // Research R-4: the written value is activity value evidence of the writer; its capture decides the state.
    private static VariableValueStateView WrittenValueState(
        VariableWriteEntry entry,
        IReadOnlyDictionary<string, ActivityExecutionInspectionProjection?> projections,
        bool canInspectSensitive)
    {
        var digest = entry.IsApplied ? entry.Record.Digest : null;
        if (entry.Row is null || !entry.IsApplied)
            return new VariableValueStateView(ValueStates.Omitted, digest, null, false);
        var snapshot = FindWrittenSnapshot(projections.GetValueOrDefault(entry.Row.Execution.ActivityExecutionId), entry.Record);
        if (snapshot is null || ActivityExecutionInspectionDisclosure.WithheldKind(snapshot) is not null ||
            snapshot.CaptureMode is not (RuntimePayloadCaptureMode.Payload or RuntimePayloadCaptureMode.DiagnosticSnapshot))
            return new VariableValueStateView(ValueStates.Omitted, digest, null, false);
        if (snapshot.Payload is null)
            return new VariableValueStateView(ValueStates.Expired, digest, null, false);
        if (!canInspectSensitive)
            return new VariableValueStateView(ValueStates.Redacted, digest, null, false);
        return new VariableValueStateView(ValueStates.Captured, digest, PayloadRef(entry, PayloadValues.Written), false);
    }

    // Overwritten and refused values are operational data on the write record (FR-026), shown by the same rules.
    private static VariableValueStateView? EnvelopeState(VariableWriteEntry entry, ValueEnvelope? envelope, string which, bool canInspectSensitive)
    {
        if (envelope is null)
            return new VariableValueStateView(ValueStates.Omitted, null, null, true);
        if (InlineValue(envelope) is null)
            return new VariableValueStateView(ValueStates.Omitted, null, null, true);
        if (!canInspectSensitive || envelope.Policy.HidesValue())
            return new VariableValueStateView(ValueStates.Redacted, null, null, true);
        return new VariableValueStateView(ValueStates.Captured, null, PayloadRef(entry, which), true);
    }

    private static JsonElement? InlineValue(ValueEnvelope? envelope) => envelope?.Presence switch
    {
        ValuePresence.ExplicitNull => JsonSerializer.SerializeToElement<object?>(null),
        ValuePresence.Present when envelope.InlineValue is { } value => value,
        _ => null
    };

    private static VariableValuePayloadRefView PayloadRef(VariableWriteEntry entry, string which) =>
        new(entry.Row is null ? "alteration" : "activityExecution", entry.Row?.Execution.ActivityExecutionId ?? entry.AlterationJobId!, entry.EntryId, which);

    private static ActivityExecutionInspectionValueSnapshot? FindWrittenSnapshot(ActivityExecutionInspectionProjection? projection, VariableWriteRecord record) =>
        projection?.ValueSnapshots.LastOrDefault(snapshot =>
            snapshot.Subject == ActivityExecutionInspectionValueSubject.VariableWrite &&
            StringComparer.Ordinal.Equals(snapshot.Name, record.VariableKey) &&
            StringComparer.Ordinal.Equals(snapshot.Phase, record.ScopeId) &&
            snapshot.Sequence == record.NewVersion);

    private static VariableWriterView WriterOf(VariableWriteEntry entry) => new(
        SourceName(entry.Record.Writer.Source),
        entry.Record.Writer.ExecutableNodeId ?? entry.Row?.Execution.ExecutableNodeId,
        entry.Row?.Execution.ActivityExecutionId,
        entry.Row?.LogicalInvocationId,
        entry.Record.Writer.AttemptId,
        entry.Row?.BranchId,
        entry.Row?.Provenance.ExecutionPathId,
        entry.AlterationJobId);

    private static VariableWriterView WriterOf(VariableWriter writer) => new(
        SourceName(writer.Source),
        writer.ExecutableNodeId,
        writer.Source == VariableWriteSource.Alteration ? null : writer.InvocationId,
        null,
        writer.AttemptId,
        null,
        null,
        writer.Source == VariableWriteSource.Alteration ? writer.InvocationId : null);

    private static bool Matches(VariableWriteEntry entry, VariableWriteHistoryQuery query) =>
        (query.FrameId is null || StringComparer.Ordinal.Equals(entry.Record.FrameId, query.FrameId)) &&
        (query.ScopeId is null || StringComparer.Ordinal.Equals(entry.Record.ScopeId, query.ScopeId)) &&
        (query.VariableKey is null || StringComparer.Ordinal.Equals(entry.Record.VariableKey, query.VariableKey)) &&
        (query.Source is null || StringComparer.OrdinalIgnoreCase.Equals(SourceName(entry.Record.Writer.Source), query.Source)) &&
        (query.Outcome is null || StringComparer.OrdinalIgnoreCase.Equals(OutcomeName(entry.Record.Outcome), query.Outcome)) &&
        (query.Overwrites != true || entry.Overwrote is not null);

    private static Dictionary<string, VariableDigests> DigestsByVariable(VariableLifecycleInspection model)
    {
        var result = new Dictionary<string, VariableDigests>(StringComparer.Ordinal);
        foreach (var entry in model.Entries.Where(entry => entry.IsApplied))
        {
            var framePath = model.FramePathOf(entry);
            var key = $"{framePath}\u001f{entry.Record.VariableKey}";
            if (!result.TryGetValue(key, out var digests))
                result[key] = digests = new VariableDigests(framePath, entry.Record.VariableKey);
            digests.Written.Add(entry.Record.Digest ?? string.Empty);
            digests.Final = entry.Record.Digest ?? string.Empty;
        }

        return result;
    }

    private static int Take(int? take) => take is null ? DefaultTake : Math.Clamp(take.Value, 1, MaximumTake);

    public static string SourceName(VariableWriteSource source) => Camel(source.ToString());

    public static string OutcomeName(VariableCommitOutcome outcome) => outcome switch
    {
        VariableCommitOutcome.LostUpdateWarning => "lostUpdateWarning",
        VariableCommitOutcome.LostUpdateFault => "lostUpdateFault",
        VariableCommitOutcome.AcceptedOverride => "acceptedOverride",
        _ => "applied"
    };

    private static string Camel(string value) => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private sealed record LoadedInstance(
        WorkflowExecutionState Workflow,
        VariableLifecycleInspection Model,
        IReadOnlyList<(VariableFrameState Frame, string Kind)> Frames,
        bool CanInspectSensitive);

    private sealed class VariableDigests(string? framePath, string? variableKey)
    {
        public string? FramePath { get; } = framePath;
        public string? VariableKey { get; } = variableKey;
        public List<string> Written { get; } = [];
        public string? Final { get; set; }

        public static VariableDigests Empty(string key) => new(key.Split('\u001f')[0], key.Split('\u001f')[^1]);
    }

    internal static class ValueStates
    {
        public const string Captured = "captured";
        public const string Omitted = "omitted";
        public const string Redacted = "redacted";
        public const string Expired = "expired";
    }

    internal static class PayloadValues
    {
        public const string Written = "written";
        public const string Overwritten = "overwritten";
        public const string Refused = "refused";
    }
}

/// <summary>The variable lifecycle read operations the runtime endpoints dispatch to; null means not found or hidden.</summary>
public interface IVariableLifecycleInspectionService
{
    Task<IReadOnlyList<VariableSummaryView>?> ListVariablesAsync(string workflowExecutionId, CancellationToken cancellationToken);
    Task<VariableWriteHistoryPageView?> ListWritesAsync(VariableWriteHistoryQuery query, CancellationToken cancellationToken);
    Task<JournalPageView?> ListJournalAsync(string workflowExecutionId, string? cursor, int? take, CancellationToken cancellationToken);
    Task<InstanceComparisonResult> CompareAsync(string workflowExecutionId, string otherWorkflowExecutionId, CancellationToken cancellationToken);
    Task<VariableWritePayloadResult> ReadPayloadAsync(string workflowExecutionId, string entryId, string? value, CancellationToken cancellationToken);
}

/// <summary>The write-history filters (FR-003) and page position.</summary>
public sealed record VariableWriteHistoryQuery(
    string WorkflowExecutionId,
    string? FrameId = null,
    string? ScopeId = null,
    string? VariableKey = null,
    string? Source = null,
    string? Outcome = null,
    bool? Overwrites = null,
    string? Cursor = null,
    int? Take = null);

public enum InstanceComparisonOutcome
{
    Compared,
    NotFound,
    DifferentExecutable
}

public sealed record InstanceComparisonResult(InstanceComparisonOutcome Outcome, InstanceComparisonView? View)
{
    public static InstanceComparisonResult NotFound { get; } = new(InstanceComparisonOutcome.NotFound, null);
}

public enum VariableWritePayloadOutcome
{
    Resolved,
    NotFound,
    Denied,
    Unavailable
}

public sealed record VariableWritePayloadResult(VariableWritePayloadOutcome Outcome, VariableWritePayloadView? Value)
{
    public static VariableWritePayloadResult NotFound { get; } = new(VariableWritePayloadOutcome.NotFound, null);
    public static VariableWritePayloadResult Denied { get; } = new(VariableWritePayloadOutcome.Denied, null);
    public static VariableWritePayloadResult Unavailable { get; } = new(VariableWritePayloadOutcome.Unavailable, null);
}
