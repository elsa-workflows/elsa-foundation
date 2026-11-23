using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public enum RuntimeRecipientPhase { Pending, DispatchCommitted, TargetAccepted, CancelledBeforeDispatch }
public enum RuntimeRecipientRecoveryState { None, RetryWaiting, BlockedPolicy, BlockedCapability, FailedFinal, TargetPoisoned }
public enum RuntimeTargetAcceptanceKind { SchedulerCommand, InitialCheckpoint, ModelInput }

/// <summary>Immutable responsibility to enqueue one exact command, independent of the source handler's lease.</summary>
public sealed class RuntimeDispatchSeal
{
    public const int CurrentVersion = 1;
    [JsonConstructor]
    public RuntimeDispatchSeal(int version, string sourceOutboxItemId, string sourceFingerprint, string selectionFingerprint,
        string recipientId, RuntimeSubscriptionStartIdentity startIdentity, DateTimeOffset committedAt,
        string commandProtocol, int commandProtocolVersion, JsonElement command)
    {
        if (version != CurrentVersion) throw new NotSupportedException($"Dispatch seal version '{version}' is unsupported.");
        RuntimeDeliveryFingerprint.Text(sourceOutboxItemId, nameof(sourceOutboxItemId));
        RuntimeDeliveryFingerprint.Hash(sourceFingerprint, nameof(sourceFingerprint));
        RuntimeDeliveryFingerprint.Hash(selectionFingerprint, nameof(selectionFingerprint));
        RuntimeDeliveryFingerprint.Text(recipientId, nameof(recipientId)); RuntimeDeliveryFingerprint.Text(commandProtocol, nameof(commandProtocol));
        ArgumentNullException.ThrowIfNull(startIdentity);
        if (startIdentity.SourceOutboxItemId != sourceOutboxItemId) throw new ArgumentException("Seal and start identity must name one source.", nameof(startIdentity));
        if (committedAt == default || committedAt == DateTimeOffset.MaxValue) throw new ArgumentOutOfRangeException(nameof(committedAt));
        if (commandProtocolVersion <= 0) throw new ArgumentOutOfRangeException(nameof(commandProtocolVersion));
        if (command.ValueKind != JsonValueKind.Object) throw new ArgumentException("A sealed command must be a defined object.", nameof(command));
        Version = version; SourceOutboxItemId = sourceOutboxItemId; SourceFingerprint = sourceFingerprint;
        SelectionFingerprint = selectionFingerprint; RecipientId = recipientId; StartIdentity = startIdentity;
        CommittedAt = committedAt; CommandProtocol = commandProtocol; CommandProtocolVersion = commandProtocolVersion; Command = command.Clone();
        _ = Fingerprint;
    }
    public int Version { get; }
    public string SourceOutboxItemId { get; }
    public string SourceFingerprint { get; }
    public string SelectionFingerprint { get; }
    public string RecipientId { get; }
    public RuntimeSubscriptionStartIdentity StartIdentity { get; }
    public DateTimeOffset CommittedAt { get; }
    public string CommandProtocol { get; }
    public int CommandProtocolVersion { get; }
    public JsonElement Command { get; }
    [JsonIgnore] public string Fingerprint => RuntimeDeliveryFingerprint.Compute("elsa.runtime.dispatch-seal.v1", this);
    public bool IsEquivalentTo(RuntimeDispatchSeal? other) => other is not null && Fingerprint == other.Fingerprint;
}

/// <summary>Data describing physical target evidence; the owning provider must verify that evidence.</summary>
public sealed class RuntimeTargetAcceptanceReceipt
{
    [JsonConstructor]
    public RuntimeTargetAcceptanceReceipt(string recipientId, string sealFingerprint, RuntimeSubscriptionStartIdentity startIdentity,
        WorkflowExecutableIdentity material, RuntimeTargetAcceptanceKind kind, string evidenceId, DateTimeOffset observedAt)
    {
        RuntimeDeliveryFingerprint.Text(recipientId, nameof(recipientId)); RuntimeDeliveryFingerprint.Hash(sealFingerprint, nameof(sealFingerprint));
        ArgumentNullException.ThrowIfNull(startIdentity); ArgumentNullException.ThrowIfNull(material);
        RuntimeDeliveryFingerprint.Text(evidenceId, nameof(evidenceId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (observedAt == default || observedAt == DateTimeOffset.MaxValue) throw new ArgumentOutOfRangeException(nameof(observedAt));
        RecipientId = recipientId; SealFingerprint = sealFingerprint; StartIdentity = startIdentity;
        Material = material; Kind = kind; EvidenceId = evidenceId; ObservedAt = observedAt;
    }
    public string RecipientId { get; }
    public string SealFingerprint { get; }
    public RuntimeSubscriptionStartIdentity StartIdentity { get; }
    public WorkflowExecutableIdentity Material { get; }
    public RuntimeTargetAcceptanceKind Kind { get; }
    public string EvidenceId { get; }
    public DateTimeOffset ObservedAt { get; }
    public bool IsEquivalentTo(RuntimeTargetAcceptanceReceipt? other) => other is not null &&
        RuntimeDeliveryFingerprint.Compute("elsa.runtime.target-receipt.v1", this) == RuntimeDeliveryFingerprint.Compute("elsa.runtime.target-receipt.v1", other);
}

/// <summary>Monotonic recipient responsibility, with operational failures separate from its phase.</summary>
public sealed class RuntimeRecipientProgress
{
    [JsonConstructor]
    public RuntimeRecipientProgress(string recipientId, RuntimeRecipientPhase phase, RuntimeDispatchSeal? seal = null,
        RuntimeTargetAcceptanceReceipt? receipt = null, int attemptCount = 0, DateTimeOffset? retryAt = null,
        string? lastFailureCode = null, RuntimeRecipientRecoveryState recoveryState = RuntimeRecipientRecoveryState.None)
    {
        RuntimeDeliveryFingerprint.Text(recipientId, nameof(recipientId));
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        if (!Enum.IsDefined(recoveryState)) throw new ArgumentOutOfRangeException(nameof(recoveryState));
        if (attemptCount < 0) throw new ArgumentOutOfRangeException(nameof(attemptCount));
        if (lastFailureCode is not null) RuntimeDeliveryFingerprint.Text(lastFailureCode, nameof(lastFailureCode));
        var sealedPhase = phase is RuntimeRecipientPhase.DispatchCommitted or RuntimeRecipientPhase.TargetAccepted;
        if (sealedPhase != (seal is not null) || (phase == RuntimeRecipientPhase.TargetAccepted) != (receipt is not null))
            throw new ArgumentException("Recipient phase requires its exact seal and acceptance receipt.", nameof(phase));
        if (seal is not null && seal.RecipientId != recipientId || receipt is not null &&
            (receipt.RecipientId != recipientId || receipt.SealFingerprint != seal!.Fingerprint || !receipt.StartIdentity.IsEquivalentTo(seal.StartIdentity)))
            throw new ArgumentException("Recipient progress must preserve the same sealed identity.", nameof(seal));
        RecipientId = recipientId; Phase = phase; Seal = seal; Receipt = receipt; AttemptCount = attemptCount;
        RetryAt = retryAt; LastFailureCode = lastFailureCode; RecoveryState = recoveryState;
    }
    public string RecipientId { get; }
    public RuntimeRecipientPhase Phase { get; }
    public RuntimeDispatchSeal? Seal { get; }
    public RuntimeTargetAcceptanceReceipt? Receipt { get; }
    public int AttemptCount { get; }
    public DateTimeOffset? RetryAt { get; }
    public string? LastFailureCode { get; }
    public RuntimeRecipientRecoveryState RecoveryState { get; }

    public RuntimeRecipientProgress CommitDispatch(RuntimeDispatchSeal seal)
    {
        ArgumentNullException.ThrowIfNull(seal);
        if (Phase == RuntimeRecipientPhase.CancelledBeforeDispatch) throw new InvalidOperationException("A cancelled recipient cannot commit dispatch.");
        if (Seal is not null)
        {
            if (!Seal.IsEquivalentTo(seal)) throw new InvalidOperationException("A committed dispatch seal cannot be replaced.");
            return this;
        }
        return new(RecipientId, RuntimeRecipientPhase.DispatchCommitted, seal, null, AttemptCount, RetryAt, LastFailureCode, RecoveryState);
    }

    public RuntimeRecipientProgress AcceptTarget(RuntimeTargetAcceptanceReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (Seal is null) throw new InvalidOperationException("Target acceptance requires committed dispatch responsibility.");
        if (Receipt is not null)
        {
            if (!Receipt.IsEquivalentTo(receipt)) throw new InvalidOperationException("A physical target receipt cannot be replaced.");
            return this;
        }
        return new(RecipientId, RuntimeRecipientPhase.TargetAccepted, Seal, receipt, AttemptCount);
    }

    public RuntimeRecipientProgress CancelPending() => Phase == RuntimeRecipientPhase.Pending
        ? new(RecipientId, RuntimeRecipientPhase.CancelledBeforeDispatch, attemptCount: AttemptCount)
        : this;
}

/// <summary>Frozen complete evaluated selection plus monotonic progress. Does not itself grant physical admission.</summary>
public sealed class RuntimeSubscriptionDelivery
{
    public const int CurrentVersion = 1;
    [JsonConstructor]
    public RuntimeSubscriptionDelivery(int version, string sourceOutboxItemId, string sourceFingerprint, PersistenceScope scope,
        IReadOnlyList<RuntimeSubscriptionRecipient> recipients, IReadOnlyList<RuntimeSubscriptionRefusal> refusals,
        IReadOnlyList<RuntimeRecipientProgress> progress, string? selectionFingerprint = null,
        IReadOnlyList<RuntimeSubscriptionResourceCapture>? resourceCaptures = null, RuntimeIngressWaitDelivery? waitDelivery = null)
    {
        if (version != CurrentVersion) throw new NotSupportedException($"Subscription delivery version '{version}' is unsupported.");
        RuntimeDeliveryFingerprint.Text(sourceOutboxItemId, nameof(sourceOutboxItemId)); RuntimeDeliveryFingerprint.Hash(sourceFingerprint, nameof(sourceFingerprint));
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(refusals); ArgumentNullException.ThrowIfNull(progress);
        if (recipients.Any(item => item is null) || refusals.Any(item => item is null) || progress.Any(item => item is null))
            throw new ArgumentException("Delivery collections cannot contain null entries.");
        var accepted = recipients.OrderBy(item => item.SubscriptionId, StringComparer.Ordinal).ToArray();
        var refused = refusals.OrderBy(item => item.SubscriptionId, StringComparer.Ordinal).ToArray();
        if (accepted.Select(item => item.SubscriptionId).Concat(refused.Select(item => item.SubscriptionId)).Distinct(StringComparer.Ordinal).Count() != accepted.Length + refused.Length ||
            accepted.Select(item => item.RecipientId).Distinct(StringComparer.Ordinal).Count() != accepted.Length)
            throw new ArgumentException("Every evaluated subscription and accepted recipient must have one identity.");
        if (accepted.Any(item => item.StartIdentity.SourceOutboxItemId != sourceOutboxItemId || item.StartIdentity.Scope != scope))
            throw new ArgumentException("Every accepted identity must name this exact source and scope.", nameof(recipients));
        var states = progress.OrderBy(item => item.RecipientId, StringComparer.Ordinal).ToArray();
        if (!states.Select(item => item.RecipientId).SequenceEqual(accepted.Select(item => item.RecipientId).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ArgumentException("Progress must contain exactly the accepted recipients.", nameof(progress));
        Version = version; SourceOutboxItemId = sourceOutboxItemId; SourceFingerprint = sourceFingerprint; Scope = scope;
        Recipients = Array.AsReadOnly(accepted); Refusals = Array.AsReadOnly(refused); Progress = Array.AsReadOnly(states);
        if (resourceCaptures is not null)
        {
            if (resourceCaptures.Any(resource => resource is null))
                throw new ArgumentException("Resource captures cannot contain null entries.", nameof(resourceCaptures));
            var resources = resourceCaptures.OrderBy(resource => resource.TriggerId, StringComparer.Ordinal).ToArray();
            if (resources.Select(resource => resource.TriggerId).Distinct(StringComparer.Ordinal).Count() != resources.Length ||
                resources.SelectMany(resource => resource.SubscriptionIds).Distinct(StringComparer.Ordinal).Count() != resources.Sum(resource => resource.SubscriptionIds.Count) ||
                !resources.SelectMany(resource => resource.SubscriptionIds).ToHashSet(StringComparer.Ordinal).SetEquals(
                    accepted.Select(recipient => recipient.SubscriptionId).Concat(refused.Select(refusal => refusal.SubscriptionId))))
                throw new ArgumentException("Resource captures must attribute every evaluated association exactly once.", nameof(resourceCaptures));
            ResourceCaptures = Array.AsReadOnly(resources);
        }
        if (waitDelivery is not null && (waitDelivery.SourceOutboxItemId != sourceOutboxItemId || waitDelivery.SourceFingerprint != sourceFingerprint || waitDelivery.Scope != scope))
            throw new ArgumentException("The ordinary wait cohort must belong to this exact accepted source.", nameof(waitDelivery));
        WaitDelivery = waitDelivery;
        SelectionFingerprint = WaitDelivery is not null
            ? RuntimeDeliveryFingerprint.Compute("elsa.runtime.subscription-selection.v1", new { version, sourceOutboxItemId, sourceFingerprint, scope, recipients = Recipients, refusals = Refusals, resourceCaptures = ResourceCaptures, waitSelectionFingerprint = WaitDelivery.SelectionFingerprint })
            : ResourceCaptures is null
            ? RuntimeDeliveryFingerprint.Compute("elsa.runtime.subscription-selection.v1", new { version, sourceOutboxItemId, sourceFingerprint, scope, recipients = Recipients, refusals = Refusals })
            : RuntimeDeliveryFingerprint.Compute("elsa.runtime.subscription-selection.v1", new { version, sourceOutboxItemId, sourceFingerprint, scope, recipients = Recipients, refusals = Refusals, resourceCaptures = ResourceCaptures });
        if (selectionFingerprint is not null && selectionFingerprint != SelectionFingerprint)
            throw new ArgumentException("The saved selection fingerprint differs from its complete immutable selection.", nameof(selectionFingerprint));
        foreach (var state in states)
        {
            var recipient = accepted.Single(item => item.RecipientId == state.RecipientId);
            if (state.Seal is { } seal && (seal.SourceOutboxItemId != sourceOutboxItemId || seal.SourceFingerprint != sourceFingerprint ||
                seal.SelectionFingerprint != SelectionFingerprint || !seal.StartIdentity.IsEquivalentTo(recipient.StartIdentity)))
                throw new ArgumentException("A dispatch seal must belong to this exact accepted selection.", nameof(progress));
            if (state.Receipt is { } receipt && receipt.Material != recipient.Material)
                throw new ArgumentException("A target receipt must preserve the exact accepted material.", nameof(progress));
        }
    }
    public int Version { get; }
    public string SourceOutboxItemId { get; }
    public string SourceFingerprint { get; }
    public PersistenceScope Scope { get; }
    public IReadOnlyList<RuntimeSubscriptionRecipient> Recipients { get; }
    public IReadOnlyList<RuntimeSubscriptionRefusal> Refusals { get; }
    public IReadOnlyList<RuntimeRecipientProgress> Progress { get; }
    public string SelectionFingerprint { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RuntimeSubscriptionResourceCapture>? ResourceCaptures { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeIngressWaitDelivery? WaitDelivery { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeIngressControlFacts? ControlFacts { get; init; }
    [JsonIgnore] public bool IsSettled => Progress.All(item => item.Phase is RuntimeRecipientPhase.TargetAccepted or RuntimeRecipientPhase.CancelledBeforeDispatch) &&
        (WaitDelivery?.IsSettled ?? true);

    public RuntimeSubscriptionDelivery WithControlFacts(RuntimeIngressControlFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (ControlFacts is not null) facts.EnsurePreserves(ControlFacts);
        return new(Version, SourceOutboxItemId, SourceFingerprint, Scope, Recipients, Refusals, Progress, SelectionFingerprint, ResourceCaptures, WaitDelivery)
            { ControlFacts = facts };
    }

    public RuntimeSubscriptionDelivery WithProgress(RuntimeRecipientProgress replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var current = Progress.SingleOrDefault(item => item.RecipientId == replacement.RecipientId)
            ?? throw new ArgumentException("No selected recipient has this identity.", nameof(replacement));
        if (replacement.AttemptCount < current.AttemptCount || current.Phase == RuntimeRecipientPhase.Pending && replacement.Phase == RuntimeRecipientPhase.TargetAccepted)
            throw new InvalidOperationException("Progress cannot lose attempts or skip committed dispatch responsibility.");
        if (current.Phase == RuntimeRecipientPhase.CancelledBeforeDispatch && replacement.Phase != current.Phase ||
            current.Seal is { } seal && (replacement.Seal is null || !seal.IsEquivalentTo(replacement.Seal)) ||
            current.Receipt is { } receipt && (replacement.Receipt is null || !receipt.IsEquivalentTo(replacement.Receipt)))
            throw new InvalidOperationException("Recipient progress cannot erase cancellation, committed dispatch or target acceptance.");
        return new(Version, SourceOutboxItemId, SourceFingerprint, Scope, Recipients, Refusals,
            Progress.Select(item => item.RecipientId == replacement.RecipientId ? replacement : item).ToArray(), SelectionFingerprint, ResourceCaptures, WaitDelivery)
            { ControlFacts = ControlFacts };
    }

    public RuntimeSubscriptionDelivery WithWaitDelivery(RuntimeIngressWaitDelivery replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (WaitDelivery is null || WaitDelivery.SelectionFingerprint != replacement.SelectionFingerprint)
            throw new InvalidOperationException("The complete accepted wait selection cannot be installed or replaced through progress updates.");
        foreach (var progress in replacement.Progress) _ = WaitDelivery.WithProgress(progress);
        return new(Version, SourceOutboxItemId, SourceFingerprint, Scope, Recipients, Refusals, Progress,
            SelectionFingerprint, ResourceCaptures, replacement) { ControlFacts = ControlFacts };
    }

    public bool IsEquivalentTo(RuntimeSubscriptionDelivery? other) => other is not null &&
        RuntimeDeliveryFingerprint.Compute("elsa.runtime.subscription-delivery.v1", this) == RuntimeDeliveryFingerprint.Compute("elsa.runtime.subscription-delivery.v1", other);
}
