using System.Collections.ObjectModel;
using Elsa.Rules.Core;

namespace Elsa.Rules.Persistence.Core;

/// <summary>A current worker lease, fenced independently from the semantic model revision.</summary>
public sealed record RuleWorkLease
{
    public RuleWorkLease(RuleRunIdentity identity, RuleInvocationFingerprint fingerprint, string owner, long fence, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(fingerprint);
        RuleValueValidation.Identifier(owner, nameof(owner));
        if (fence <= 0)
            throw new ArgumentOutOfRangeException(nameof(fence));
        Identity = identity;
        Fingerprint = fingerprint;
        Owner = owner;
        Fence = fence;
        ExpiresAt = expiresAt;
    }

    public RuleRunIdentity Identity { get; }
    public RuleInvocationFingerprint Fingerprint { get; }
    public string Owner { get; }
    public long Fence { get; }
    public DateTimeOffset ExpiresAt { get; }
}

/// <summary>Worker command token; model revision is a compare-and-swap value separate from lease fencing.</summary>
public sealed record RuleWorkToken
{
    public RuleWorkToken(RuleWorkLease lease, long expectedModelRevision)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (expectedModelRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedModelRevision));
        Lease = lease;
        ExpectedModelRevision = expectedModelRevision;
    }

    public RuleWorkLease Lease { get; }
    public long ExpectedModelRevision { get; }
}

/// <summary>Independent fenced lease for one native terminal-delivery obligation.</summary>
public sealed record RuleDeliveryLease
{
    public RuleDeliveryLease(RuleRunIdentity identity, string deliveryId, string owner, long fence, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(identity);
        RuleValueValidation.Identifier(deliveryId, nameof(deliveryId));
        RuleValueValidation.Identifier(owner, nameof(owner));
        if (fence <= 0)
            throw new ArgumentOutOfRangeException(nameof(fence));
        Identity = identity;
        DeliveryId = deliveryId;
        Owner = owner;
        Fence = fence;
        ExpiresAt = expiresAt;
    }

    public RuleRunIdentity Identity { get; }
    public string DeliveryId { get; }
    public string Owner { get; }
    public long Fence { get; }
    public DateTimeOffset ExpiresAt { get; }
}

/// <summary>Frozen logical identity of one deterministic evaluation round.</summary>
public sealed record RuleRoundIdentity
{
    public RuleRoundIdentity(RuleRunIdentity run, RuleInvocationFingerprint fingerprint, int ordinal, string inputStateSha256)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (ordinal is < 0 or >= RuleLimits.MaxRounds)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        RuleValueValidation.Sha256(inputStateSha256, nameof(inputStateSha256));
        Run = run;
        Fingerprint = fingerprint;
        Ordinal = ordinal;
        InputStateSha256 = inputStateSha256;
    }

    public RuleRunIdentity Run { get; }
    public RuleInvocationFingerprint Fingerprint { get; }
    public int Ordinal { get; }
    public string InputStateSha256 { get; }
}

/// <summary>The immutable artifact, implementation, fact set, budget, and revision supplied to one round.</summary>
public sealed class RuleRoundInput
{
    private readonly ReadOnlyCollection<RuleFact> _facts;

    public RuleRoundInput(RuleRoundIdentity identity, RuleArtifactSnapshot artifact, RuleSemanticPin implementation,
        IReadOnlyList<RuleFact> facts, int maxRounds, long modelRevision)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(implementation);
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Count > RuleLimits.MaxVocabularyFacts)
            throw new ArgumentOutOfRangeException(nameof(facts));
        if (maxRounds is <= 0 or > RuleLimits.MaxRounds)
            throw new ArgumentOutOfRangeException(nameof(maxRounds));
        if (modelRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(modelRevision));
        var copy = facts.ToArray();
        if (copy.Any(static fact => fact is null))
            throw new ArgumentException("Round facts cannot contain null values.", nameof(facts));
        Identity = identity;
        Artifact = artifact;
        Implementation = implementation;
        _facts = Array.AsReadOnly(copy);
        MaxRounds = maxRounds;
        ModelRevision = modelRevision;
    }

    public RuleRoundIdentity Identity { get; }
    public RuleArtifactSnapshot Artifact { get; }
    public RuleSemanticPin Implementation { get; }
    public IReadOnlyList<RuleFact> Facts => _facts;
    public int MaxRounds { get; }
    public long ModelRevision { get; }
}

/// <summary>One rule's firing and its single propositional conclusion.</summary>
public sealed record RuleFiringEvidence
{
    public RuleFiringEvidence(string ruleId, RuleFact conclusion)
    {
        RuleValueValidation.Identifier(ruleId, nameof(ruleId));
        ArgumentNullException.ThrowIfNull(conclusion);
        RuleId = ruleId;
        Conclusion = conclusion;
    }

    public string RuleId { get; }
    public RuleFact Conclusion { get; }
}

/// <summary>Immutable semantic round outcome, excluding physical attempt attribution.</summary>
public sealed class RuleRoundReceipt
{
    private readonly ReadOnlyCollection<RuleFact> _addedFacts;
    private readonly ReadOnlyCollection<RuleFiringEvidence> _firingEvidence;

    public RuleRoundReceipt(RuleRoundIdentity identity, string implementationPinHash,
        IReadOnlyList<RuleFact> addedFacts, IReadOnlyList<RuleFiringEvidence> firingEvidence, string semanticReceiptSha256)
    {
        ArgumentNullException.ThrowIfNull(identity);
        RuleValueValidation.Sha256(implementationPinHash, nameof(implementationPinHash));
        RuleValueValidation.Sha256(semanticReceiptSha256, nameof(semanticReceiptSha256));
        ArgumentNullException.ThrowIfNull(addedFacts);
        ArgumentNullException.ThrowIfNull(firingEvidence);
        if (addedFacts.Count > RuleLimits.MaxVocabularyFacts)
            throw new ArgumentOutOfRangeException(nameof(addedFacts));
        if (firingEvidence.Count > RuleLimits.MaxRules)
            throw new ArgumentOutOfRangeException(nameof(firingEvidence));
        var factCopy = addedFacts.ToArray();
        var firingCopy = firingEvidence.ToArray();
        if (factCopy.Any(static fact => fact is null) || firingCopy.Any(static firing => firing is null))
            throw new ArgumentException("A receipt cannot contain null facts or firing evidence.");
        Identity = identity;
        ImplementationPinHash = implementationPinHash;
        _addedFacts = Array.AsReadOnly(factCopy);
        _firingEvidence = Array.AsReadOnly(firingCopy);
        SemanticReceiptSha256 = semanticReceiptSha256;
    }

    public RuleRoundIdentity Identity { get; }
    public string ImplementationPinHash { get; }
    public IReadOnlyList<RuleFact> AddedFacts => _addedFacts;
    public IReadOnlyList<RuleFiringEvidence> FiringEvidence => _firingEvidence;
    public string SemanticReceiptSha256 { get; }
}

/// <summary>Physical worker attempt attribution, persisted separately from semantic receipt identity.</summary>
public sealed record RuleRoundAttempt
{
    public RuleRoundAttempt(string attemptId, string workerId, long leaseFence)
    {
        RuleValueValidation.Identifier(attemptId, nameof(attemptId));
        RuleValueValidation.Identifier(workerId, nameof(workerId));
        if (leaseFence <= 0)
            throw new ArgumentOutOfRangeException(nameof(leaseFence));
        AttemptId = attemptId;
        WorkerId = workerId;
        LeaseFence = leaseFence;
    }

    public string AttemptId { get; }
    public string WorkerId { get; }
    public long LeaseFence { get; }
}

/// <summary>Candidate receipt plus physical attempt metadata for atomic acceptance.</summary>
public sealed record RuleRoundSubmission(RuleRoundReceipt Receipt, RuleRoundAttempt Attempt);

/// <summary>Joined immutable receipt with its first accepted attempt and revision-stamped consumption.</summary>
public sealed record RuleStoredRoundReceipt(string ReceiptId, RuleRoundReceipt Receipt, RuleRoundAttempt AcceptedAttempt,
    long AcceptedModelRevision, long? ConsumedModelRevision);

/// <summary>Identity-only reference to one immutable accepted receipt.</summary>
public sealed record RuleReceiptIdentity(string ReceiptId, string SemanticReceiptSha256);

/// <summary>Input supplied to the semantic owner to verify a complete candidate step.</summary>
public sealed record RuleRoundValidationInput(RuleRoundInput Input);

/// <summary>Outcome of deterministic semantic validation before any store mutation.</summary>
public enum RuleValidationDisposition
{
    Equivalent,
    Invalid,
    UnsupportedFormat
}

/// <summary>Validation outcome with only a safe stable diagnostic code.</summary>
public sealed record RuleRoundValidationResult
{
    public RuleRoundValidationResult(RuleValidationDisposition disposition, string? safeDiagnosticCode)
    {
        if (safeDiagnosticCode is not null)
            RuleValueValidation.Identifier(safeDiagnosticCode, nameof(safeDiagnosticCode));
        Disposition = disposition;
        SafeDiagnosticCode = safeDiagnosticCode;
    }

    public RuleValidationDisposition Disposition { get; }
    public string? SafeDiagnosticCode { get; }
}

/// <summary>Runtime-owned validation seam; providers must not infer equivalence themselves.</summary>
public interface IRuleRoundValidator
{
    ValueTask<RuleRoundValidationResult> ValidateAsync(RuleRoundValidationInput input, RuleRoundReceipt candidate,
        CancellationToken cancellationToken = default);
}

/// <summary>Worker identity and the immutable semantic/schema pins it can execute.</summary>
public sealed class RuleClaimRequest
{
    private readonly ReadOnlyCollection<RuleSemanticPin> _supportedImplementations;
    private readonly ReadOnlyCollection<int> _invocationSchemas;
    private readonly ReadOnlyCollection<int> _receiptSchemas;
    private readonly ReadOnlyCollection<int> _stateSchemas;

    public RuleClaimRequest(string owner, IReadOnlyList<RuleSemanticPin> supportedImplementations,
        IReadOnlyList<int> invocationSchemas, IReadOnlyList<int> receiptSchemas, IReadOnlyList<int> stateSchemas)
    {
        RuleValueValidation.Identifier(owner, nameof(owner));
        _supportedImplementations = Snapshot(supportedImplementations, nameof(supportedImplementations));
        _invocationSchemas = SnapshotVersions(invocationSchemas, nameof(invocationSchemas));
        _receiptSchemas = SnapshotVersions(receiptSchemas, nameof(receiptSchemas));
        _stateSchemas = SnapshotVersions(stateSchemas, nameof(stateSchemas));
        Owner = owner;
    }

    public string Owner { get; }
    public IReadOnlyList<RuleSemanticPin> SupportedImplementations => _supportedImplementations;
    public IReadOnlyList<int> InvocationSchemas => _invocationSchemas;
    public IReadOnlyList<int> ReceiptSchemas => _receiptSchemas;
    public IReadOnlyList<int> StateSchemas => _stateSchemas;

    private static ReadOnlyCollection<RuleSemanticPin> Snapshot(IReadOnlyList<RuleSemanticPin> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        var copy = values.ToArray();
        if (copy.Any(static value => value is null))
            throw new ArgumentException("Supported implementation pins cannot contain null values.", name);
        return Array.AsReadOnly(copy);
    }

    private static ReadOnlyCollection<int> SnapshotVersions(IReadOnlyList<int> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        var copy = values.ToArray();
        if (copy.Any(static value => value <= 0))
            throw new ArgumentOutOfRangeException(name, "Supported schema versions must be positive.");
        return Array.AsReadOnly(copy);
    }
}

/// <summary>Outcome of atomically acquiring an eligible pending round.</summary>
public enum RuleWorkClaimDisposition
{
    Claimed,
    NoEligibleWork,
    Unavailable
}

public sealed record RuleWorkClaimResult(RuleWorkClaimDisposition Disposition, RuleWorkToken? Token);

/// <summary>Terminal-result schemas supported by a native delivery worker.</summary>
public sealed class RuleDeliveryClaimRequest
{
    private readonly ReadOnlyCollection<int> _resultSchemas;

    public RuleDeliveryClaimRequest(string owner, IReadOnlyList<int> resultSchemas)
    {
        RuleValueValidation.Identifier(owner, nameof(owner));
        ArgumentNullException.ThrowIfNull(resultSchemas);
        var copy = resultSchemas.ToArray();
        if (copy.Any(static schema => schema <= 0))
            throw new ArgumentOutOfRangeException(nameof(resultSchemas));
        Owner = owner;
        _resultSchemas = Array.AsReadOnly(copy);
    }

    public string Owner { get; }
    public IReadOnlyList<int> ResultSchemas => _resultSchemas;
}

public enum RuleDeliveryClaimDisposition
{
    Claimed,
    NoEligibleDelivery,
    Unavailable
}

public sealed record RuleDeliveryClaimResult(RuleDeliveryClaimDisposition Disposition, RuleDeliveryLease? Lease);

public enum RuleLeaseDisposition
{
    Renewed,
    Released,
    LeaseLost
}

public sealed record RuleLeaseConfirmation(RuleLeaseDisposition Disposition, RuleWorkLease? Lease);

public enum RuleReceiptDisposition
{
    Accepted,
    Replay,
    Conflict,
    LeaseLost,
    RevisionConflict,
    InvalidReceipt,
    Unavailable
}

/// <summary>Small acceptance confirmation; it never carries facts, receipts, or successor work.</summary>
public sealed record RuleReceiptConfirmation(RuleReceiptDisposition Disposition, RuleRunIdentity Identity,
    string? ReceiptId, long? ModelRevision, string? TerminalId, string? NextRoundId);

public enum RuleTransitionDisposition
{
    Consumed,
    Acknowledged,
    Replay,
    TerminalUnchanged,
    Conflict,
    LeaseLost,
    RevisionConflict,
    ProofUnavailable,
    Invalid,
    Unavailable
}

/// <summary>Small transition confirmation with no result payload or ownership grant.</summary>
public sealed record RuleTransitionConfirmation(RuleTransitionDisposition Disposition, RuleRunIdentity Identity,
    string? TransitionId, string? ReceiptId, string? TerminalId, string? NextRoundId, long? ModelRevision);

public enum RuleWorkReadDisposition
{
    EvaluateRound,
    ConsumeReceipt,
    Terminal,
    LeaseLost,
    NotFound,
    Unavailable
}

/// <summary>A consistent bounded work snapshot paired with the current model revision token.</summary>
public sealed record RuleWorkReadResult
{
    public RuleWorkReadResult(RuleWorkReadDisposition disposition, RuleWorkToken? currentToken,
        RuleRoundInput? roundInput, RuleStoredRoundReceipt? pendingReceipt, string? terminalId)
    {
        var valid = disposition switch
        {
            RuleWorkReadDisposition.EvaluateRound => currentToken is not null && roundInput is not null && pendingReceipt is null && terminalId is null,
            RuleWorkReadDisposition.ConsumeReceipt => currentToken is not null && roundInput is null && pendingReceipt is not null && terminalId is null,
            RuleWorkReadDisposition.Terminal => currentToken is null && roundInput is null && pendingReceipt is null && terminalId is not null,
            _ => currentToken is null && roundInput is null && pendingReceipt is null && terminalId is null
        };
        if (!valid)
            throw new ArgumentException("The work read payload does not match its disposition.");
        Disposition = disposition;
        CurrentToken = currentToken;
        RoundInput = roundInput;
        PendingReceipt = pendingReceipt;
        TerminalId = terminalId;
    }

    public RuleWorkReadDisposition Disposition { get; }
    public RuleWorkToken? CurrentToken { get; }
    public RuleRoundInput? RoundInput { get; }
    public RuleStoredRoundReceipt? PendingReceipt { get; }
    public string? TerminalId { get; }
}

public enum RuleDeliveryReadDisposition
{
    Ready,
    LeaseLost,
    NotFound,
    Unavailable
}

public sealed record RuleDeliveryReadResult
{
    public RuleDeliveryReadResult(RuleDeliveryReadDisposition disposition, RuleTerminalEnvelope? envelope)
    {
        if ((disposition == RuleDeliveryReadDisposition.Ready) != (envelope is not null))
            throw new ArgumentException("A delivery envelope exists only for a ready delivery.", nameof(envelope));
        Disposition = disposition;
        Envelope = envelope;
    }

    public RuleDeliveryReadDisposition Disposition { get; }
    public RuleTerminalEnvelope? Envelope { get; }
}

public enum RuleRunReadDisposition
{
    Found,
    NotFound,
    Unavailable
}

public enum RuleRunStatus
{
    Running,
    Succeeded,
    Faulted,
    Cancelled,
    Refused
}

/// <summary>Bounded status view; it contains no worker capability or full receipt/result payload.</summary>
public sealed record RuleRunView(RuleRunIdentity Identity, RuleInvocationFingerprint Fingerprint,
    string ArtifactId, string ArtifactSha256, string ImplementationPinHash, RuleRunStatus Status,
    long ModelRevision, int CommittedRounds, int CommittedFactCount, string? PendingReceiptId,
    string? TerminalId, RuleTerminalKind? TerminalKind);

public sealed record RuleRunReadResult
{
    public RuleRunReadResult(RuleRunReadDisposition disposition, RuleRunView? view)
    {
        if ((disposition == RuleRunReadDisposition.Found) != (view is not null))
            throw new ArgumentException("A run view exists only for a found run.", nameof(view));
        Disposition = disposition;
        View = view;
    }

    public RuleRunReadDisposition Disposition { get; }
    public RuleRunView? View { get; }
}

public enum RuleTerminalReadDisposition
{
    Found,
    NotTerminal,
    NotFound,
    Unavailable
}

public sealed record RuleTerminalReadResult
{
    public RuleTerminalReadResult(RuleTerminalReadDisposition disposition, RuleTerminalResult? result)
    {
        if ((disposition == RuleTerminalReadDisposition.Found) != (result is not null))
            throw new ArgumentException("A terminal result exists only when found.", nameof(result));
        Disposition = disposition;
        Result = result;
    }

    public RuleTerminalReadDisposition Disposition { get; }
    public RuleTerminalResult? Result { get; }
}

/// <summary>Opaque exclusive cursor bound by the provider to one as-of model-revision snapshot.</summary>
public sealed record RuleProgressPosition
{
    public RuleProgressPosition(string cursor)
    {
        RuleValueValidation.Identifier(cursor, nameof(cursor));
        Cursor = cursor;
    }

    public string Cursor { get; }
}

public enum RuleProgressItemKind
{
    Fact,
    Round
}

public sealed record RuleRoundProgress(int Ordinal, string InputStateSha256, string? ReceiptId,
    string? SemanticReceiptSha256, long AcceptedModelRevision, long? ConsumedModelRevision,
    string? AcceptedAttemptId, string? AcceptedWorkerId, long? AcceptedLeaseFence, int AddedFactCount);

/// <summary>One tagged item in a stable, exclusive, as-of progress stream.</summary>
public sealed record RuleProgressItem
{
    public RuleProgressItem(RuleProgressItemKind kind, string key, long firstVisibleModelRevision,
        RuleFact? fact, RuleRoundProgress? round)
    {
        RuleValueValidation.Identifier(key, nameof(key));
        if (firstVisibleModelRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(firstVisibleModelRevision));
        if ((kind == RuleProgressItemKind.Fact) != (fact is not null) ||
            (kind == RuleProgressItemKind.Round) != (round is not null))
            throw new ArgumentException("A progress item must carry exactly the payload selected by its kind.");
        Kind = kind;
        Key = key;
        FirstVisibleModelRevision = firstVisibleModelRevision;
        Fact = fact;
        Round = round;
    }

    public RuleProgressItemKind Kind { get; }
    public string Key { get; }
    public long FirstVisibleModelRevision { get; }
    public RuleFact? Fact { get; }
    public RuleRoundProgress? Round { get; }
}

/// <summary>One bounded page in a fixed-revision progress projection.</summary>
public sealed class RuleProgressPage
{
    private readonly ReadOnlyCollection<RuleProgressItem> _items;

    public RuleProgressPage(IReadOnlyList<RuleProgressItem> items, RuleProgressPosition? next)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > RuleLimits.MaxPageEntries)
            throw new ArgumentOutOfRangeException(nameof(items));
        var copy = items.ToArray();
        if (copy.Any(static item => item is null))
            throw new ArgumentException("Progress pages cannot contain null items.", nameof(items));
        _items = Array.AsReadOnly(copy);
        Next = next;
    }

    public IReadOnlyList<RuleProgressItem> Items => _items;
    public RuleProgressPosition? Next { get; }
}

public enum RuleProgressReadDisposition
{
    Page,
    NotFound,
    Unavailable,
    InvalidPosition
}

public sealed record RuleProgressReadResult
{
    public RuleProgressReadResult(RuleProgressReadDisposition disposition, RuleProgressPage? page)
    {
        if ((disposition == RuleProgressReadDisposition.Page) != (page is not null))
            throw new ArgumentException("A progress page exists only for the Page disposition.", nameof(page));
        Disposition = disposition;
        Page = page;
    }

    public RuleProgressReadDisposition Disposition { get; }
    public RuleProgressPage? Page { get; }
}

/// <summary>The provider-neutral command/query boundary for durable Rules runs.</summary>
public interface IRuleRunStore
{
    ValueTask<RuleAdmissionConfirmation> AdmitAsync(RuleRunAdmission admission, CancellationToken cancellationToken = default);
    ValueTask<RuleRefusalConfirmation> RefuseAdmissionAsync(RuleAdmissionRefusal refusal, CancellationToken cancellationToken = default);
    ValueTask<RuleCancellationConfirmation> CancelAsync(RuleCancellationRequest request, CancellationToken cancellationToken = default);
    ValueTask<RuleWorkClaimResult> ClaimWorkAsync(RuleClaimRequest request, CancellationToken cancellationToken = default);
    ValueTask<RuleLeaseConfirmation> RenewAsync(RuleWorkLease lease, CancellationToken cancellationToken = default);
    ValueTask<RuleLeaseConfirmation> ReleaseAsync(RuleWorkLease lease, CancellationToken cancellationToken = default);
    ValueTask<RuleReceiptConfirmation> AcceptRoundAsync(RuleWorkToken token, RuleRoundSubmission submission, CancellationToken cancellationToken = default);
    ValueTask<RuleTransitionConfirmation> ConsumeRoundAsync(RuleWorkToken token, RuleReceiptIdentity receipt, CancellationToken cancellationToken = default);
    ValueTask<RuleDeliveryClaimResult> ClaimDeliveryAsync(RuleDeliveryClaimRequest request, CancellationToken cancellationToken = default);
    ValueTask<RuleTransitionConfirmation> AcknowledgeDeliveryAsync(RuleDeliveryLease lease,
        RuleDeliveryAcknowledgement acknowledgement, CancellationToken cancellationToken = default);
}

/// <summary>Read-only bounded projections; queries never acquire, renew, or mutate leases.</summary>
public interface IRuleRunReader
{
    ValueTask<RuleWorkReadResult> ReadWorkAsync(RuleWorkToken token, CancellationToken cancellationToken = default);
    ValueTask<RuleDeliveryReadResult> ReadDeliveryAsync(RuleDeliveryLease lease, CancellationToken cancellationToken = default);
    ValueTask<RuleRunReadResult> FindAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default);
    ValueTask<RuleTerminalReadResult> ReadTerminalAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default);
    ValueTask<RuleProgressReadResult> ReadProgressAsync(RuleRunIdentity identity, RuleProgressPosition? after,
        int take, CancellationToken cancellationToken = default);
}

public enum RuleAdmissionDisposition
{
    Admitted,
    Replay,
    AlreadyCancelled,
    AlreadyRefused,
    Conflict,
    Invalid,
    Unavailable
}

public sealed record RuleAdmissionConfirmation(RuleAdmissionDisposition Disposition, RuleRunIdentity Identity,
    long? ModelRevision, string? TerminalId);

public enum RuleRefusalDisposition
{
    Refused,
    Replay,
    ExistingAdmission,
    AlreadyCancelled,
    Conflict,
    Invalid,
    Unavailable
}

public sealed record RuleRefusalConfirmation(RuleRefusalDisposition Disposition, RuleRunIdentity Identity, string? TerminalId);

public enum RuleCancellationDisposition
{
    Cancelled,
    Replay,
    TerminalUnchanged,
    Conflict,
    Invalid,
    Unavailable
}

public sealed record RuleCancellationConfirmation(RuleCancellationDisposition Disposition, RuleRunIdentity Identity,
    long? ModelRevision, string? TerminalId);

/// <summary>Proven native delivery settlement; not an enqueue acknowledgement.</summary>
public sealed record RuleDeliveryAcknowledgement(RuleDeliveryProof Proof);
