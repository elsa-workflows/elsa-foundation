using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelRuntimeHistoryPageReader(KernelRunReader runs, KernelRuns host, KernelHistoryCursor cursor,
    IActivityInspectionContextAsync authorization, RuntimeHistoryInspectionService legacy, TimeProvider time) : IRuntimeHistoryPageReader
{
    public async Task<RuntimeHistoryPageView?> ReadAsync(GetRuntimeHistory request, CancellationToken cancellationToken = default)
    {
        var run = await AuthorizedRunAsync(request.WorkflowExecutionId, cancellationToken);
        if (run is null) return null;
        var access = await AccessBindingAsync(cancellationToken);
        if (run.Header is not { } header)
        {
            if (KernelHistoryCursor.IsKernelCursor(request.Cursor)) return null;
            var legacyPage = await legacy.ReadAsync(request, cancellationToken);
            var current = await AuthorizedRunAsync(request.WorkflowExecutionId, cancellationToken);
            return current is { Header: null } && current.State.TenantId == run.State.TenantId &&
                await AccessBindingAsync(cancellationToken) == access ? legacyPage : null;
        }

        if (request.Category is { } category && !Enum.IsDefined(category))
            throw new ArgumentException("Unknown history category.", nameof(request));
        var query = new RuntimeHistoryQuery(request.WorkflowExecutionId, request.Limit, request.Cursor,
            request.ActivityExecutionId, request.IncludeDescendants, request.BookmarkId, request.Category, request.Name, request.Search, access);
        var tenant = host.Tenant.Tenant;
        var position = cursor.Open(query, tenant, header.Workflow);
        RuntimeHistoryPageView page;
        if (!KernelActivityTimeline.Supports(header) || !await host.SupportsSequencedJournalAsync(cancellationToken))
        {
            page = Empty("unsupported", "The run does not have sequenced structural history on this host.", time.GetUtcNow());
        }
        else
        {
            var snapshot = await host.ReadSequencedJournalAsync(new ActorRef(request.WorkflowExecutionId),
                position.AfterSequence, position.UpperSequence, query.Limit, cancellationToken);
            if (snapshot is null) return null;
            page = Project(query, tenant, header.Workflow, position, snapshot);
        }

        var verified = await AuthorizedRunAsync(request.WorkflowExecutionId, cancellationToken);
        return verified is { Header: { } currentHeader } && currentHeader.Workflow == header.Workflow &&
            currentHeader.Model == header.Model && currentHeader.History == header.History && verified.State.TenantId == run.State.TenantId &&
            await AccessBindingAsync(cancellationToken) == access ? page : null;
    }

    private RuntimeHistoryPageView Project(RuntimeHistoryQuery query, string tenant, string workflow,
        KernelHistoryCursor.Position position, ActorSequencedJournalPage snapshot)
    {
        var readAt = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.AsOf);
        if (snapshot.UpperSequence < position.AfterSequence ||
            position.UpperSequence is { } pinned && pinned != snapshot.UpperSequence ||
            snapshot.ScannedThroughSequence < position.AfterSequence || snapshot.ScannedThroughSequence > snapshot.UpperSequence ||
            snapshot.Items.Count > query.Limit || snapshot.RetainedFromSequence < 0 ||
            snapshot.RetainedFromSequence > snapshot.UpperSequence ||
            snapshot.ScannedThroughSequence == position.AfterSequence && snapshot.ScannedThroughSequence < snapshot.UpperSequence)
            return Invalid(readAt);

        var events = new List<RuntimeHistoryEventView>();
        var previous = Math.Max(position.AfterSequence, snapshot.RetainedFromSequence - 1);
        var revision = position.OccurrenceRevision;
        var occurredAt = position.OccurredAt;
        var omitted = false;
        try
        {
            foreach (var entry in snapshot.Items)
            {
                if (entry.Sequence != previous + 1 || entry.Sequence > snapshot.ScannedThroughSequence ||
                    entry.Revision <= 0 || entry.Revision < revision || entry.CommitOrdinal < 0)
                    return Invalid(readAt);
                previous = entry.Sequence;
                if (revision != entry.Revision)
                {
                    revision = entry.Revision;
                    occurredAt = null;
                }
                if (entry.Entry is null)
                {
                    omitted = true;
                    continue;
                }
                var item = KernelHistoryJournal.Project(query.WorkflowExecutionId, entry.Sequence, entry.Revision,
                    entry.CommitOrdinal, entry.Entry, entry.RecordedAt, ref occurredAt);
                if (item is not null && KernelHistoryJournal.Matches(query, item))
                    events.Add(item);
            }
        }
        catch (Exception error) when (error is JsonException or ArgumentOutOfRangeException)
        {
            return Invalid(readAt);
        }
        if (previous != snapshot.ScannedThroughSequence) return Invalid(readAt);

        const string vocabularyLimitation = "Only retained committed activity starts and outcomes are available.";
        var scopeLimitation = query.IncludeDescendants ? "Some descendant activity history is unavailable." :
            query.BookmarkId is not null ? "Kernel bookmark history is unavailable." :
            omitted ? "Some retained journal entries could not be projected." :
            query.Category is not (null or RuntimeHistoryEventCategory.ActivityLifecycle) ||
                query.Name is { } name && !KernelHistoryJournal.SupportedKinds.Contains(name, StringComparer.Ordinal)
                ? vocabularyLimitation : null;
        var fromStart = snapshot.CapturedFromStart && snapshot.RetainedFromSequence == 1;
        var coverage = new RuntimeHistoryCoverageView("partial", vocabularyLimitation, KernelHistoryJournal.Policy, 1,
            KernelHistoryJournal.SupportedKinds, fromStart, !fromStart || omitted, snapshot.RetainedFromSequence,
            snapshot.UpperSequence, snapshot.UpperSequence, Math.Max(0, snapshot.RetainedFromSequence - 1), null,
            snapshot.LastRecordedAt is { } recorded ? DateTimeOffset.FromUnixTimeMilliseconds(recorded) : null)
        { CaptureAvailable = true };
        var next = snapshot.ScannedThroughSequence < snapshot.UpperSequence
            ? cursor.Seal(query, tenant, workflow, new(snapshot.ScannedThroughSequence, snapshot.UpperSequence, revision, occurredAt))
            : null;
        var catchUp = cursor.Seal(query, tenant, workflow, new(snapshot.UpperSequence, null));
        return new(coverage, events, snapshot.UpperSequence, snapshot.ScannedThroughSequence, snapshot.Items.Count, next, catchUp,
            scopeLimitation is null, scopeLimitation, readAt);
    }

    private async ValueTask<KernelRunReader.Run?> AuthorizedRunAsync(string runId, CancellationToken cancellationToken) =>
        await runs.GetAsync(runId, cancellationToken) is { } run && await authorization.CanInspectStructureAsync(run.State, cancellationToken)
            ? run : null;

    private async ValueTask<string> AccessBindingAsync(CancellationToken cancellationToken) => RuntimeHistoryIdentity.CreateBinding(
        [authorization.TenantScope, authorization.AuditSubject, await authorization.GetAuthorizationProfileAsync(cancellationToken)]);

    private static RuntimeHistoryPageView Invalid(DateTimeOffset readAt) =>
        Empty("invalid", "The committed journal boundary could not be established.", readAt);

    private static RuntimeHistoryPageView Empty(string state, string limitation, DateTimeOffset readAt) =>
        new(new(state, limitation, null, null, [], false, state == "invalid", 0, 0, 0, 0, null, null),
            [], 0, 0, 0, null, null, false, limitation, readAt);
}
