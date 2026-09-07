using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Runs bounded provider-side recovery routes and correlates candidates with current runtime state.</summary>
/// <remarks>
/// Recovery routes are queried independently because each has a different native index. The aggregate
/// continuation carries one cursor per route. Rows are assigned to one
/// canonical route before selection, allowing the scanner to de-duplicate overlapping signals without materializing
/// the recovery population or accumulating an unbounded emitted-identity set. Candidate correlation is performed
/// against current five-store state before emission; candidate payloads never cross the trust boundary.
/// </remarks>
public sealed class GroundworkV2RuntimeRecoveryScanner : IRuntimeRecoveryPagedScanner
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly GroundworkV2RuntimeLivenessContext context;
    private readonly StorageUnit workflowExecutionUnit;
    private readonly StorageUnit incidentUnit;
    private readonly StorageUnit schedulerUnit;
    private readonly StorageUnit workflowHoldUnit;
    private readonly IRuntimeRecoveryContinuationCodec continuationCodec;

    // Preserve the old constructor metadata for callers compiled before recovery paging. Those callers do not have
    // a way to supply a durable key, so they receive the same process-local development protection as the in-memory
    // scanner. DI composition uses the overload below and injects the host-configured codec.
    public GroundworkV2RuntimeRecoveryScanner(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor)
        : this(sessions, accessContextAccessor, null, CompatibilityCodec())
    {
    }

    public GroundworkV2RuntimeRecoveryScanner(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName)
        : this(sessions, accessContextAccessor, targetName, CompatibilityCodec())
    {
    }

    public GroundworkV2RuntimeRecoveryScanner(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null,
        IRuntimeRecoveryContinuationCodec? continuationCodec = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = GroundworkTargetNames.Normalize(targetName);
        this.continuationCodec = continuationCodec ?? throw new ArgumentNullException(
            nameof(continuationCodec),
            "Groundwork recovery paging requires an injected configured continuation codec.");
        context = new GroundworkV2RuntimeLivenessContext(sessions, accessContextAccessor, this.targetName);
        workflowExecutionUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, this.targetName);
        incidentUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind, this.targetName);
        schedulerUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind, this.targetName);
        workflowHoldUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind, this.targetName);
    }

    private static IRuntimeRecoveryContinuationCodec CompatibilityCodec() =>
        new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions
            {
                AllowEphemeralDevelopmentKey = true
            }));

    public async ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(
        RuntimeRecoveryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Preserve the original collection-returning surface as one bounded sweep. Callers that need complete
        // traversal must use ScanPageAsync and retain its continuation; a legacy call must never drain a population
        // merely because its result type has no continuation channel.
        var page = await ScanPageAsync(request, cancellationToken);
        return page.Items;
    }

    public ValueTask<RuntimeRecoveryPage> ScanPageAsync(
        RuntimeRecoveryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var routes = Routes(request, context.Unit);
        var binding = Binding(request, routes.Count);
        var continuation = DecodeContinuation(request.ContinuationToken, binding, routes.Count);
        var routeCursors = continuation?.Cursors
            .Select(cursor => cursor.ToRouteCursor())
            .ToArray() ?? new RouteCursor[routes.Count];
        var states = new Dictionary<string, ExecutionLivenessState>(StringComparer.Ordinal);
        var nextCursors = routeCursors.ToArray();
        var frontiers = new RouteFrontier[routes.Count];
        var routeStates = new ExecutionLivenessState?[routes.Count];

        // A recovery page deliberately reads one row per active route. This is a bounded global-order frontier:
        // every route is advanced exactly once, and the next row on that route cannot sort before its returned row.
        // A larger provider page would require carrying an unbounded number of cross-route candidates when one
        // route is skewed, while a one-row page keeps continuation state bounded independently of population.
        var livenessRows = context.Open();
        for (var index = 0; index < routes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var route = routes[index];
            var cursor = routeCursors[index];
            if (cursor.Started && cursor.ContinuationToken is null)
            {
                frontiers[index] = RouteFrontier.Complete;
                continue;
            }

            var result = livenessRows.Query(new QueryRequest(
                new TableId(context.Unit.Name),
                route.Where,
                [.. route.Order],
                Projection.All,
                !cursor.Started
                    ? Paging.Keyset(RecoveryProviderPageLimit)
                    : Paging.Continuation(cursor.ContinuationToken!, RecoveryProviderPageLimit)));
            if (cursor.Started && StringComparer.Ordinal.Equals(result.NextContinuationToken, cursor.ContinuationToken))
                throw new InvalidOperationException("Groundwork recovery provider returned a non-advancing continuation.");

            var pageStates = result.Rows
                .Select(GroundworkV2RuntimeLivenessCodec.Deserialize)
                .ToArray();
            foreach (var state in pageStates)
            {
                if (CanonicalRouteIndex(state, request, routes.Count) == index)
                    states[Identity(state.WorkflowExecutionId, state.OperationalStateId)] = state;
            }

            var nextRouteToken = RuntimeStorePageRequest.ValidateContinuationToken(
                result.NextContinuationToken,
                nameof(result.NextContinuationToken));
            nextCursors[index] = new RouteCursor(true, nextRouteToken);
            routeStates[index] = pageStates.FirstOrDefault();
            frontiers[index] = nextRouteToken is null
                ? RouteFrontier.Complete
                : pageStates.Length == 0
                    ? RouteFrontier.Unknown
                    : RouteFrontier.For(pageStates[^1], index, request);
        }

        // A pending hold walk is a protected identity, not a snapshot of a candidate. The route row normally gets
        // re-read because its route cursor is rewound below; if its current signal moved to another canonical route,
        // however, no route page may contain it. Re-read that identity through the admitted liveness index so a
        // concurrent signal update cannot permanently drop the pending candidate or bypass fresh correlation.
        if (continuation?.Pending is { } pending &&
            !states.ContainsKey(Identity(pending.WorkflowExecutionId, pending.OperationalStateId)) &&
            QueryPendingLiveness(pending.WorkflowExecutionId, pending.OperationalStateId, cancellationToken) is { } pendingState)
        {
            states[Identity(pendingState.WorkflowExecutionId, pendingState.OperationalStateId)] = pendingState;
        }

        var candidates = RuntimeRecoveryCandidateSelector.Select(
            states.Values,
            new RuntimeRecoveryScanRequest(
                request.Now,
                request.LeaseTimeout,
                request.HeartbeatTimeout,
                RuntimeStorePageRequest.MaximumLimit,
                request.OwnerId));
        var correlation = Correlate(candidates, states, request, continuation?.Pending, routes.Count, cancellationToken);
        var correlated = correlation.Items
            .Select(candidate => new OrderedCandidate(
                candidate,
                EligibleAt(states[Identity(candidate)], request)))
            .ToArray();

        var ordered = correlated
            .GroupBy(candidate => Identity(candidate.Candidate), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.EligibleAt)
            .ThenBy(candidate => candidate.Candidate.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Candidate.OperationalStateId, StringComparer.Ordinal)
            .ToArray();
        var frontier = frontiers
            .Where(frontier => !frontier.IsComplete)
            .OrderBy(frontier => frontier.IsKnown ? frontier.SignalAt : DateTimeOffset.MinValue)
            .ThenBy(frontier => frontier.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(frontier => frontier.OperationalStateId, StringComparer.Ordinal)
            .FirstOrDefault();
        var safe = frontier.IsKnown
            ? ordered.Where(candidate => Compare(candidate, frontier) <= 0).ToArray()
            : frontiers.Any(frontier => !frontier.IsComplete)
                ? []
                : ordered;

        var emitted = safe
            .Take(request.Limit)
            .Select(candidate => Identity(candidate.Candidate))
            .ToHashSet(StringComparer.Ordinal);
        var selectedByIdentity = candidates.ToDictionary(Identity, StringComparer.Ordinal);
        for (var index = 0; index < routeStates.Length; index++)
        {
            if (routeStates[index] is not { } state || CanonicalRouteIndex(state, request, routes.Count) != index)
                continue;

            var identity = Identity(state.WorkflowExecutionId, state.OperationalStateId);
            if (selectedByIdentity.ContainsKey(identity) &&
                !correlation.Rejected.Contains(identity) &&
                !emitted.Contains(identity))
                nextCursors[index] = routeCursors[index];
        }

        var pendingToCarry = correlation.Pending;
        if (pendingToCarry is { HoldScanComplete: true } completedPending &&
            emitted.Contains(Identity(completedPending.WorkflowExecutionId, completedPending.OperationalStateId)))
        {
            pendingToCarry = null;
        }

        return ValueTask.FromResult(BuildPage(
            request,
            routes.Count,
            nextCursors,
            safe,
            pendingToCarry,
            cancellationToken));
    }

    private const int RecoveryProviderPageLimit = 1;

    private static int Compare(OrderedCandidate candidate, RouteFrontier frontier)
    {
        var comparison = candidate.EligibleAt.CompareTo(frontier.SignalAt);
        if (comparison != 0)
            return comparison;

        comparison = StringComparer.Ordinal.Compare(candidate.Candidate.WorkflowExecutionId, frontier.WorkflowExecutionId);
        if (comparison != 0)
            return comparison;

        return StringComparer.Ordinal.Compare(candidate.Candidate.OperationalStateId, frontier.OperationalStateId);
    }

    private RuntimeRecoveryPage BuildPage(
        RuntimeRecoveryScanRequest request,
        int routeCount,
        IReadOnlyList<RouteCursor> nextCursors,
        IReadOnlyCollection<OrderedCandidate> available,
        PendingCorrelation? pending,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ordered = available
            .OrderBy(candidate => candidate.EligibleAt)
            .ThenBy(candidate => candidate.Candidate.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Candidate.OperationalStateId, StringComparer.Ordinal)
            .ToArray();
        var pageItems = ordered.Take(request.Limit).Select(candidate => candidate.Candidate).ToArray();
        var hasMoreRoutes = nextCursors.Any(cursor => !cursor.Started || cursor.ContinuationToken is not null);
        var nextContinuation = hasMoreRoutes || pending is not null
            ? EncodeContinuation(nextCursors, Binding(request, routeCount), pending)
            : null;

        cancellationToken.ThrowIfCancellationRequested();
        return new RuntimeRecoveryPage(request, pageItems, nextContinuation);
    }

    private CorrelationResult Correlate(
        IReadOnlyCollection<RuntimeRecoveryCandidate> candidates,
        IReadOnlyDictionary<string, ExecutionLivenessState> states,
        RuntimeRecoveryScanRequest request,
        PendingCorrelation? previousPending,
        int routeCount,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return new([], new HashSet<string>(StringComparer.Ordinal), null);

        var correlated = new List<RuntimeRecoveryCandidate>(candidates.Count);
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        PendingCorrelation? pending = null;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = Identity(candidate);
            var execution = QueryWorkflowExecution(candidate.WorkflowExecutionId, cancellationToken);
            if (execution is null || execution.Status.IsTerminal())
            {
                rejected.Add(identity);
                continue;
            }

            // Presence is all recovery needs from each one-to-many incident/scheduler store. Holds are walked by
            // bounded provider pages below; an unfinished hold walk is retained as a protected identity/cursor
            // continuation and is re-correlated against all current stores on the next public page call.
            var hasIncident = QueryIncident(candidate.WorkflowExecutionId, cancellationToken);
            var hasScheduler = QueryScheduler(candidate.WorkflowExecutionId, cancellationToken);
            var routeIndex = CanonicalRouteIndex(states[identity], request, routeCount);
            var pendingIdentity = previousPending is { } prior &&
                                   prior.RouteIndex == routeIndex &&
                                   StringComparer.Ordinal.Equals(prior.WorkflowExecutionId, candidate.WorkflowExecutionId) &&
                                   StringComparer.Ordinal.Equals(prior.OperationalStateId, candidate.OperationalStateId);
            var hold = pendingIdentity && previousPending!.HoldScanComplete
                ? new HoldCorrelationResult(false, null)
                : QueryEffectiveHold(
                    candidate.WorkflowExecutionId,
                    pendingIdentity ? previousPending!.HoldContinuationToken : null,
                    cancellationToken);
            if (hold.IsHeld)
            {
                rejected.Add(identity);
                continue;
            }

            if (hold.NextContinuationToken is { } nextHoldContinuation)
            {
                // Keep only the identity and provider cursor in the protected continuation. The liveness row is
                // re-read and all five stores are re-correlated on the next page, so a stale candidate payload can
                // never bypass a terminal execution or a newly created hold.
                pending = new PendingCorrelation(
                    routeIndex,
                    candidate.WorkflowExecutionId,
                    candidate.OperationalStateId!,
                    nextHoldContinuation,
                    HoldScanComplete: false);
                break;
            }

            if (pendingIdentity)
            {
                // The hold walk completed on this page. Keep only the identity while global ordering or the caller's
                // limit may still defer emission; the next page will re-correlate all stores without repeating the
                // completed one-to-many hold walk.
                pending = new PendingCorrelation(
                    routeIndex,
                    candidate.WorkflowExecutionId,
                    candidate.OperationalStateId!,
                    HoldContinuationToken: null,
                    HoldScanComplete: true);
            }

            correlated.Add(WithCorrelationMetadata(candidate, execution, hasIncident, hasScheduler));
        }

        return new(correlated, rejected, pending);
    }

    private WorkflowExecutionState? QueryWorkflowExecution(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        // Workflow execution state has a stable runtime row key. Use the admitted point lookup rather than a
        // history projection query: this keeps the correlation read bounded and avoids relying on an index that
        // was designed for history ordering rather than current-state identity.
        var entry = Open(workflowExecutionUnit).Read(GroundworkRuntimeRowStore.Key(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        if (entry is null)
            return null;

        var state = GroundworkV2WorkflowExecutionStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId))
        {
            throw new InvalidDataException(
                "Groundwork workflow-execution row identity does not match its requested recovery key.");
        }

        return state;
    }

    private ExecutionLivenessState? QueryPendingLiveness(
        string workflowExecutionId,
        string operationalStateId,
        CancellationToken cancellationToken)
    {
        var table = new TableId(context.Unit.Name);
        var collection = Column(context.Unit, table, ElsaRuntimeV2StorageManifest.CollectionField);
        var workflow = Column(context.Unit, table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var operational = Column(context.Unit, table, ElsaRuntimeV2StorageManifest.ExecutionLivenessOperationalStateIdField);
        var rows = context.Open().Query(new QueryRequest(
            table,
            And(
                Equal(collection, ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind),
                Equal(workflow, workflowExecutionId),
                Equal(operational, operationalStateId)),
            [
                new OrderTerm(workflow, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(operational, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            Paging.Keyset(1))).Rows;
        cancellationToken.ThrowIfCancellationRequested();
        var state = rows.Select(GroundworkV2RuntimeLivenessCodec.Deserialize).SingleOrDefault();
        if (state is not null &&
            (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId) ||
             !StringComparer.Ordinal.Equals(state.OperationalStateId, operationalStateId)))
        {
            throw new InvalidDataException("Groundwork pending recovery liveness identity does not match its lookup key.");
        }

        return state;
    }

    private bool QueryIncident(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var table = new TableId(incidentUnit.Name);
        var workflow = Column(incidentUnit, table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var rows = Open(incidentUnit).Query(new QueryRequest(
            table,
            Equal(workflow, workflowExecutionId),
            [new OrderTerm(workflow, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(1))).Rows;
        cancellationToken.ThrowIfCancellationRequested();
        return rows.Select(GroundworkV2IncidentStateStorageConventions.Deserialize).Any();
    }

    private bool QueryScheduler(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var table = new TableId(schedulerUnit.Name);
        var collection = Column(schedulerUnit, table, ElsaRuntimeV2StorageManifest.CollectionField);
        var workflow = Column(schedulerUnit, table, ElsaRuntimeV2StorageManifest.IdField);
        var rows = Open(schedulerUnit).Query(new QueryRequest(
            table,
            And(
                Equal(collection, ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind),
                Equal(workflow, workflowExecutionId)),
            [new OrderTerm(workflow, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(1))).Rows;
        cancellationToken.ThrowIfCancellationRequested();
        return rows.Select(GroundworkV2SchedulerStateStorageConventions.Deserialize).Any();
    }

    private HoldCorrelationResult QueryEffectiveHold(
        string workflowExecutionId,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var table = new TableId(workflowHoldUnit.Name);
        var collection = Column(workflowHoldUnit, table, ElsaRuntimeV2StorageManifest.CollectionField);
        var workflow = Column(workflowHoldUnit, table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var id = Column(workflowHoldUnit, table, ElsaRuntimeV2StorageManifest.IdField);

        // The continuation below is an ID cursor over inactive rows. Once a cursor exists, an active hold can be
        // inserted or activated at an earlier ID between calls. Recheck this exact workflow from the existing
        // workflow index and deserialize its current payload before following the saved cursor. The recheck is one
        // bounded page per pending candidate and carries no new persisted projection or schema requirement.
        if (continuationToken is not null)
        {
            var recheckRows = Open(workflowHoldUnit).Query(new QueryRequest(
                table,
                And(
                    Equal(collection, ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind),
                    Equal(workflow, workflowExecutionId)),
                [
                    new OrderTerm(workflow, OrderDirection.Ascending, NullOrder.Last),
                    new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)
                ],
                Projection.All,
                Paging.Keyset(RuntimeStorePageRequest.MaximumLimit))).Rows;
            cancellationToken.ThrowIfCancellationRequested();
            var recheckedStates = recheckRows
                .Select(GroundworkV2WorkflowHoldStateStorageConventions.Deserialize)
                .ToArray();
            if (recheckedStates.Any(state => state.ActiveHolds.Any(hold => hold.IsEffective)))
                return new(true, null);
        }

        var result = Open(workflowHoldUnit).Query(new QueryRequest(
            table,
            And(
                Equal(collection, ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind),
                Equal(workflow, workflowExecutionId)),
            [
                new OrderTerm(workflow, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            continuationToken is null
                ? Paging.Keyset(RuntimeStorePageRequest.MaximumLimit)
                : Paging.Continuation(continuationToken, RuntimeStorePageRequest.MaximumLimit)));
        cancellationToken.ThrowIfCancellationRequested();
        var states = result.Rows
            .Select(GroundworkV2WorkflowHoldStateStorageConventions.Deserialize)
            .ToArray();
        if (states.Any(state => state.ActiveHolds.Any(hold => hold.IsEffective)))
            return new(true, null);

        if (result.NextContinuationToken is { } next && StringComparer.Ordinal.Equals(next, continuationToken))
            throw new InvalidDataException("Groundwork recovery hold continuation did not advance.");

        return new(
            false,
            RuntimeStorePageRequest.ValidateContinuationToken(
                result.NextContinuationToken,
                nameof(result.NextContinuationToken)));
    }

    private IStorageSession Open(StorageUnit storageUnit)
    {
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException("Groundwork recovery persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork recovery requires one explicit persistence scope; global and across-scope access are refused.");
        }

        return sessions.Open(
            storageUnit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope.Value)),
            targetName);
    }

    private static RuntimeRecoveryCandidate WithCorrelationMetadata(
        RuntimeRecoveryCandidate candidate,
        WorkflowExecutionState execution,
        bool hasIncident,
        bool hasScheduler)
    {
        var metadata = candidate.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        metadata["runtime.recovery.correlation.execution"] = execution.Status.ToString();
        metadata["runtime.recovery.correlation.incident"] = hasIncident.ToString().ToLowerInvariant();
        metadata["runtime.recovery.correlation.scheduler"] = hasScheduler.ToString().ToLowerInvariant();
        metadata["runtime.recovery.correlation.hold"] = "false";
        return new RuntimeRecoveryCandidate(
            candidate.WorkflowExecutionId,
            candidate.OperationalStateId,
            candidate.LastCheckpointId,
            candidate.Reason,
            candidate.DetectedAt,
            candidate.RequeueFromLastCheckpoint,
            metadata);
    }

    private static string Identity(RuntimeRecoveryCandidate candidate) =>
        Identity(candidate.WorkflowExecutionId, candidate.OperationalStateId);

    private static string Identity(string workflowExecutionId, string? operationalStateId) =>
        $"{workflowExecutionId.Length}:{workflowExecutionId}{operationalStateId?.Length ?? -1}:{operationalStateId}";

    private static DateTimeOffset EligibleAt(
        ExecutionLivenessState state,
        RuntimeRecoveryScanRequest request)
    {
        var signals = new List<DateTimeOffset>(3);
        if (state.InterruptedExecution is { Status: RuntimeInterruptionStatus.Detected } interrupted &&
            DetectedInterruptionOwnerMatches(state, request.OwnerId))
        {
            signals.Add(interrupted.InterruptedAt);
        }

        if (state.ExecutionLease is { } lease && OwnerMatches(lease.OwnerId, request.OwnerId))
            signals.Add(LeaseDueAt(lease, request));

        if (state.Heartbeat is { } heartbeat && OwnerMatches(heartbeat.OwnerId, request.OwnerId))
            signals.Add(HeartbeatDueAt(heartbeat, request));

        return signals.Min();
    }

    // A liveness row can satisfy more than one native route (for example, a lease and heartbeat can both be due).
    // Assigning each row to the route representing its earliest signal makes route pages disjoint, so continuation
    // state does not need an unbounded emitted-identity set.
    private static int CanonicalRouteIndex(
        ExecutionLivenessState state,
        RuntimeRecoveryScanRequest request,
        int routeCount)
    {
        var signals = new List<(DateTimeOffset At, int Route, int Priority)>(3);
        if (state.InterruptedExecution is { Status: RuntimeInterruptionStatus.Detected } &&
            DetectedInterruptionOwnerMatches(state, request.OwnerId))
        {
            var route = request.OwnerId is null
                ? 0
                : state.ExecutionLease?.OwnerId == request.OwnerId
                    ? 0
                    : state.Heartbeat?.OwnerId == request.OwnerId
                        ? 1
                        : HasNoOperationalOwner(state)
                            ? 2
                            : -1;
            if (route >= 0)
                signals.Add((state.InterruptedExecution.InterruptedAt, route, 0));
        }

        if (state.ExecutionLease is { } lease && OwnerMatches(lease.OwnerId, request.OwnerId))
        {
            var due = LeaseDueAt(lease, request);
            if (due <= request.Now)
            {
                var acquisitionIsEarlier = lease.AcquiredAt.Add(request.LeaseTimeout) < lease.ExpiresAt;
                var route = request.OwnerId is null
                    ? acquisitionIsEarlier ? 2 : 1
                    : acquisitionIsEarlier ? 4 : 3;
                signals.Add((due, route, 1));
            }
        }

        if (state.Heartbeat is { } heartbeat && OwnerMatches(heartbeat.OwnerId, request.OwnerId))
        {
            var due = HeartbeatDueAt(heartbeat, request);
            if (due <= request.Now)
                signals.Add((due, request.OwnerId is null ? 3 : 5, 2));
        }

        if (signals.Count == 0)
            return -1;

        var routeIndex = signals
            .OrderBy(signal => signal.At)
            .ThenBy(signal => signal.Priority)
            .ThenBy(signal => signal.Route)
            .First()
            .Route;
        return routeIndex < routeCount ? routeIndex : -1;
    }

    private static bool DetectedInterruptionOwnerMatches(ExecutionLivenessState state, string? ownerId) =>
        ownerId is null
        || HasNoOperationalOwner(state)
        || StringComparer.Ordinal.Equals(state.ExecutionLease?.OwnerId, ownerId)
        || StringComparer.Ordinal.Equals(state.Heartbeat?.OwnerId, ownerId);

    private static bool HasNoOperationalOwner(ExecutionLivenessState state) =>
        state.ExecutionLease is null && state.Heartbeat is null;

    private static bool OwnerMatches(string sourceOwnerId, string? requestedOwnerId) =>
        requestedOwnerId is null || StringComparer.Ordinal.Equals(sourceOwnerId, requestedOwnerId);

    private static DateTimeOffset LeaseDueAt(RuntimeExecutionLease lease, RuntimeRecoveryScanRequest request) =>
        lease.ExpiresAt <= lease.AcquiredAt.Add(request.LeaseTimeout)
            ? lease.ExpiresAt
            : lease.AcquiredAt.Add(request.LeaseTimeout);

    private static DateTimeOffset HeartbeatDueAt(RuntimeHeartbeat heartbeat, RuntimeRecoveryScanRequest request) =>
        heartbeat.RecordedAt.Add(request.HeartbeatTimeout);

    private static IReadOnlyList<RecoveryRoute> Routes(RuntimeRecoveryScanRequest request, StorageUnit unit)
    {
        var table = new TableId(unit.Name);
        var status = Column(unit, table, ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField);
        var interruptedAt = Column(unit, table, ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField);
        var leaseOwner = Column(unit, table, ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField);
        var leaseAcquiredAt = Column(unit, table, ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField);
        var leaseExpiresAt = Column(unit, table, ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField);
        var heartbeatOwner = Column(unit, table, ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField);
        var heartbeatRecordedAt = Column(unit, table, ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField);
        var hasOwner = Column(unit, table, ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField);
        var workflow = Column(unit, table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var operational = Column(unit, table, ElsaRuntimeV2StorageManifest.ExecutionLivenessOperationalStateIdField);
        var detected = (int)RuntimeInterruptionStatus.Detected;

        if (request.OwnerId is null)
        {
            return
            [
                Route(Equal(status, detected), Order(interruptedAt, workflow, operational)),
                Route(Due(leaseExpiresAt, request.Now), Order(leaseExpiresAt, workflow, operational)),
                Route(Due(leaseAcquiredAt, request.Now.Subtract(request.LeaseTimeout)), Order(leaseAcquiredAt, workflow, operational)),
                Route(Due(heartbeatRecordedAt, request.Now.Subtract(request.HeartbeatTimeout)), Order(heartbeatRecordedAt, workflow, operational))
            ];
        }

        var owner = request.OwnerId;
        return
        [
            Route(And(Equal(status, detected), Equal(leaseOwner, owner)), Order(interruptedAt, workflow, operational)),
            Route(And(Equal(status, detected), Equal(heartbeatOwner, owner)), Order(interruptedAt, workflow, operational)),
            Route(And(Equal(status, detected), Equal(hasOwner, false)), Order(interruptedAt, workflow, operational)),
            Route(And(Equal(leaseOwner, owner), Due(leaseExpiresAt, request.Now)), Order(leaseExpiresAt, workflow, operational)),
            Route(And(Equal(leaseOwner, owner), Due(leaseAcquiredAt, request.Now.Subtract(request.LeaseTimeout))), Order(leaseAcquiredAt, workflow, operational)),
            Route(And(Equal(heartbeatOwner, owner), Due(heartbeatRecordedAt, request.Now.Subtract(request.HeartbeatTimeout))), Order(heartbeatRecordedAt, workflow, operational))
        ];
    }

    private static RecoveryRoute Route(Predicate where, IReadOnlyList<OrderTerm> order) => new(where, order);

    private static IReadOnlyList<OrderTerm> Order(params ColumnRef[] columns) =>
        columns.Select(column => new OrderTerm(column, OrderDirection.Ascending, NullOrder.Last)).ToArray();

    private static Predicate And(params Predicate[] predicates) => new Predicate.And(predicates);

    private static Predicate Equal(ColumnRef column, object value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate Due(ColumnRef column, DateTimeOffset value) =>
        new Predicate.Range(column, null, Bound.Inclusive(QueryConstant.Of(column, value)));

    private static ColumnRef Column(StorageUnit unit, TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork recovery unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork recovery query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private string EncodeContinuation(
        IReadOnlyList<RouteCursor> cursors,
        string binding,
        PendingCorrelation? pending)
    {
        var payload = new RecoveryContinuation(
            binding,
            cursors.Select(cursor => cursor.ToSnapshot()).ToArray(),
            pending);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var token = continuationCodec.Encode("rrs2", bytes);
        RuntimeStorePageRequest.ValidateContinuationToken(token, nameof(token));
        return token;
    }

    private RecoveryContinuation? DecodeContinuation(string? token, string binding, int routeCount)
    {
        if (token is null)
            return null;

        try
        {
            var payloadBytes = continuationCodec.Decode("rrs2", token);
            var payload = JsonSerializer.Deserialize<RecoveryContinuation>(payloadBytes)
                          ?? throw new InvalidDataException("Recovery continuation token is empty.");
            if (!StringComparer.Ordinal.Equals(payload.Binding, binding) ||
                payload.Cursors is null ||
                payload.Cursors.Count != routeCount ||
                payload.Cursors.Any(cursor => cursor is null ||
                                              (!cursor.Started && cursor.ContinuationToken is not null) ||
                                              cursor.ContinuationToken is { } routeToken &&
                                              !IsValidContinuationToken(routeToken)) ||
                payload.Pending is { } pending &&
                (pending.RouteIndex < 0 ||
                 pending.RouteIndex >= routeCount ||
                 string.IsNullOrWhiteSpace(pending.WorkflowExecutionId) ||
                 string.IsNullOrWhiteSpace(pending.OperationalStateId) ||
                 (!pending.HoldScanComplete && string.IsNullOrWhiteSpace(pending.HoldContinuationToken))))
                throw new InvalidDataException("Recovery continuation token does not belong to this recovery scan.");
            return payload;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or InvalidDataException)
        {
            throw new ArgumentException("The recovery continuation token is invalid.", nameof(token), exception);
        }
    }

    private sealed record RecoveryRoute(Predicate Where, IReadOnlyList<OrderTerm> Order);

    private readonly record struct RouteFrontier(
        bool IsKnown,
        bool IsComplete,
        DateTimeOffset SignalAt,
        string WorkflowExecutionId,
        string OperationalStateId)
    {
        public static RouteFrontier Unknown => new(false, false, default, "", "");
        public static RouteFrontier Complete => new(false, true, default, "", "");

        public static RouteFrontier For(
            ExecutionLivenessState state,
            int routeIndex,
            RuntimeRecoveryScanRequest request) =>
            new(
                true,
                false,
                RouteSignalAt(state, routeIndex, request),
                state.WorkflowExecutionId,
                state.OperationalStateId);
    }

    private static DateTimeOffset RouteSignalAt(
        ExecutionLivenessState state,
        int routeIndex,
        RuntimeRecoveryScanRequest request)
    {
        var interruptedAt = state.InterruptedExecution?.InterruptedAt;
        var lease = state.ExecutionLease;
        var heartbeat = state.Heartbeat;
        var signal = request.OwnerId is null
            ? routeIndex switch
            {
                0 => interruptedAt,
                1 => lease?.ExpiresAt,
                2 => lease?.AcquiredAt.Add(request.LeaseTimeout),
                3 => heartbeat?.RecordedAt.Add(request.HeartbeatTimeout),
                _ => null
            }
            : routeIndex switch
            {
                0 or 1 or 2 => interruptedAt,
                3 => lease?.ExpiresAt,
                4 => lease?.AcquiredAt.Add(request.LeaseTimeout),
                5 => heartbeat?.RecordedAt.Add(request.HeartbeatTimeout),
                _ => null
            };
        return signal ?? DateTimeOffset.MaxValue;
    }

    private string Binding(RuntimeRecoveryScanRequest request, int routeCount)
    {
        var accessContext = accessContextAccessor.Current ??
                            throw new InvalidOperationException("Groundwork recovery persistence access context is missing.");
        if (accessContext.Scope is null || accessContext.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork recovery requires one explicit persistence scope; global and across-scope access are refused.");
        }

        return $"recovery|{routeCount}|{request.Now.UtcTicks}|{request.LeaseTimeout.Ticks}|{request.HeartbeatTimeout.Ticks}|{request.OwnerId}|{accessContext.Scope.Value}|{targetName ?? "<default>"}";
    }

    private sealed record RecoveryContinuation(
        string Binding,
        IReadOnlyList<RouteCursorSnapshot> Cursors,
        PendingCorrelation? Pending);

    private sealed record PendingCorrelation(
        int RouteIndex,
        string WorkflowExecutionId,
        string OperationalStateId,
        string? HoldContinuationToken,
        bool HoldScanComplete);

    private sealed record HoldCorrelationResult(bool IsHeld, string? NextContinuationToken);

    private sealed record CorrelationResult(
        IReadOnlyCollection<RuntimeRecoveryCandidate> Items,
        IReadOnlySet<string> Rejected,
        PendingCorrelation? Pending);

    private readonly record struct RouteCursor(bool Started, string? ContinuationToken)
    {
        public RouteCursorSnapshot ToSnapshot() => new(Started, ContinuationToken);
    }

    private sealed record RouteCursorSnapshot(bool Started, string? ContinuationToken)
    {
        public RouteCursor ToRouteCursor() => new(Started, ContinuationToken);
    }

    private sealed record OrderedCandidate(RuntimeRecoveryCandidate Candidate, DateTimeOffset EligibleAt);

    private static bool IsValidContinuationToken(string token)
    {
        try
        {
            RuntimeStorePageRequest.ValidateContinuationToken(token, nameof(token));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
