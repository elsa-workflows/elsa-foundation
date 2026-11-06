using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Opaque server-owned source identity; neither a workflow identity nor an authorization token.</summary>
public sealed record RuntimeDeliverySourceAddress
{
    public RuntimeDeliverySourceAddress(string family, PersistenceScope scope, string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family); ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        Family = family; Scope = scope; SourceId = sourceId;
    }
    public string Family { get; }
    public PersistenceScope Scope { get; }
    public string SourceId { get; }
    public override string ToString() => nameof(RuntimeDeliverySourceAddress);
}

/// <summary>Immutable canonical-source read value. The provider verifies its content fingerprint and authority.</summary>
public sealed class RuntimeDeliverySourceSnapshot
{
    public RuntimeDeliverySourceSnapshot(RuntimeDeliverySourceAddress address, int headerVersion, long revision,
        string headerFingerprint, JsonElement header, WorkflowExecutionAuthoritySnapshot authority,
        string? tenantId = null, WorkflowRunKind runKind = WorkflowRunKind.Unknown, WorkflowTestScope? testScope = null)
    {
        ArgumentNullException.ThrowIfNull(address); ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerFingerprint);
        if (headerVersion <= 0) throw new ArgumentOutOfRangeException(nameof(headerVersion));
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (header.ValueKind != JsonValueKind.Object) throw new ArgumentException("Source header must be an object.", nameof(header));
        if (tenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!Enum.IsDefined(runKind)) throw new ArgumentOutOfRangeException(nameof(runKind));
        if (testScope is not null && (runKind != WorkflowRunKind.TestRun || testScope.TenantId != tenantId))
            throw new ArgumentException("Test scope must match source run kind and tenant.", nameof(testScope));
        Address = address; HeaderVersion = headerVersion; Revision = revision; HeaderFingerprint = headerFingerprint;
        Header = header.Clone(); Authority = authority; TenantId = tenantId; RunKind = runKind; TestScope = testScope;
    }
    public RuntimeDeliverySourceAddress Address { get; }
    public int HeaderVersion { get; }
    public long Revision { get; }
    public string HeaderFingerprint { get; }
    public JsonElement Header { get; }
    public WorkflowExecutionAuthoritySnapshot Authority { get; }
    public string? TenantId { get; }
    public WorkflowRunKind RunKind { get; }
    public WorkflowTestScope? TestScope { get; }
    public override string ToString() => nameof(RuntimeDeliverySourceSnapshot);
}

public enum RuntimeDeliveryWorkRole { Plan = 0, Dispatch = 1 }

/// <summary>Immutable physical claim value. Fresh ownership/expiry and recipient capability require provider rereads.</summary>
public sealed class RuntimeDeliveryWorkClaim
{
    public RuntimeDeliveryWorkClaim(RuntimeDeliverySourceAddress source, long sourceRevision, string headerFingerprint,
        string workId, string workKind, RuntimeDeliveryWorkRole role, JsonElement content, string contentFingerprint,
        long revision, string ownerId, long fencingToken, DateTimeOffset claimedAt, DateTimeOffset visibleAfter)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentException.ThrowIfNullOrWhiteSpace(headerFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(workId); ArgumentException.ThrowIfNullOrWhiteSpace(workKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFingerprint); ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (sourceRevision <= 0) throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (fencingToken <= 0) throw new ArgumentOutOfRangeException(nameof(fencingToken));
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (content.ValueKind != JsonValueKind.Object) throw new ArgumentException("Work content must be an object.", nameof(content));
        if (claimedAt == default || visibleAfter <= claimedAt) throw new ArgumentOutOfRangeException(nameof(visibleAfter));
        Source = source; SourceRevision = sourceRevision; HeaderFingerprint = headerFingerprint; WorkId = workId;
        WorkKind = workKind; Role = role; Content = content.Clone(); ContentFingerprint = contentFingerprint;
        Revision = revision; OwnerId = ownerId; FencingToken = fencingToken; ClaimedAt = claimedAt; VisibleAfter = visibleAfter;
    }
    public RuntimeDeliverySourceAddress Source { get; }
    public long SourceRevision { get; }
    public string HeaderFingerprint { get; }
    public string WorkId { get; }
    public string WorkKind { get; }
    public RuntimeDeliveryWorkRole Role { get; }
    public JsonElement Content { get; }
    public string ContentFingerprint { get; }
    public long Revision { get; }
    public string OwnerId { get; }
    public long FencingToken { get; }
    public DateTimeOffset ClaimedAt { get; }
    public DateTimeOffset VisibleAfter { get; }
    public override string ToString() => nameof(RuntimeDeliveryWorkClaim);
}

/// <summary>Structurally coherent read/claim pair, not proof of present authority or physical commit.</summary>
public sealed class RuntimeDeliveryContext
{
    public RuntimeDeliveryContext(RuntimeDeliverySourceSnapshot source, RuntimeDeliveryWorkClaim claim)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(claim);
        if (source.Address != claim.Source || source.Revision != claim.SourceRevision || source.HeaderFingerprint != claim.HeaderFingerprint)
            throw new ArgumentException("Source and work claim differ.", nameof(claim));
        Source = source; Claim = claim;
    }
    public RuntimeDeliverySourceSnapshot Source { get; }
    public RuntimeDeliveryWorkClaim Claim { get; }
    public override string ToString() => nameof(RuntimeDeliveryContext);
}

/// <summary>Bounded claim request; unsupported or unspecified discovery context refuses before reads/claim mutation.</summary>
/// <remarks>The preserved constructor leaves Partition unspecified, never a wildcard. Providers require the explicit
/// trusted partition, full predicates and current canonical family authority before Take and again before mutation.
/// This unregistered provider request is not persisted wire authority. Its original collection constructor/property
/// types retain the pre-existing default JSON reader limitation; JsonConstructor selects the preserved constructor
/// without providing a general serializer policy.</remarks>
public sealed class RuntimeDeliveryClaimRequest
{
    private RuntimeDeliveryDiscoveryPartition? partition;

    [JsonConstructor]
    public RuntimeDeliveryClaimRequest(PersistenceScope scope, IReadOnlyCollection<string> families,
        IReadOnlyCollection<string> workKinds, string ownerId, TimeSpan visibility, int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (visibility <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(visibility));
        if (limit is <= 0 or > RuntimeStorePageRequest.MaximumLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        Scope = scope; Families = RuntimeDeliveryValues.Names(families); WorkKinds = RuntimeDeliveryValues.Names(workKinds);
        OwnerId = ownerId; Visibility = visibility; Limit = limit;
    }
    /// <summary>Supplies an explicit exact discovery partition while preserving the original constructor shape.</summary>
    public RuntimeDeliveryClaimRequest(PersistenceScope scope, IReadOnlyCollection<string> families,
        IReadOnlyCollection<string> workKinds, string ownerId, TimeSpan visibility, int limit,
        RuntimeDeliveryDiscoveryPartition partition)
        : this(scope, families, workKinds, ownerId, visibility, limit)
    {
        ArgumentNullException.ThrowIfNull(partition);
        Partition = partition;
    }

    public PersistenceScope Scope { get; }
    /// <summary>Null is an explicitly unsupported legacy request, never across-tenant/run/test/authority discovery.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeDeliveryDiscoveryPartition? Partition
    {
        get => partition;
        init
        {
            if (value is not null && value.Scope != Scope)
                throw new ArgumentException("Claim scope differs from its discovery partition.", nameof(Partition));
            partition = value;
        }
    }
    public IReadOnlyList<string> Families { get; }
    public IReadOnlyList<string> WorkKinds { get; }
    public string OwnerId { get; }
    public TimeSpan Visibility { get; }
    public int Limit { get; }

    /// <summary>Requires exact trusted current context, ordinary same-scope access and an unexpired test lifetime.</summary>
    /// <remarks>This structural check grants no authority. The provider supplies checked canonical family/host context,
    /// compares full authority metadata before Take, and rereads test-ledger/claim/source/schema state in its transaction.
    /// C2 derives authority from CreateRoot(retained RequestedBy), never business metadata or a later ambient caller.</remarks>
    public RuntimeDeliveryDiscoveryPartition RequirePartition(PersistenceAccessContext access,
        RuntimeDeliveryDiscoveryPartition currentPartition, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(access); ArgumentNullException.ThrowIfNull(currentPartition);
        var partition = Partition ?? throw new NotSupportedException("The claim request has no explicit discovery partition.");
        partition.ValidateCurrent(access, observedAt);
        if (!partition.Matches(currentPartition))
            throw new InvalidOperationException("The claim request differs from its trusted current discovery context.");
        return partition;
    }

    public override string ToString() => nameof(RuntimeDeliveryClaimRequest);
}

/// <summary>Actual selected scoped stores. Merely constructing this tuple never qualifies atomic participation.</summary>
public sealed class RuntimeDeliveryBoundary
{
    public RuntimeDeliveryBoundary(PersistenceScope scope, IRuntimeDeliverySourceStore sources,
        IRuntimeCheckpointCommitStore checkpoints, IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore materials,
        IWorkflowExecutableSourceReferenceStore references, IDurableValueStateStore values, IWorkflowSchedulerWorkQueue scheduler,
        IWorkflowExecutionStateStore executions, IActivityExecutionStateStore activities, IBookmarkStateStore bookmarks,
        IRuntimeModelExecutionReader? modelReader = null)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(sources); ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(outbox); ArgumentNullException.ThrowIfNull(materials); ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(values); ArgumentNullException.ThrowIfNull(scheduler); ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(activities); ArgumentNullException.ThrowIfNull(bookmarks);
        Scope = scope; Sources = sources; Checkpoints = checkpoints; Outbox = outbox; Materials = materials; References = references;
        Values = values; Scheduler = scheduler; Executions = executions; Activities = activities; Bookmarks = bookmarks; ModelReader = modelReader;
    }
    public PersistenceScope Scope { get; }
    public IRuntimeDeliverySourceStore Sources { get; }
    public IRuntimeCheckpointCommitStore Checkpoints { get; }
    public IRuntimePostCommitOutboxStore Outbox { get; }
    public IWorkflowExecutableStore Materials { get; }
    public IWorkflowExecutableSourceReferenceStore References { get; }
    public IDurableValueStateStore Values { get; }
    public IWorkflowSchedulerWorkQueue Scheduler { get; }
    public IWorkflowExecutionStateStore Executions { get; }
    public IActivityExecutionStateStore Activities { get; }
    public IBookmarkStateStore Bookmarks { get; }
    public IRuntimeModelExecutionReader? ModelReader { get; }
    public override string ToString() => nameof(RuntimeDeliveryBoundary);
}

/// <summary>Closed-family proposal tied to an exact observed source/work, not an arbitrary mutation callback.</summary>
public sealed class RuntimeSourceCommit
{
    public RuntimeSourceCommit(RuntimeDeliveryContext context, string operationId, string operationFingerprint, IRuntimeSourcePlan plan)
    {
        ArgumentNullException.ThrowIfNull(context); RuntimeDeliveryValues.Plan(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentException.ThrowIfNullOrWhiteSpace(operationFingerprint);
        if (plan.Family != context.Source.Address.Family) throw new ArgumentException("Plan belongs to another family.", nameof(plan));
        Context = context; OperationId = operationId; OperationFingerprint = operationFingerprint; Plan = plan;
    }
    public RuntimeDeliveryContext Context { get; }
    public string OperationId { get; }
    public string OperationFingerprint { get; }
    public IRuntimeSourcePlan Plan { get; }
    public override string ToString() => nameof(RuntimeSourceCommit);
}

public enum RuntimeSourceCommitOutcome { Committed = 0, Replayed = 1, Conflict = 2, Superseded = 3 }

/// <summary>Canonical persisted winner/proof. An uncertain commit is an exception, never an assumed outcome.</summary>
public sealed class RuntimeSourceCommitResult
{
    public RuntimeSourceCommitResult(RuntimeSourceCommitOutcome outcome, RuntimeDeliverySourceSnapshot source, string operationId, string operationFingerprint)
    {
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        ArgumentNullException.ThrowIfNull(source); ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationFingerprint);
        Outcome = outcome; Source = source; OperationId = operationId; OperationFingerprint = operationFingerprint;
    }
    public RuntimeSourceCommitOutcome Outcome { get; }
    public RuntimeDeliverySourceSnapshot Source { get; }
    public string OperationId { get; }
    public string OperationFingerprint { get; }
    public override string ToString() => nameof(RuntimeSourceCommitResult);
}

/// <summary>Candidate target-proof references only; transport success cannot assert consumption or derive follow-ups.</summary>
public sealed class RuntimeDeliveryAttempt
{
    public RuntimeDeliveryAttempt(IReadOnlyCollection<string> candidateProofReferences) =>
        CandidateProofReferences = RuntimeDeliveryValues.Names(candidateProofReferences, allowEmpty: true);
    public IReadOnlyList<string> CandidateProofReferences { get; }
    public override string ToString() => nameof(RuntimeDeliveryAttempt);
}

/// <summary>Read-only preparation for the existing native claim-completion transaction; it is not acceptance.</summary>
public sealed class RuntimePostCommitSourceFinalization
{
    public RuntimePostCommitSourceFinalization(int version, string operationId, string operationFingerprint, IRuntimeSourcePlan plan)
    {
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        RuntimeDeliveryValues.Plan(plan); ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationFingerprint);
        Version = version; OperationId = operationId; OperationFingerprint = operationFingerprint; Plan = plan;
    }
    public int Version { get; }
    public string OperationId { get; }
    public string OperationFingerprint { get; }
    public IRuntimeSourcePlan Plan { get; }
    public override string ToString() => nameof(RuntimePostCommitSourceFinalization);
}

public sealed class RuntimePostCommitDispatchPreparation
{
    private RuntimePostCommitDispatchPreparation(RuntimePostCommitSourceFinalization? finalization) => Finalization = finalization;
    public static RuntimePostCommitDispatchPreparation LegacyHandled { get; } = new(null);
    public static RuntimePostCommitDispatchPreparation FromFinalization(RuntimePostCommitSourceFinalization finalization)
    {
        ArgumentNullException.ThrowIfNull(finalization);
        return new(finalization);
    }
    public RuntimePostCommitSourceFinalization? Finalization { get; }
    public override string ToString() => nameof(RuntimePostCommitDispatchPreparation);
}

/// <summary>Additive scoped family contribution; exact sealed plan types remain subject to provider recognition.</summary>
public sealed class RuntimeDeliverySourceFamilyContribution
{
    public RuntimeDeliverySourceFamilyContribution(string family, int headerVersion, int participantVersion,
        Type plannerType, Type dispatchHandlerType, IReadOnlyCollection<Type> planTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (headerVersion <= 0) throw new ArgumentOutOfRangeException(nameof(headerVersion));
        if (participantVersion <= 0) throw new ArgumentOutOfRangeException(nameof(participantVersion));
        RuntimeDeliveryValues.Implementation(plannerType, typeof(IRuntimeDeliverySourcePlanner));
        RuntimeDeliveryValues.Implementation(dispatchHandlerType, typeof(IRuntimeDeliveryDispatchHandler));
        ArgumentNullException.ThrowIfNull(planTypes);
        var plans = planTypes.ToArray();
        if (plans.Length == 0 || plans.Distinct().Count() != plans.Length) throw new ArgumentException("Plan types must be nonempty and unique.", nameof(planTypes));
        foreach (var type in plans)
        {
            RuntimeDeliveryValues.Implementation(type, typeof(IRuntimeSourcePlan));
            if (!type.IsSealed) throw new ArgumentException("Plan types must be sealed.", nameof(planTypes));
        }
        Family = family; HeaderVersion = headerVersion; ParticipantVersion = participantVersion;
        PlannerType = plannerType; DispatchHandlerType = dispatchHandlerType; PlanTypes = Array.AsReadOnly(plans);
    }
    public string Family { get; }
    public int HeaderVersion { get; }
    public int ParticipantVersion { get; }
    public Type PlannerType { get; }
    public Type DispatchHandlerType { get; }
    public IReadOnlyList<Type> PlanTypes { get; }
    public override string ToString() => nameof(RuntimeDeliverySourceFamilyContribution);
}

internal static class RuntimeDeliveryValues
{
    internal static IReadOnlyList<string> Names(IReadOnlyCollection<string> values, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if ((!allowEmpty && copy.Length == 0) || copy.Any(string.IsNullOrWhiteSpace) || copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Values must contain unique nonblank entries.", nameof(values));
        return Array.AsReadOnly(copy);
    }
    internal static void Implementation(Type type, Type contract)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || !contract.IsAssignableFrom(type))
            throw new ArgumentException("A concrete compatible implementation type is required.", nameof(type));
    }
    internal static void Plan(IRuntimeSourcePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentException.ThrowIfNullOrWhiteSpace(plan.Family);
        if (!plan.GetType().IsClass || !plan.GetType().IsSealed || plan.Version <= 0)
            throw new ArgumentException("A sealed versioned family plan is required.", nameof(plan));
    }
}
