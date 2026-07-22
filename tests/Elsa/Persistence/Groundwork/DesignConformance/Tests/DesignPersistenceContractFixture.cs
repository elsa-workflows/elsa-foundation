using System.Collections.ObjectModel;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Provider-neutral boundary for the shared design-persistence scenarios. Provider fixtures own
/// materialization and lifecycle details; scenarios receive only a scoped service provider and
/// explicit restart/readiness operations.
/// </summary>
public interface IDesignPersistenceContractFixture : IAsyncDisposable
{
    /// <summary>The provider identity written into scenario evidence, for example <c>sqlite</c>.</summary>
    string Provider { get; }

    /// <summary>
    /// Creates a fresh scope-bound service provider for one ordinary design request. The returned
    /// provider is bound to <paramref name="storageScope"/> for every resolved design store and
    /// command; implementations must not reuse a provider or an access context from another scope.
    /// </summary>
    IServiceScope CreateScope(string storageScope);

    /// <summary>Closes and reopens the same durable target without changing its contents.</summary>
    Task RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates the selected schema and provider capabilities without applying changes.</summary>
    Task ValidateReadinessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the supplied candidates visible to the fixture's normal reconciliation source.
    /// This method MUST NOT persist candidates or invoke reconciliation; the shared suite verifies
    /// pre-reconciliation absence and invokes the real domain reconciler from the request scope.
    /// </summary>
    Task StageActivityReconciliationCandidatesAsync(
        string storageScope,
        IReadOnlyCollection<ActivityDefinitionVersion> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the event/outcome observation window without changing durable state.</summary>
    void ClearObservedEvents();

    /// <summary>
    /// Returns events emitted through the composed domain event infrastructure since the last
    /// clear. Implementations wait for already-published deferred events to become observable.
    /// </summary>
    Task<IReadOnlyList<IEvent>> ReadObservedEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Arms one provider-neutral fault at a named point in the next atomic design operation. The
    /// returned lease reports whether the point was reached and disarms the fault when disposed.
    /// </summary>
    Task<IDesignAtomicityFaultLease> ArmAtomicityFaultAsync(
        DesignAtomicityFaultPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the fixture's canonical multi-document design operation. The caller supplies a
    /// stable operation key independently from the canonical request fingerprint so retries and
    /// conflicting key reuse can be observed without exposing provider mechanics to the suite.
    /// </summary>
    Task<DesignAtomicityOperationResult> ExecuteAtomicityOperationAsync(
        DesignAtomicityOperationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the observable state of the fixture's canonical multi-document operation. The counts
    /// intentionally describe logical aggregate parts, durable outcomes, and a fixture-local
    /// post-commit completion observation rather than provider documents, tables, transactions,
    /// SDK types, or domain-event publication. Provider-specific composed-host tests observe public
    /// command/event durability separately.
    /// </summary>
    Task<DesignAtomicitySnapshot> ReadAtomicitySnapshotAsync(
        string storageScope,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates one durable fixture per mandatory provider without exposing provider SDK types to scenarios.</summary>
public interface IDesignPersistenceContractFixtureFactory
{
    string Provider { get; }

    Task<IDesignPersistenceContractFixture> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral point at which a fixture can inject one atomic-write fault.</summary>
public enum DesignAtomicityFaultPhase
{
    AfterStagedWrite,
    BeforeProviderDecision,
    AfterDurableDecision
}

/// <summary>Provider-neutral behavior injected at an atomic-write fault point.</summary>
public enum DesignAtomicityFaultAction
{
    Throw,
    Cancel,
    ReturnNonSuccess
}

/// <summary>Immutable one-shot plan for an atomic-write conformance fault.</summary>
public sealed record DesignAtomicityFaultPlan
{
    public DesignAtomicityFaultPlan(DesignAtomicityFaultPhase phase, DesignAtomicityFaultAction action)
    {
        if (phase == DesignAtomicityFaultPhase.AfterDurableDecision && action == DesignAtomicityFaultAction.ReturnNonSuccess)
        {
            throw new ArgumentException(
                "A non-success provider decision cannot occur after the provider has made a durable decision.",
                nameof(action));
        }

        Phase = phase;
        Action = action;
    }

    public DesignAtomicityFaultPhase Phase { get; }
    public DesignAtomicityFaultAction Action { get; }
}

/// <summary>Owns an armed fault and reports whether the configured point was reached.</summary>
public interface IDesignAtomicityFaultLease : IAsyncDisposable
{
    bool WasTriggered { get; }
}

/// <summary>
/// Caller-controlled stable identity for exactly-once operation replay. This is intentionally not
/// a request fingerprint: the same key with a different fingerprint is a conflict.
/// </summary>
public sealed record DesignAtomicityOperationKey
{
    public DesignAtomicityOperationKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}

/// <summary>
/// Canonical representation of the requested operation content. Providers persist and compare it
/// against the caller-supplied <see cref="DesignAtomicityOperationKey"/>; it is not an idempotency key.
/// </summary>
public sealed record DesignCanonicalRequestFingerprint
{
    public DesignCanonicalRequestFingerprint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Provider-neutral request for the canonical multi-document atomicity scenario.</summary>
public sealed record DesignAtomicityOperationRequest(
    string StorageScope,
    DesignAtomicityOperationKey OperationKey,
    DesignCanonicalRequestFingerprint CanonicalRequestFingerprint);

/// <summary>Observable terminal classification for one atomicity operation attempt.</summary>
public enum DesignAtomicityOperationStatus
{
    Committed,
    Replayed,
    Rejected,
    Conflict
}

/// <summary>Authoritative result returned for a committed, replayed, rejected, or conflicting operation.</summary>
public sealed record DesignAtomicityOperationResult(
    DesignAtomicityOperationStatus Status,
    string? AuthoritativeResultFingerprint);

/// <summary>
/// Proves that a provider acknowledged a same-scope duplicate and made that duplicate observable.
/// The target-baseline runner recognizes only this sentinel as the intentional T024 red outcome.
/// </summary>
public sealed class DesignDuplicateIdentityAcceptedException()
    : Exception("A same-scope duplicate identity was acknowledged and became observable.")
{
    public const string Classification = "same-scope-identity-rejection-absent";
}

/// <summary>Provider-neutral observable state for the canonical multi-document operation.</summary>
public sealed record DesignAtomicitySnapshot(
    int VisibleAggregatePartCount,
    int ExpectedAggregatePartCount,
    int DurableOutcomeCount,
    int PostCommitOutcomeCount,
    string? CanonicalAggregateStateFingerprint,
    string? AuthoritativeDurableResultFingerprint);

/// <summary>One executable T023/T024 scenario whose applicability differs by conformance profile.</summary>
public enum DesignPersistenceContractScenario
{
    AtomicityPartialStagingFailure,
    AtomicityNonSuccessProviderDecision,
    AtomicityCancellation,
    AtomicityLostAcknowledgement,
    AtomicityExactReplay,
    AtomicityKeyReuseConflict,
    AtomicityDuplicateDelivery,
    AtomicityProjectionOverLimitRejection,
    IsolationSamePointIdentities,
    IsolationForeignPointReads,
    IsolationForeignScopeWrites,
    IsolationDuplicateIdentities,
    IsolationReusableActivityDraftOcc,
    IsolationWorkflowDraftLastWriterWins,
    IsolationSingleScopeRestart,
    IsolationCrossScopeSameIdentityRestart,
    QueryScalePagedListing,
    QueryScaleBatchProjection,
    QueryScaleOversizedMembership,
    QueryScaleKnownBatchParity,
    QueryScaleExistenceConsistency
}

/// <summary>Whether a profile can execute a scenario, with an explicit reason when it cannot.</summary>
public sealed record DesignPersistenceScenarioApplicability
{
    private DesignPersistenceScenarioApplicability(bool isApplicable, string? notApplicableReason)
    {
        IsApplicable = isApplicable;
        NotApplicableReason = notApplicableReason;
    }

    public bool IsApplicable { get; }
    public string? NotApplicableReason { get; }

    public static DesignPersistenceScenarioApplicability Applicable() => new(true, null);

    public static DesignPersistenceScenarioApplicability NotApplicable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(false, reason);
    }
}

/// <summary>
/// Immutable applicability declaration for all executable T023/T024 scenarios. It separates the
/// target conformance contract from the deliberately narrower temporary legacy-EF oracle.
/// </summary>
public sealed class DesignPersistenceContractProfile
{
    private readonly IReadOnlyDictionary<DesignPersistenceContractScenario, DesignPersistenceScenarioApplicability> _applicability;

    public DesignPersistenceContractProfile(
        string name,
        IReadOnlyDictionary<DesignPersistenceContractScenario, DesignPersistenceScenarioApplicability> applicability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(applicability);

        var expected = Enum.GetValues<DesignPersistenceContractScenario>();
        var missing = expected.Except(applicability.Keys).ToArray();
        var unexpected = applicability.Keys.Except(expected).ToArray();

        if (missing.Length > 0 || unexpected.Length > 0)
            throw new ArgumentException("A design persistence contract profile must declare every T023/T024 scenario exactly once.", nameof(applicability));

        if (applicability.Values.Any(value => value is null || (!value.IsApplicable && string.IsNullOrWhiteSpace(value.NotApplicableReason))))
            throw new ArgumentException("Every non-applicable scenario must provide a reason.", nameof(applicability));

        Name = name;
        _applicability = new ReadOnlyDictionary<DesignPersistenceContractScenario, DesignPersistenceScenarioApplicability>(
            new Dictionary<DesignPersistenceContractScenario, DesignPersistenceScenarioApplicability>(applicability));
    }

    public string Name { get; }
    public IReadOnlyDictionary<DesignPersistenceContractScenario, DesignPersistenceScenarioApplicability> Applicability => _applicability;

    public DesignPersistenceScenarioApplicability GetApplicability(DesignPersistenceContractScenario scenario) =>
        _applicability.TryGetValue(scenario, out var applicability)
            ? applicability
            : throw new ArgumentOutOfRangeException(nameof(scenario));
}

/// <summary>Ratified target and temporary legacy-EF oracle applicability profiles.</summary>
public static class DesignPersistenceContractProfiles
{
    public static DesignPersistenceContractProfile Target { get; } = new(
        "target",
        Enum.GetValues<DesignPersistenceContractScenario>()
            .ToDictionary(scenario => scenario, _ => DesignPersistenceScenarioApplicability.Applicable()));

    // The temporary "legacy-ef-oracle" profile existed only for the design EF behavioural oracle leaf
    // (spec 093 T025) and the T067 benchmark harness. Both were removed with the design EF lane
    // (T072/T073), so the profile and its shape-test pins were removed together.
}
