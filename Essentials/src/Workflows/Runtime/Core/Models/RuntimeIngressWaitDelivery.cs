using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Detached source input for selecting ordinary waits before any accepted-source effects.</summary>
/// <remarks>This contains no selected recipient, publisher identity or delivery authority.</remarks>
public sealed class RuntimeIngressWaitCandidate
{
    [JsonConstructor]
    public RuntimeIngressWaitCandidate(string stimulusType, string stimulusHash, JsonElement? input,
        IReadOnlyDictionary<string, string> metadata, ValueTypeDescriptor? payloadType = null, string? providerId = null,
        IReadOnlyList<RuntimeIngressWaitObservation>? observedWaits = null)
    {
        RuntimeDeliveryFingerprint.Text(stimulusType, nameof(stimulusType));
        RuntimeDeliveryFingerprint.Text(stimulusHash, nameof(stimulusHash));
        ArgumentNullException.ThrowIfNull(metadata);
        if (input is { ValueKind: JsonValueKind.Undefined }) throw new ArgumentException("Wait input must be defined.", nameof(input));
        if ((payloadType is null) != (providerId is null)) throw new ArgumentException("Typed wait input requires its provider identity.");
        if (providerId is not null) RuntimeDeliveryFingerprint.Text(providerId, nameof(providerId));
        StimulusType = stimulusType; StimulusHash = stimulusHash; Input = input?.Clone();
        Metadata = new ReadOnlyDictionary<string, string>(metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        PayloadType = payloadType; ProviderId = providerId;
        if (observedWaits is not null)
        {
            if (observedWaits.Any(item => item is null) || observedWaits.Select(item => (item.WorkflowExecutionId, item.BookmarkId)).Distinct().Count() != observedWaits.Count)
                throw new ArgumentException("A source policy observation must contain each physical wait once.", nameof(observedWaits));
            ObservedWaits = Array.AsReadOnly(observedWaits.OrderBy(item => item.WorkflowExecutionId, StringComparer.Ordinal)
                .ThenBy(item => item.BookmarkId, StringComparer.Ordinal).ToArray());
        }
    }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public JsonElement? Input { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public ValueTypeDescriptor? PayloadType { get; }
    public string? ProviderId { get; }
    /// <summary>Optional complete source-policy observation; providers must reject an added or changed wait before effects.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RuntimeIngressWaitObservation>? ObservedWaits { get; }
    [JsonIgnore] public string Fingerprint => RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-candidate.v1", this);
}

/// <summary>A module's pre-effect source-policy observation. This data alone grants no resume authority.</summary>
public sealed record RuntimeIngressWaitObservation
{
    [JsonConstructor]
    public RuntimeIngressWaitObservation(string workflowExecutionId, string bookmarkId, long generation, string registrationFingerprint)
    {
        RuntimeDeliveryFingerprint.Text(workflowExecutionId, nameof(workflowExecutionId)); RuntimeDeliveryFingerprint.Text(bookmarkId, nameof(bookmarkId));
        RuntimeDeliveryFingerprint.Hash(registrationFingerprint, nameof(registrationFingerprint));
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        WorkflowExecutionId = workflowExecutionId; BookmarkId = bookmarkId; Generation = generation; RegistrationFingerprint = registrationFingerprint;
    }
    public string WorkflowExecutionId { get; }
    public string BookmarkId { get; }
    public long Generation { get; }
    public string RegistrationFingerprint { get; }
    public static RuntimeIngressWaitObservation From(BookmarkStateObservation observation) => new(observation.State.WorkflowExecutionId,
        observation.State.BookmarkId, observation.Generation, RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-observation.v1", observation.State));
    public bool Matches(BookmarkStateObservation observation) => this == From(observation);
}

/// <summary>One deterministic resume of an existing workflow and exact physical bookmark generation.</summary>
public sealed class RuntimeIngressWaitIdentity
{
    public const int CurrentVersion = 1;
    [JsonConstructor]
    public RuntimeIngressWaitIdentity(int version, PersistenceScope scope, string sourceOutboxItemId,
        string workflowExecutionId, string bookmarkId, long generation)
    {
        if (version != CurrentVersion) throw new NotSupportedException($"Wait identity version '{version}' is unsupported.");
        ArgumentNullException.ThrowIfNull(scope);
        foreach (var text in new[] { sourceOutboxItemId, workflowExecutionId, bookmarkId }) RuntimeDeliveryFingerprint.Text(text, nameof(text));
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        Version = version; Scope = scope; SourceOutboxItemId = sourceOutboxItemId;
        WorkflowExecutionId = workflowExecutionId; BookmarkId = bookmarkId; Generation = generation;
    }
    public int Version { get; }
    public PersistenceScope Scope { get; }
    public string SourceOutboxItemId { get; }
    public string WorkflowExecutionId { get; }
    public string BookmarkId { get; }
    public long Generation { get; }
    [JsonIgnore] public string Digest => RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-identity.v1", this);
    [JsonIgnore] public string WaitId => "wait:ingress:v1:" + Digest;
    [JsonIgnore] public string CommandId => "command:ingress-wait:v1:" + Digest;
    [JsonIgnore] public string EnvelopeId => "envelope:ingress-wait:v1:" + Digest;
    [JsonIgnore] public string IdempotencyKey => "resume:ingress-wait:v1:" + Digest;
    public bool IsEquivalentTo(RuntimeIngressWaitIdentity? other) => other is not null && Digest == other.Digest;
}

/// <summary>Immutable observed wait; provider acceptance and physical retention are separate.</summary>
public sealed class RuntimeIngressWaitRecipient
{
    [JsonConstructor]
    public RuntimeIngressWaitRecipient(RuntimeIngressWaitIdentity identity, StimulusResumeRecipient expected)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(expected);
        if (expected.RegistrationCapture is not { } capture || identity.WorkflowExecutionId != expected.WorkflowExecutionId ||
            identity.BookmarkId != expected.BookmarkId || identity.Generation != capture.Generation || identity.Scope != capture.Scope.PersistenceScope)
            throw new ArgumentException("The wait identity must name its exact observed registration and physical scope.", nameof(expected));
        Identity = identity; Expected = expected;
    }
    public RuntimeIngressWaitIdentity Identity { get; }
    public StimulusResumeRecipient Expected { get; }
    [JsonIgnore] public string WaitId => Identity.WaitId;
    [JsonIgnore] public string Fingerprint => RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-recipient.v1", this);
    public bool IsEquivalentTo(RuntimeIngressWaitRecipient? other) => other is not null && Fingerprint == other.Fingerprint;
}

/// <summary>Non-circular reference to an immutable physical wait dispatch seal; no source lease is carried.</summary>
public sealed record RuntimeIngressWaitOrigin
{
    [JsonConstructor]
    public RuntimeIngressWaitOrigin(string sourceOutboxItemId, string sourceFingerprint, string selectionFingerprint,
        PersistenceScope scope, string waitId)
    {
        RuntimeDeliveryFingerprint.Text(sourceOutboxItemId, nameof(sourceOutboxItemId));
        RuntimeDeliveryFingerprint.Hash(sourceFingerprint, nameof(sourceFingerprint));
        RuntimeDeliveryFingerprint.Hash(selectionFingerprint, nameof(selectionFingerprint));
        RuntimeDeliveryFingerprint.Text(waitId, nameof(waitId)); ArgumentNullException.ThrowIfNull(scope);
        SourceOutboxItemId = sourceOutboxItemId; SourceFingerprint = sourceFingerprint;
        SelectionFingerprint = selectionFingerprint; Scope = scope; WaitId = waitId;
    }
    public string SourceOutboxItemId { get; }
    public string SourceFingerprint { get; }
    public string SelectionFingerprint { get; }
    public PersistenceScope Scope { get; }
    public string WaitId { get; }
}

/// <summary>Irrevocable responsibility for one canonical ordinary resume command.</summary>
public sealed class RuntimeIngressWaitDispatchSeal
{
    [JsonConstructor]
    public RuntimeIngressWaitDispatchSeal(RuntimeIngressWaitOrigin origin, RuntimeIngressWaitIdentity identity,
        DateTimeOffset committedAt, string commandProtocol, int commandProtocolVersion, JsonElement command)
    {
        ArgumentNullException.ThrowIfNull(origin); ArgumentNullException.ThrowIfNull(identity);
        RuntimeDeliveryFingerprint.Text(commandProtocol, nameof(commandProtocol));
        if (origin.SourceOutboxItemId != identity.SourceOutboxItemId || origin.Scope != identity.Scope || origin.WaitId != identity.WaitId)
            throw new ArgumentException("The seal and exact wait must name one source and physical scope.", nameof(identity));
        if (committedAt == default || committedAt == DateTimeOffset.MaxValue) throw new ArgumentOutOfRangeException(nameof(committedAt));
        if (commandProtocolVersion <= 0) throw new ArgumentOutOfRangeException(nameof(commandProtocolVersion));
        if (command.ValueKind != JsonValueKind.Object) throw new ArgumentException("A sealed resume command must be an object.", nameof(command));
        Origin = origin; Identity = identity; CommittedAt = committedAt; CommandProtocol = commandProtocol;
        CommandProtocolVersion = commandProtocolVersion; Command = command.Clone();
    }
    public RuntimeIngressWaitOrigin Origin { get; }
    public RuntimeIngressWaitIdentity Identity { get; }
    public DateTimeOffset CommittedAt { get; }
    public string CommandProtocol { get; }
    public int CommandProtocolVersion { get; }
    public JsonElement Command { get; }
    [JsonIgnore] public string Fingerprint => RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-seal.v1", this);
    public bool IsEquivalentTo(RuntimeIngressWaitDispatchSeal? other) => other is not null && Fingerprint == other.Fingerprint;
}

public enum RuntimeIngressWaitAcceptanceKind { SchedulerCommand, ConsumptionCheckpoint, RetiredGeneration }
public enum RuntimeIngressWaitRetirementKind { ReplacedGeneration, ExpiredRegistration, RetiredOwner }

/// <summary>Positive provider-observed retirement facts. Missing state alone is never a proof.</summary>
public sealed record RuntimeIngressWaitRetirement(RuntimeIngressWaitRetirementKind Kind, long? ObservedGeneration,
    DateTimeOffset? ExpiredAt, ActivityExecutionStatus? OwnerStatus);

/// <summary>Physical target evidence, independently reread by the owning source provider.</summary>
public sealed class RuntimeIngressWaitReceipt
{
    [JsonConstructor]
    public RuntimeIngressWaitReceipt(string waitId, string sealFingerprint, RuntimeIngressWaitIdentity identity,
        WorkflowExecutableIdentity material, RuntimeIngressWaitAcceptanceKind kind, string evidenceId,
        DateTimeOffset observedAt, RuntimeIngressWaitRetirement? retirement = null)
    {
        RuntimeDeliveryFingerprint.Text(waitId, nameof(waitId)); RuntimeDeliveryFingerprint.Hash(sealFingerprint, nameof(sealFingerprint));
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(material);
        RuntimeDeliveryFingerprint.Text(evidenceId, nameof(evidenceId));
        if (waitId != identity.WaitId) throw new ArgumentException("The receipt must name its exact wait identity.", nameof(waitId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (observedAt == default || observedAt == DateTimeOffset.MaxValue) throw new ArgumentOutOfRangeException(nameof(observedAt));
        if ((kind == RuntimeIngressWaitAcceptanceKind.RetiredGeneration) != (retirement is not null))
            throw new ArgumentException("Retirement requires its positive typed observation.", nameof(retirement));
        if (retirement is { } proof && (!Enum.IsDefined(proof.Kind) || (proof.Kind switch
            {
                RuntimeIngressWaitRetirementKind.ReplacedGeneration => proof.ObservedGeneration is not { } generation || generation <= identity.Generation,
                RuntimeIngressWaitRetirementKind.ExpiredRegistration => proof.ExpiredAt is not { } expired || expired > observedAt,
                RuntimeIngressWaitRetirementKind.RetiredOwner => proof.OwnerStatus is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered),
                _ => true
            }))) throw new ArgumentException("The typed observation does not prove this generation retired.", nameof(retirement));
        WaitId = waitId; SealFingerprint = sealFingerprint; Identity = identity; Material = material; Kind = kind;
        EvidenceId = evidenceId; ObservedAt = observedAt; Retirement = retirement;
    }
    public string WaitId { get; }
    public string SealFingerprint { get; }
    public RuntimeIngressWaitIdentity Identity { get; }
    public WorkflowExecutableIdentity Material { get; }
    public RuntimeIngressWaitAcceptanceKind Kind { get; }
    public string EvidenceId { get; }
    public DateTimeOffset ObservedAt { get; }
    public RuntimeIngressWaitRetirement? Retirement { get; }
    public bool IsEquivalentTo(RuntimeIngressWaitReceipt? other) => other is not null &&
        RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-receipt.v1", this) == RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-receipt.v1", other);
}

public enum RuntimeIngressWaitPhase { Pending, DispatchCommitted, TargetAccepted }

/// <summary>Monotonic wait responsibility. Failures never erase a physical seal or receipt.</summary>
public sealed class RuntimeIngressWaitProgress
{
    [JsonConstructor]
    public RuntimeIngressWaitProgress(string waitId, RuntimeIngressWaitPhase phase, RuntimeIngressWaitDispatchSeal? seal = null,
        RuntimeIngressWaitReceipt? receipt = null, int attemptCount = 0, DateTimeOffset? retryAt = null,
        string? lastFailureCode = null, RuntimeRecipientRecoveryState recoveryState = RuntimeRecipientRecoveryState.None)
    {
        RuntimeDeliveryFingerprint.Text(waitId, nameof(waitId));
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        if (!Enum.IsDefined(recoveryState)) throw new ArgumentOutOfRangeException(nameof(recoveryState));
        if (attemptCount < 0) throw new ArgumentOutOfRangeException(nameof(attemptCount));
        if ((phase != RuntimeIngressWaitPhase.Pending) != (seal is not null) ||
            (phase == RuntimeIngressWaitPhase.TargetAccepted) != (receipt is not null) ||
            seal is not null && seal.Identity.WaitId != waitId || receipt is not null &&
            (receipt.WaitId != waitId || receipt.SealFingerprint != seal!.Fingerprint || !receipt.Identity.IsEquivalentTo(seal.Identity)))
            throw new ArgumentException("Wait progress must preserve its exact physical seal and acceptance receipt.");
        WaitId = waitId; Phase = phase; Seal = seal; Receipt = receipt; AttemptCount = attemptCount;
        RetryAt = retryAt; LastFailureCode = lastFailureCode; RecoveryState = recoveryState;
    }
    public string WaitId { get; }
    public RuntimeIngressWaitPhase Phase { get; }
    public RuntimeIngressWaitDispatchSeal? Seal { get; }
    public RuntimeIngressWaitReceipt? Receipt { get; }
    public int AttemptCount { get; }
    public DateTimeOffset? RetryAt { get; }
    public string? LastFailureCode { get; }
    public RuntimeRecipientRecoveryState RecoveryState { get; }
    public RuntimeIngressWaitProgress CommitDispatch(RuntimeIngressWaitDispatchSeal seal)
    {
        ArgumentNullException.ThrowIfNull(seal);
        if (Seal is not null) return Seal.IsEquivalentTo(seal) ? this : throw new InvalidOperationException("A wait dispatch seal cannot be replaced.");
        return new(WaitId, RuntimeIngressWaitPhase.DispatchCommitted, seal, attemptCount: AttemptCount,
            retryAt: RetryAt, lastFailureCode: LastFailureCode, recoveryState: RecoveryState);
    }
    public RuntimeIngressWaitProgress AcceptTarget(RuntimeIngressWaitReceipt receipt)
    {
        if (Seal is null) throw new InvalidOperationException("Wait acceptance requires committed responsibility.");
        if (Receipt is not null) return Receipt.IsEquivalentTo(receipt) ? this : throw new InvalidOperationException("A wait receipt cannot be replaced.");
        return new(WaitId, RuntimeIngressWaitPhase.TargetAccepted, Seal, receipt, AttemptCount);
    }
}

/// <summary>The complete pre-effect ordinary wait cohort on one true accepted external source.</summary>
public sealed class RuntimeIngressWaitDelivery
{
    public const int CurrentVersion = 1;
    [JsonConstructor]
    public RuntimeIngressWaitDelivery(int version, string sourceOutboxItemId, string sourceFingerprint, PersistenceScope scope,
        DateTimeOffset selectedAt, RuntimeIngressWaitCandidate candidate, IReadOnlyList<RuntimeIngressWaitRecipient> recipients,
        IReadOnlyList<RuntimeIngressWaitProgress> progress, string? selectionFingerprint = null)
    {
        if (version != CurrentVersion) throw new NotSupportedException($"Wait delivery version '{version}' is unsupported.");
        RuntimeDeliveryFingerprint.Text(sourceOutboxItemId, nameof(sourceOutboxItemId)); RuntimeDeliveryFingerprint.Hash(sourceFingerprint, nameof(sourceFingerprint));
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(recipients); ArgumentNullException.ThrowIfNull(progress);
        if (selectedAt == default || selectedAt == DateTimeOffset.MaxValue) throw new ArgumentOutOfRangeException(nameof(selectedAt));
        if (recipients.Any(item => item is null) || progress.Any(item => item is null)) throw new ArgumentException("A wait cohort cannot contain null entries.");
        var saved = recipients.OrderBy(item => item.WaitId, StringComparer.Ordinal).ToArray();
        var states = progress.OrderBy(item => item.WaitId, StringComparer.Ordinal).ToArray();
        if (saved.Select(item => item.WaitId).Distinct(StringComparer.Ordinal).Count() != saved.Length ||
            !saved.Select(item => item.WaitId).SequenceEqual(states.Select(item => item.WaitId), StringComparer.Ordinal) ||
            saved.Select(item => (item.Identity.WorkflowExecutionId, item.Identity.BookmarkId)).Distinct().Count() != saved.Length)
            throw new ArgumentException("A complete wait cohort must contain each exact registration and progress once.");
        if (saved.Any(item => item.Identity.SourceOutboxItemId != sourceOutboxItemId || item.Identity.Scope != scope ||
            item.Expected.StimulusType != candidate.StimulusType || item.Expected.StimulusHash != candidate.StimulusHash))
            throw new ArgumentException("The saved waits must belong to this source, scope and stimulus.", nameof(recipients));
        Version = version; SourceOutboxItemId = sourceOutboxItemId; SourceFingerprint = sourceFingerprint; Scope = scope;
        SelectedAt = selectedAt; Candidate = candidate; Recipients = Array.AsReadOnly(saved); Progress = Array.AsReadOnly(states);
        SelectionFingerprint = RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-selection.v1",
            new { version, sourceOutboxItemId, sourceFingerprint, scope, selectedAt, candidate, recipients = Recipients });
        if (selectionFingerprint is not null && selectionFingerprint != SelectionFingerprint)
            throw new ArgumentException("The wait selection fingerprint differs from its immutable cohort.", nameof(selectionFingerprint));
        foreach (var state in states)
        {
            var recipient = saved.Single(item => item.WaitId == state.WaitId);
            if (state.Seal is { } seal && (seal.Origin != Origin(recipient) || !seal.Identity.IsEquivalentTo(recipient.Identity)) ||
                state.Receipt is { } receipt && receipt.Material != recipient.Expected.RegistrationCapture!.Executable)
                throw new ArgumentException("Wait progress must preserve this exact selection and material.", nameof(progress));
        }
    }
    public int Version { get; }
    public string SourceOutboxItemId { get; }
    public string SourceFingerprint { get; }
    public PersistenceScope Scope { get; }
    public DateTimeOffset SelectedAt { get; }
    public RuntimeIngressWaitCandidate Candidate { get; }
    public IReadOnlyList<RuntimeIngressWaitRecipient> Recipients { get; }
    public IReadOnlyList<RuntimeIngressWaitProgress> Progress { get; }
    public string SelectionFingerprint { get; }
    [JsonIgnore] public bool IsSettled => Progress.All(item => item.Phase == RuntimeIngressWaitPhase.TargetAccepted);
    public RuntimeIngressWaitOrigin Origin(RuntimeIngressWaitRecipient recipient) => new(SourceOutboxItemId, SourceFingerprint, SelectionFingerprint, Scope, recipient.WaitId);
    public RuntimeIngressWaitDelivery WithProgress(RuntimeIngressWaitProgress replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var prior = Progress.SingleOrDefault(item => item.WaitId == replacement.WaitId) ?? throw new ArgumentException("The wait was not selected.", nameof(replacement));
        if (replacement.AttemptCount < prior.AttemptCount || prior.Phase == RuntimeIngressWaitPhase.Pending && replacement.Phase == RuntimeIngressWaitPhase.TargetAccepted ||
            prior.Seal is { } seal && (replacement.Seal is null || !seal.IsEquivalentTo(replacement.Seal)) ||
            prior.Receipt is { } receipt && (replacement.Receipt is null || !receipt.IsEquivalentTo(replacement.Receipt)))
            throw new InvalidOperationException("Wait progress cannot erase attempts, commitment or physical acceptance.");
        return new(Version, SourceOutboxItemId, SourceFingerprint, Scope, SelectedAt, Candidate, Recipients,
            Progress.Select(item => item.WaitId == replacement.WaitId ? replacement : item).ToArray(), SelectionFingerprint);
    }
    public bool IsEquivalentTo(RuntimeIngressWaitDelivery? other) => other is not null &&
        RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-delivery.v1", this) == RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-wait-delivery.v1", other);
}

/// <summary>Actual immutable physical wait commitment. Its authority outlives a delivery handler's claim.</summary>
public sealed record RuntimeCommittedIngressWait(RuntimePostCommitOutboxItem Item, RuntimeIngressWaitRecipient Recipient,
    RuntimeIngressWaitDispatchSeal Seal);
