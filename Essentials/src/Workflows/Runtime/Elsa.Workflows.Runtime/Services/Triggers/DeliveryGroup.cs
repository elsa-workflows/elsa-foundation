using System.Collections.ObjectModel;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

// Internal accounting only. These values are not persisted protocol contracts, source authority,
// scheduler admissions or a StateMachine selector. Physical participants are a separate integration step.
internal sealed record DeliveryGroupIdentity(PersistenceScope Scope, string? TenantId, WorkflowExecutionPartition Partition, string WorkflowExecutionId,
    string ReceiverInvocationId, WorkflowExecutableIdentity Executable, string ReceiverProtocol,
    int ReceiverVersion, string SourceKind, int SourceVersion, string SourceId, string SourceFingerprint);

internal sealed record DeliveryRecipient(string SlotId, string WaitActivityId, string RegistrationId,
    string RegistrationGeneration, string RegistrationFingerprint, string VisitId, string TriggerOperationId);

internal sealed record DeliveryWorkReference(string WorkId, string SlotId, string Fingerprint);

internal sealed record DeliveryReadyOccurrence(string OccurrenceId, string SlotId, string VisitId,
    string TriggerOperationId, string TriggerActivationId, string CompletionId, string ResultReferenceId);

internal enum DeliveryGroupStatus { Open, SealedWaiting, Blocked, ClosedReady, ConsumerEnded }
internal enum DeliveryRecipientStatus { Expected, Queued, Applied, Retired, Blocked }
internal enum DeliveryWorkStatus { Runnable, Blocked, Settled, Parked, Retired }

/// <summary>
/// Immutable outstanding-work accounting for an already validated source/consumer manifest.
/// It deliberately retains completed work identities: replay cannot recreate settled debt.
/// </summary>
internal sealed class DeliveryGroup
{
    private sealed record Slot(DeliveryRecipient Recipient, DeliveryRecipientStatus Status = DeliveryRecipientStatus.Expected,
        DeliveryWorkReference? InitialWork = null, string? AppliedWorkId = null, string? Retirement = null);

    private sealed record Work(DeliveryWorkReference Reference, DeliveryWorkStatus Status,
        string? FailureId = null, long FailureClaimToken = 0, string? DecisionId = null,
        IReadOnlyList<DeliveryWorkReference>? Successors = null,
        IReadOnlyList<DeliveryReadyOccurrence>? Occurrences = null);

    private readonly IReadOnlyDictionary<string, Slot> slots;
    private readonly IReadOnlyDictionary<string, Work> work;
    private readonly IReadOnlyDictionary<string, DeliveryReadyOccurrence> occurrences;

    internal DeliveryGroup(DeliveryGroupIdentity identity, string manifestFingerprint,
        IReadOnlyCollection<DeliveryRecipient> recipients, int workBudget)
    {
        ValidateIdentity(identity);
        RequireFingerprint(manifestFingerprint);
        ArgumentNullException.ThrowIfNull(recipients);
        if (workBudget <= 0 || workBudget < recipients.Count)
            throw new ArgumentOutOfRangeException(nameof(workBudget), "The work budget must reserve the captured recipient set.");
        foreach (var recipient in recipients) ValidateRecipient(recipient);
        if (recipients.Select(item => item.SlotId).Distinct(StringComparer.Ordinal).Count() != recipients.Count ||
            recipients.Select(item => (item.WaitActivityId, item.RegistrationId, item.RegistrationGeneration)).Distinct().Count() != recipients.Count)
            throw new ArgumentException("A captured recipient can occur only once.", nameof(recipients));
        Identity = identity;
        ManifestFingerprint = manifestFingerprint;
        WorkBudget = workBudget;
        slots = Snapshot(recipients.ToDictionary(item => item.SlotId, item => new Slot(item), StringComparer.Ordinal));
        work = Snapshot(new Dictionary<string, Work>(StringComparer.Ordinal));
        occurrences = Snapshot(new Dictionary<string, DeliveryReadyOccurrence>(StringComparer.Ordinal));
    }

    private DeliveryGroup(DeliveryGroup original, Dictionary<string, Slot>? slots = null,
        Dictionary<string, Work>? work = null, Dictionary<string, DeliveryReadyOccurrence>? occurrences = null,
        string? seal = null, bool close = false, string? ended = null)
    {
        Identity = original.Identity;
        ManifestFingerprint = original.ManifestFingerprint;
        WorkBudget = original.WorkBudget;
        this.slots = slots is null ? original.slots : Snapshot(slots);
        this.work = work is null ? original.work : Snapshot(work);
        this.occurrences = occurrences is null ? original.occurrences : Snapshot(occurrences);
        SealFingerprint = seal ?? original.SealFingerprint;
        IsClosed = close || original.IsClosed;
        EndedReason = ended ?? original.EndedReason;
        Revision = checked(original.Revision + 1);
    }

    internal DeliveryGroupIdentity Identity { get; }
    internal string ManifestFingerprint { get; }
    internal int WorkBudget { get; }
    internal long Revision { get; }
    internal string? SealFingerprint { get; }
    internal string? EndedReason { get; }
    internal bool IsClosed { get; }
    internal int KnownWorkCount => work.Count;
    internal int RunnableCount => work.Values.Count(item => item.Status == DeliveryWorkStatus.Runnable);
    internal IReadOnlyList<DeliveryReadyOccurrence> Ready => Array.AsReadOnly(
        occurrences.Values.OrderBy(item => item.OccurrenceId, StringComparer.Ordinal).ToArray());
    internal DeliveryGroupStatus Status => EndedReason is not null ? DeliveryGroupStatus.ConsumerEnded :
        IsClosed ? DeliveryGroupStatus.ClosedReady :
        work.Values.Any(item => item.Status == DeliveryWorkStatus.Blocked) ? DeliveryGroupStatus.Blocked :
        SealFingerprint is null ? DeliveryGroupStatus.Open : DeliveryGroupStatus.SealedWaiting;

    internal bool CanClose => EndedReason is null && !IsClosed && SealFingerprint is not null &&
        slots.Values.All(item => item.Status is DeliveryRecipientStatus.Applied or DeliveryRecipientStatus.Retired) &&
        work.Values.All(item => item.Status is DeliveryWorkStatus.Settled or DeliveryWorkStatus.Parked or DeliveryWorkStatus.Retired);

    internal DeliveryRecipientStatus RecipientStatus(string slotId)
    {
        var slot = RequireSlot(slotId);
        return work.Values.Any(item => item.Reference.SlotId == slotId && item.Status == DeliveryWorkStatus.Blocked)
            ? DeliveryRecipientStatus.Blocked : slot.Status;
    }

    internal DeliveryWorkStatus WorkStatus(string workId) => work.TryGetValue(workId, out var value)
        ? value.Status : throw new InvalidOperationException("The group does not own that work identity.");

    internal DeliveryGroup Queue(DeliveryGroupIdentity identity, DeliveryWorkReference initial)
    {
        RequireIdentity(identity);
        ValidateWork(initial);
        var slot = RequireSlot(initial.SlotId);
        if (EndedReason is not null || slot.Status == DeliveryRecipientStatus.Retired) return this;
        if (slot.InitialWork is { } existing)
            return existing == initial ? this : throw new InvalidOperationException("A recipient cannot change its original queued work.");
        RequireOpen();
        Reserve([initial]);
        var nextSlots = Copy(slots);
        nextSlots[initial.SlotId] = slot with { Status = DeliveryRecipientStatus.Queued, InitialWork = initial };
        var nextWork = Copy(work);
        nextWork.Add(initial.WorkId, new Work(initial, DeliveryWorkStatus.Runnable));
        return new DeliveryGroup(this, nextSlots, nextWork);
    }

    internal DeliveryGroup Apply(DeliveryGroupIdentity identity, DeliveryWorkReference activeWork)
    {
        RequireIdentity(identity);
        var current = RequireWork(activeWork);
        var slot = RequireSlot(activeWork.SlotId);
        if (EndedReason is not null || slot.Status == DeliveryRecipientStatus.Retired) return this;
        if (slot.Status == DeliveryRecipientStatus.Applied)
            return slot.AppliedWorkId == activeWork.WorkId ? this :
                throw new InvalidOperationException("A recipient cannot be applied by another work item.");
        RequireOpen();
        if (slot.Status != DeliveryRecipientStatus.Queued || current.Status != DeliveryWorkStatus.Runnable)
            throw new InvalidOperationException("Only the queued recipient's runnable work can apply its wait.");
        var next = Copy(slots);
        next[activeWork.SlotId] = slot with { Status = DeliveryRecipientStatus.Applied, AppliedWorkId = activeWork.WorkId };
        return new DeliveryGroup(this, slots: next);
    }

    internal DeliveryGroup RetireBeforeConsumption(DeliveryGroupIdentity identity, string slotId, string retirement)
    {
        RequireIdentity(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(retirement);
        var slot = RequireSlot(slotId);
        if (EndedReason is not null) return this;
        if (slot.Status == DeliveryRecipientStatus.Retired)
            return slot.Retirement == retirement ? this : throw new InvalidOperationException("Retirement evidence cannot change.");
        RequireOpen();
        if (slot.Status == DeliveryRecipientStatus.Applied)
            throw new InvalidOperationException("An applied wait cannot be retired as an unconsumed recipient.");
        var nextSlots = Copy(slots);
        nextSlots[slotId] = slot with { Status = DeliveryRecipientStatus.Retired, Retirement = retirement };
        var nextWork = Copy(work);
        if (slot.InitialWork is { } initial)
        {
            var current = RequireWork(initial);
            if (current.Status is not (DeliveryWorkStatus.Runnable or DeliveryWorkStatus.Blocked))
                throw new InvalidOperationException("Recipient retirement cannot discard transferred work.");
            nextWork[initial.WorkId] = current with { Status = DeliveryWorkStatus.Retired, DecisionId = retirement };
        }
        return new DeliveryGroup(this, nextSlots, nextWork);
    }

    internal DeliveryGroup Block(DeliveryGroupIdentity identity, DeliveryWorkReference reference, string failureId, long claimToken)
    {
        RequireIdentity(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureId);
        if (claimToken <= 0) throw new ArgumentOutOfRangeException(nameof(claimToken));
        var current = RequireWork(reference);
        if (EndedReason is not null) return this;
        // The normal scheduler claim token is monotonic for this work identity. Retaining its high-water
        // mark prevents an older fault replay after any number of repairs, without an unbounded ID set.
        // This recorded value is evidence only; the real checkpoint must still validate current authority.
        if (claimToken < current.FailureClaimToken) return this;
        if (claimToken == current.FailureClaimToken)
            return current.FailureId == failureId ? this : throw new InvalidOperationException("One work claim cannot change its failure identity.");
        RequireOpen();
        if (current.Status != DeliveryWorkStatus.Runnable)
            throw new InvalidOperationException("Completed or parked work cannot become a new failure.");
        var next = Copy(work);
        next[reference.WorkId] = current with { Status = DeliveryWorkStatus.Blocked, FailureId = failureId, FailureClaimToken = claimToken };
        return new DeliveryGroup(this, work: next);
    }

    internal DeliveryGroup ResumeBlocked(DeliveryGroupIdentity identity, DeliveryWorkReference reference, string failureId, long claimToken)
    {
        RequireIdentity(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureId);
        if (claimToken <= 0) throw new ArgumentOutOfRangeException(nameof(claimToken));
        var current = RequireWork(reference);
        if (EndedReason is not null) return this;
        if (claimToken < current.FailureClaimToken) return this;
        // A duplicate old repair cannot clear a failure from a later normal work claim.
        if (claimToken != current.FailureClaimToken || current.FailureId != failureId)
            throw new InvalidOperationException("Recovery does not match the retained failure.");
        if (current.Status != DeliveryWorkStatus.Blocked) return this;
        RequireOpen();
        var next = Copy(work);
        next[reference.WorkId] = current with { Status = DeliveryWorkStatus.Runnable };
        return new DeliveryGroup(this, work: next);
    }

    internal DeliveryGroup Decide(DeliveryGroupIdentity identity, DeliveryWorkReference reference, string decisionId,
        DeliveryWorkStatus disposition, IReadOnlyCollection<DeliveryWorkReference>? successors = null,
        IReadOnlyCollection<DeliveryReadyOccurrence>? ready = null)
    {
        RequireIdentity(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionId);
        if (disposition is not (DeliveryWorkStatus.Settled or DeliveryWorkStatus.Parked or DeliveryWorkStatus.Retired))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        var current = RequireWork(reference);
        if (successors?.Any(item => item is null) == true || ready?.Any(item => item is null) == true)
            throw new ArgumentException("A work decision cannot contain null members.");
        var nextReferences = (successors ?? []).OrderBy(item => item.WorkId, StringComparer.Ordinal).ToArray();
        var nextOccurrences = (ready ?? []).OrderBy(item => item.OccurrenceId, StringComparer.Ordinal).ToArray();
        if (EndedReason is not null) return this;
        if (current.DecisionId is not null)
            return current.DecisionId == decisionId && current.Status == disposition &&
                   (current.Successors ?? []).SequenceEqual(nextReferences) && (current.Occurrences ?? []).SequenceEqual(nextOccurrences)
                ? this : throw new InvalidOperationException("A work decision cannot change after commitment.");
        RequireOpen();
        if (current.Status != DeliveryWorkStatus.Runnable || RequireSlot(reference.SlotId).Status != DeliveryRecipientStatus.Applied)
            throw new InvalidOperationException("Only applied runnable work can transfer, complete or park.");
        if (disposition != DeliveryWorkStatus.Settled && (nextReferences.Length != 0 || nextOccurrences.Length != 0))
            throw new InvalidOperationException("Parked or retired work cannot also emit runnable successors or ready results.");
        foreach (var successor in nextReferences)
        {
            ValidateWork(successor);
            if (successor.SlotId != reference.SlotId)
                throw new InvalidOperationException("Work cannot transfer into another recipient slot.");
        }
        Reserve(nextReferences);
        var nextReady = Copy(occurrences);
        foreach (var occurrence in nextOccurrences)
        {
            ValidateOccurrence(occurrence, reference.SlotId);
            if (nextReady.TryGetValue(occurrence.OccurrenceId, out var prior) && prior != occurrence)
                throw new InvalidOperationException("A ready occurrence cannot change its committed identity or result.");
            nextReady[occurrence.OccurrenceId] = occurrence;
        }
        var nextWork = Copy(work);
        nextWork[reference.WorkId] = current with
        {
            Status = disposition, DecisionId = decisionId,
            Successors = Array.AsReadOnly(nextReferences), Occurrences = Array.AsReadOnly(nextOccurrences)
        };
        foreach (var successor in nextReferences)
            nextWork.Add(successor.WorkId, new Work(successor, DeliveryWorkStatus.Runnable));
        return new DeliveryGroup(this, work: nextWork, occurrences: nextReady);
    }

    internal DeliveryGroup Seal(DeliveryGroupIdentity identity, string manifestFingerprint, string sealFingerprint)
    {
        RequireIdentity(identity);
        RequireFingerprint(sealFingerprint);
        if (ManifestFingerprint != manifestFingerprint)
            throw new InvalidOperationException("The source seal does not match the captured manifest.");
        if (SealFingerprint is not null)
            return SealFingerprint == sealFingerprint ? this : throw new InvalidOperationException("The source seal cannot change.");
        if (EndedReason is not null) return this;
        RequireOpen();
        return new DeliveryGroup(this, seal: sealFingerprint);
    }

    internal DeliveryGroup Close(DeliveryGroupIdentity identity)
    {
        RequireIdentity(identity);
        if (IsClosed || EndedReason is not null) return this;
        if (!CanClose) throw new InvalidOperationException("Source, recipient or runnable/fault responsibility remains owed.");
        return new DeliveryGroup(this, close: true);
    }

    internal DeliveryGroup EndConsumer(DeliveryGroupIdentity identity, string reason)
    {
        RequireIdentity(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        // Closed evidence is immutable; a later ended receiver is handled by its input disposition owner.
        if (IsClosed) return this;
        if (EndedReason is not null)
            return EndedReason == reason ? this : throw new InvalidOperationException("The consumer's ended disposition cannot change.");
        var nextSlots = slots.ToDictionary(pair => pair.Key, pair => pair.Value.Status is DeliveryRecipientStatus.Expected or DeliveryRecipientStatus.Queued
            ? pair.Value with { Status = DeliveryRecipientStatus.Retired, Retirement = reason } : pair.Value);
        var nextWork = work.ToDictionary(pair => pair.Key, pair => pair.Value.Status is DeliveryWorkStatus.Runnable or DeliveryWorkStatus.Blocked
            ? pair.Value with { Status = DeliveryWorkStatus.Retired, DecisionId = reason } : pair.Value);
        return new DeliveryGroup(this, nextSlots, nextWork, ended: reason);
    }

    private void RequireIdentity(DeliveryGroupIdentity identity)
    {
        if (Identity != identity) throw new InvalidOperationException("The operation belongs to a different exact consumer or source.");
    }

    private void RequireOpen()
    {
        if (IsClosed || EndedReason is not null) throw new InvalidOperationException("An ended or closed group cannot acquire more work.");
    }

    private Slot RequireSlot(string slotId) => slots.TryGetValue(slotId, out var slot)
        ? slot : throw new InvalidOperationException("The recipient is not in the original manifest.");

    private Work RequireWork(DeliveryWorkReference reference)
    {
        ValidateWork(reference);
        return work.TryGetValue(reference.WorkId, out var current) && current.Reference == reference
            ? current : throw new InvalidOperationException("Work does not match its exact committed identity.");
    }

    private void Reserve(IReadOnlyCollection<DeliveryWorkReference> references)
    {
        if (references.Select(item => item.WorkId).Distinct(StringComparer.Ordinal).Count() != references.Count ||
            references.Any(item => work.ContainsKey(item.WorkId)))
            throw new InvalidOperationException("A work identity cannot be reused, merged or reopened.");
        if (references.Count > WorkBudget - work.Count)
            throw new InvalidOperationException("The delivery group's retained work budget is exhausted.");
    }

    private void ValidateOccurrence(DeliveryReadyOccurrence occurrence, string slotId)
    {
        var slot = RequireSlot(slotId).Recipient;
        RequireStrings(occurrence.OccurrenceId, occurrence.TriggerActivationId, occurrence.CompletionId, occurrence.ResultReferenceId);
        if (occurrence.SlotId != slotId || occurrence.VisitId != slot.VisitId || occurrence.TriggerOperationId != slot.TriggerOperationId)
            throw new InvalidOperationException("A ready occurrence must belong to this exact captured trigger operation and visit.");
    }

    private static void ValidateIdentity(DeliveryGroupIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(identity.Scope);
        ArgumentNullException.ThrowIfNull(identity.Partition);
        ArgumentNullException.ThrowIfNull(identity.Executable);
        if (identity.TenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(identity.TenantId);
        RequireStrings(identity.WorkflowExecutionId, identity.ReceiverInvocationId, identity.ReceiverProtocol,
            identity.SourceKind, identity.SourceId, identity.Executable.ArtifactId, identity.Executable.ArtifactHash,
            identity.Executable.ArtifactVersion, identity.Executable.DefinitionId, identity.Executable.DefinitionVersionId);
        if (identity.ReceiverVersion <= 0 || identity.SourceVersion <= 0) throw new ArgumentOutOfRangeException(nameof(identity));
        RequireFingerprint(identity.SourceFingerprint);
    }

    private static void ValidateRecipient(DeliveryRecipient recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        RequireStrings(recipient.SlotId, recipient.WaitActivityId, recipient.RegistrationId,
            recipient.RegistrationGeneration, recipient.VisitId, recipient.TriggerOperationId);
        RequireFingerprint(recipient.RegistrationFingerprint);
    }

    private static void ValidateWork(DeliveryWorkReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        RequireStrings(reference.WorkId, reference.SlotId);
        RequireFingerprint(reference.Fingerprint);
    }

    private static void RequireStrings(params string[] values)
    {
        foreach (var value in values) ArgumentException.ThrowIfNullOrWhiteSpace(value);
    }

    private static void RequireFingerprint(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("The complete canonical SHA256 fingerprint is required.", nameof(value));
    }

    private static Dictionary<string, T> Copy<T>(IReadOnlyDictionary<string, T> values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, T> Snapshot<T>(Dictionary<string, T> values) =>
        new ReadOnlyDictionary<string, T>(values);
}
