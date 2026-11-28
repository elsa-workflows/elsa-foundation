using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelPlaybackPageReader(KernelRunReader runs, KernelRuns host, KernelPlaybackCursor cursor,
    IActivityInspectionContextAsync authorization, TimeProvider time, RuntimeHistoryOccurrenceAuthorization? currentAuthorization = null,
    KernelPreviewRunReads? preview = null)
    : IRuntimeHistoryOccurrencePageReader
{
    private static readonly RuntimeHistoryCapturePolicy Policy = new(KernelHistoryJournal.Policy, 1, false, KernelHistoryJournal.SupportedKinds);

    public async Task<RuntimeHistoryOccurrencePageView?> ReadAsync(GetRuntimeHistoryOccurrences request, CancellationToken cancellationToken = default)
    {
        var run = await FindAsync();
        if (run is null) return null;
        if (run.Header is not { } header)
        {
            if (KernelPlaybackCursor.Owns(request.SequenceToken) || preview?.History is not { } legacy) return null;
            var original = await legacy.ReadOccurrencesAsync(request, cancellationToken);
            return await FindAsync() is { Header: null } current && current.State.TenantId == run.State.TenantId ? original : null;
        }
        var query = new RuntimeHistoryOccurrenceQuery(request.WorkflowExecutionId, request.Limit, request.SequenceToken,
            request.Position, request.AnchorActivityExecutionId, request.ActivityExecutionId, request.IncludeDescendants);
        var source = run.IsolatedReads ?? host;

        if (!KernelActivityTimeline.Supports(header) || !await ProviderAsync(() => source.SupportsPlaybackAsync(cancellationToken), "playback-capability"))
            return await StillOwnsAsync(header) ? Unavailable("occurrence-facet-unsupported") : null;

        var access = await AccessAsync(run.State);
        if (access is null)
            return await StillOwnsAsync(header) ? Unavailable("current-authorization-unsupported") : null;
        var binding = KernelPlaybackCursor.Bind(source.Tenant.Tenant, header.Workflow, query, access);
        var window = cursor.Open(query.SequenceToken, binding);
        if (query.IncludeDescendants &&
            !await ProviderAsync(() => source.SupportsDescendantPlaybackAsync(cancellationToken), "playback-scope-capability"))
            return await ReauthorizeAsync() ? Unavailable("scope-descendants-unsupported") : null;

        var snapshot = await ProviderAsync(() => source.ReadPlaybackAsync(new ActorRef(query.WorkflowExecutionId),
            new(query.Limit, query.Position, query.AnchorActivityExecutionId, query.ActivityExecutionId,
                window?.Upper, window?.RetainedAfter) { IncludeDescendants = query.IncludeDescendants }, cancellationToken), "read-playback");
        if (snapshot is null || !await ReauthorizeAsync()) return null;
        try
        {
            return Project(query, binding, window, snapshot);
        }
        catch (Exception error) when (error is JsonException or ArgumentOutOfRangeException or RuntimeHistoryIntegrityException)
        {
            return Unavailable("history-invalid");
        }

        async ValueTask<KernelRunReader.Run?> FindAsync()
        {
            var current = await ProviderAsync(() => runs.GetAsync(request.WorkflowExecutionId, cancellationToken), "read-run");
            return current is not null && await authorization.CanInspectStructureAsync(current.State, cancellationToken) ? current : null;
        }

        async ValueTask<bool> StillOwnsAsync(WorkflowRunHeader expected) =>
            await FindAsync() is { } current && SameRun(current, expected, run!.State.TenantId);

        async ValueTask<bool> ReauthorizeAsync() =>
            await FindAsync() is { } current && SameRun(current, header, run.State.TenantId) && await AccessAsync(current.State) == access;

        async ValueTask<string?> AccessAsync(WorkflowExecutionState state)
        {
            var initial = RuntimeHistoryIdentity.CreateBinding([authorization.TenantScope, authorization.AuditSubject,
                await authorization.GetAuthorizationProfileAsync(cancellationToken)]);
            if (preview?.History is { } legacy) return await legacy.BindOccurrencesAsync(state, initial, cancellationToken);
            return currentAuthorization is null ? null : await currentAuthorization.BindAsync(state, initial, cancellationToken);
        }

        async ValueTask<T> ProviderAsync<T>(Func<ValueTask<T>> read, string operation)
        {
            try { return await read(); }
            catch (Exception error) when (error is TimeoutException or DbException { IsTransient: true })
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new RuntimeHistoryUnavailableException(request.WorkflowExecutionId, operation, error);
            }
        }
    }

    private static bool SameRun(KernelRunReader.Run run, WorkflowRunHeader expected, string? tenant) =>
        run.Header is { } current && current.Workflow == expected.Workflow && current.Model == expected.Model &&
        current.History == expected.History && current.TestStart == expected.TestStart && run.State.TenantId == tenant;

    private RuntimeHistoryOccurrencePageView Project(RuntimeHistoryOccurrenceQuery query, string binding,
        KernelPlaybackCursor.Window? window, ActorPlaybackPage snapshot)
    {
        var readAt = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.AsOf);
        if (!Enum.IsDefined(snapshot.Availability) || snapshot.LatestSequence < 0 || snapshot.UpperSequence < 0 ||
            snapshot.RetainedAfterSequence < 0 || snapshot.RetainedAfterSequence > snapshot.LatestSequence ||
            (window?.Upper ?? snapshot.LatestSequence) != snapshot.UpperSequence)
            return Unavailable("history-invalid");
        var latestAt = snapshot.LastRecordedAt is { } last ? DateTimeOffset.FromUnixTimeMilliseconds(last) : (DateTimeOffset?)null;
        var newer = snapshot.LatestSequence > snapshot.UpperSequence;
        var coverage = new RuntimeHistoryCoverageView("partial", "Playback covers indexed structural observations.", Policy.PolicyId,
            Policy.Version, Policy.SupportedKinds, false, false, snapshot.LatestSequence == 0 ? 0 : snapshot.RetainedAfterSequence + 1,
            snapshot.LatestSequence, snapshot.LatestSequence, snapshot.RetainedAfterSequence, null, latestAt);
        if (snapshot.Availability != ActorPlaybackAvailability.Available ||
            window is not null && (window.Count != snapshot.Count || window.RetainedAfter != snapshot.RetainedAfterSequence))
        {
            var availability = snapshot.Availability switch
            {
                ActorPlaybackAvailability.Available or ActorPlaybackAvailability.Invalidated => RuntimeHistoryOccurrenceAvailability.Invalidated,
                ActorPlaybackAvailability.TargetUnavailable => RuntimeHistoryOccurrenceAvailability.TargetUnavailable,
                _ => RuntimeHistoryOccurrenceAvailability.Unavailable
            };
            var output = new RuntimeHistoryOccurrencePage(null, availability, snapshot.Reason ?? "retained-range-changed",
                snapshot.UpperSequence, snapshot.RetainedAfterSequence, null, null, [], null, newer);
            return RuntimeHistoryOccurrenceProjection.PresentOccurrences(output, coverage, readAt);
        }

        if (snapshot is not { Count: > 0 and var count, LastRecordedAt: not null } ||
            snapshot.UpperSequence > snapshot.LatestSequence || snapshot.RetainedAfterSequence >= snapshot.UpperSequence)
            return Unavailable("history-invalid");
        var items = snapshot.Items.Select(row => new RuntimeHistoryOccurrence(row.Position,
            KernelHistoryJournal.ReadObservation(query.WorkflowExecutionId, row.Sequence, row.Revision, row.CommitOrdinal,
                row.Observation, row.RecordedAt) ?? throw new JsonException("The indexed playback observation could not be decoded."),
            row.PreviousActivity, row.NextActivity)).ToArray();
        var head = new RuntimeHistoryHead(query.WorkflowExecutionId, snapshot.RetainedAfterSequence + 1, snapshot.LatestSequence,
            "", latestAt!.Value, Policy, false, false, snapshot.RetainedAfterSequence);
        var token = query.SequenceToken ?? cursor.Seal(new(binding, snapshot.UpperSequence, snapshot.RetainedAfterSequence, count));
        var page = new RuntimeHistoryOccurrencePage(head, RuntimeHistoryOccurrenceAvailability.Partial,
            snapshot.RetainedAfterSequence > 0 ? "retained-prefix-purged" : "capture-not-from-start", snapshot.UpperSequence,
            snapshot.RetainedAfterSequence, count, snapshot.SelectedPosition, items, token, newer);
        RuntimeHistoryOccurrenceProjection.ValidateOccurrences(query, page);
        return RuntimeHistoryOccurrenceProjection.PresentOccurrences(page, coverage, readAt);
    }

    private RuntimeHistoryOccurrencePageView Unavailable(string reason) => new("unavailable", reason,
        new("unsupported", null, null, null, [], false, false, "0", "0", "0", "0", null, null, null, null, null, null),
        "0", "0", null, null, [], null, false, time.GetUtcNow());
}
