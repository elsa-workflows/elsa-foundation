using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelRuntimeHistoryPageReader(KernelRunReader runs, KernelRuns host, KernelHistoryCursor cursor,
    IActivityInspectionContextAsync authorization, RuntimeHistoryInspectionService legacy, TimeProvider time) : IRuntimeHistoryPageReader
{
    public async Task<RuntimeHistoryPageView?> ReadAsync(GetRuntimeHistory request, CancellationToken cancellationToken = default) =>
        (await ReadPageAsync(request, currentPermission: null, cancellationToken)).Page;

    internal async Task<(RuntimeHistoryPageView? Page, int? Status)> ReadPageAsync(GetRuntimeHistory request,
        Func<WorkflowExecutionState, Task<int?>>? currentPermission, CancellationToken cancellationToken)
    {
        var run = await AuthorizedRunAsync(request.WorkflowExecutionId, cancellationToken);
        if (run is null) return (null, null);
        var access = await AccessBindingAsync(cancellationToken);
        if (run.Header is not { } header)
        {
            if (currentPermission is not null) return (null, StatusCodes.Status501NotImplemented);
            if (KernelHistoryCursor.IsKernelCursor(request.Cursor)) return (null, null);
            var legacyPage = await legacy.ReadAsync(request, cancellationToken);
            var current = await AuthorizedRunAsync(request.WorkflowExecutionId, cancellationToken);
            return (current is { Header: null } && current.State.TenantId == run.State.TenantId &&
                await AccessBindingAsync(cancellationToken) == access ? legacyPage : null, null);
        }

        if (currentPermission is not null && await currentPermission(run.State) is { } denied)
            return (null, denied);
        if (request.Category is { } category && !Enum.IsDefined(category))
            throw new ArgumentException("Unknown history category.", nameof(request));
        var query = new RuntimeHistoryQuery(request.WorkflowExecutionId, request.Limit, request.Cursor,
            request.ActivityExecutionId, request.IncludeDescendants, request.BookmarkId, request.Category, request.Name, request.Search, access);
        var tenant = host.Tenant.Tenant;
        var position = cursor.Open(query, tenant, header.Workflow);
        var page = KernelActivityTimeline.Supports(header)
            ? await ReadKernelAsync(query, tenant, header.Workflow, position, cancellationToken)
            : Unsupported();
        if (page is null) return (null, null);

        var verified = await AuthorizedRunAsync(request.WorkflowExecutionId, cancellationToken);
        if (verified is not { Header: { } currentHeader } || currentHeader.Workflow != header.Workflow ||
            currentHeader.Model != header.Model || currentHeader.History != header.History || verified.State.TenantId != run.State.TenantId ||
            await AccessBindingAsync(cancellationToken) != access)
            return (null, null);
        if (currentPermission is not null && await currentPermission(verified.State) is { } revoked)
            return (null, revoked);
        return (page, null);
    }

    private async ValueTask<RuntimeHistoryPageView?> ReadKernelAsync(RuntimeHistoryQuery query, string tenant, string workflow,
        KernelHistoryCursor.Position position, CancellationToken cancellationToken)
    {
        ActorSequencedJournalPage? snapshot;
        try
        {
            if (!await host.SupportsSequencedJournalAsync(cancellationToken)) return Unsupported();
            snapshot = await host.ReadSequencedJournalAsync(new ActorRef(query.WorkflowExecutionId),
                position.AfterSequence, position.UpperSequence, query.Limit, cancellationToken);
        }
        catch (Exception error) when (IsTransientReadFailure(error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new RuntimeHistoryUnavailableException(query.WorkflowExecutionId, "read-sequenced-journal", error);
        }
        return snapshot is null ? null : Project(query, tenant, workflow, position, snapshot);
    }

    private RuntimeHistoryPageView Project(RuntimeHistoryQuery query, string tenant, string workflow,
        KernelHistoryCursor.Position position, ActorSequencedJournalPage snapshot)
    {
        var readAt = time.GetUtcNow();
        DateTimeOffset? lastRecordedAt;
        try
        {
            readAt = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.AsOf);
            lastRecordedAt = snapshot.LastRecordedAt is { } recorded ? DateTimeOffset.FromUnixTimeMilliseconds(recorded) : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return Invalid(readAt);
        }
        if (snapshot.UpperSequence < position.AfterSequence ||
            position.UpperSequence is { } pinned && pinned != snapshot.UpperSequence ||
            snapshot.ScannedThroughSequence < position.AfterSequence || snapshot.ScannedThroughSequence > snapshot.UpperSequence ||
            snapshot.Items.Count > query.Limit || snapshot.RetainedFromSequence < 0 ||
            snapshot.RetainedFromSequence > snapshot.UpperSequence ||
            snapshot.RetainedFromSequence == 0 && snapshot.UpperSequence > 0 ||
            snapshot.ScannedThroughSequence == position.AfterSequence && snapshot.ScannedThroughSequence < snapshot.UpperSequence)
            return Invalid(readAt);

        var events = new List<RuntimeHistoryEventView>();
        var previous = Math.Max(position.AfterSequence, snapshot.RetainedFromSequence - 1);
        var revision = position.LastRevision;
        var ordinal = position.LastCommitOrdinal;
        var omitted = false;
        try
        {
            foreach (var entry in snapshot.Items)
            {
                var expectedOrdinal = entry.Revision == revision && ordinal is { } priorOrdinal ? (long)priorOrdinal + 1 : 0;
                if (entry.Sequence != previous + 1 || entry.Sequence > snapshot.ScannedThroughSequence ||
                    entry.Revision <= 0 || entry.Revision < revision || entry.CommitOrdinal != expectedOrdinal)
                    return Invalid(readAt);
                previous = entry.Sequence;
                revision = entry.Revision;
                ordinal = entry.CommitOrdinal;
                if (entry.Entry is null)
                {
                    omitted = true;
                    continue;
                }
                var item = KernelHistoryJournal.Project(query.WorkflowExecutionId, entry.Sequence, entry.Revision,
                    entry.CommitOrdinal, entry.Entry, entry.RecordedAt);
                if (item is not null && KernelHistoryJournal.Matches(query, item))
                    events.Add(item);
            }
        }
        catch (Exception error) when (error is JsonException or ArgumentOutOfRangeException)
        {
            return Invalid(readAt);
        }
        if (previous != snapshot.ScannedThroughSequence) return Invalid(readAt);

        const string vocabularyLimitation = "History shows retained activity starts and outcomes with structural records.";
        var scopeLimitation = query.IncludeDescendants ? "Some descendant activity history is unavailable." :
            query.BookmarkId is not null ? "Kernel bookmark history is unavailable." :
            omitted ? "Some retained journal entries could not be projected." :
            query.Category is not (null or RuntimeHistoryEventCategory.ActivityLifecycle) ||
                query.Name is { } name && !KernelHistoryJournal.SupportedKinds.Contains(name, StringComparer.Ordinal)
                ? vocabularyLimitation : null;
        // A complete raw sequence alone cannot establish that structural recording was enabled from the run's start.
        var coverage = new RuntimeHistoryCoverageView("partial", vocabularyLimitation, KernelHistoryJournal.Policy, 1,
            KernelHistoryJournal.SupportedKinds, CapturedFromStart: false, HasGaps: true, snapshot.RetainedFromSequence,
            snapshot.UpperSequence, snapshot.UpperSequence, Math.Max(0, snapshot.RetainedFromSequence - 1), null,
            lastRecordedAt);
        var hasNext = snapshot.ScannedThroughSequence < snapshot.UpperSequence;
        var next = hasNext
            ? cursor.Seal(query, tenant, workflow, new(snapshot.ScannedThroughSequence, snapshot.UpperSequence, revision, ordinal))
            : null;
        var catchUp = cursor.Seal(query, tenant, workflow, hasNext
            ? new(snapshot.UpperSequence, null)
            : new(snapshot.UpperSequence, null, revision, ordinal));
        return new(coverage, events, snapshot.UpperSequence, snapshot.ScannedThroughSequence, snapshot.Items.Count, next, catchUp,
            scopeLimitation is null, scopeLimitation, readAt);
    }

    private async ValueTask<KernelRunReader.Run?> AuthorizedRunAsync(string runId, CancellationToken cancellationToken)
    {
        KernelRunReader.Run? run;
        try
        {
            run = await runs.GetAsync(runId, cancellationToken);
        }
        catch (Exception error) when (IsTransientReadFailure(error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new RuntimeHistoryUnavailableException(runId, "read-run", error);
        }
        return run is not null && await authorization.CanInspectStructureAsync(run.State, cancellationToken) ? run : null;
    }

    private static bool IsTransientReadFailure(Exception error) => error is TimeoutException or DbException { IsTransient: true };

    private async ValueTask<string> AccessBindingAsync(CancellationToken cancellationToken) => RuntimeHistoryIdentity.CreateBinding(
        [authorization.TenantScope, authorization.AuditSubject, await authorization.GetAuthorizationProfileAsync(cancellationToken)]);

    private static RuntimeHistoryPageView Invalid(DateTimeOffset readAt) =>
        Empty("invalid", "The committed journal boundary could not be established.", readAt);

    private RuntimeHistoryPageView Unsupported() =>
        Empty("unsupported", "The run does not have sequenced structural history on this host.", time.GetUtcNow());

    private static RuntimeHistoryPageView Empty(string state, string limitation, DateTimeOffset readAt) =>
        new(new(state, limitation, null, null, [], false, state == "invalid", 0, 0, 0, 0, null, null),
            [], 0, 0, 0, null, null, false, limitation, readAt);
}
