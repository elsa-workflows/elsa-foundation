using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class InMemoryPublicationSlotStore : IPublicationSlotStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PublicationSlot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _publicationSlots = new(StringComparer.Ordinal);

    public ValueTask<PublicationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
    {
        var slotId = SlotId(workflowDefinitionId, slotName, cancellationToken);
        lock (_gate)
            return ValueTask.FromResult(_slots.GetValueOrDefault(slotId));
    }

    public ValueTask<IReadOnlyCollection<PublicationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<PublicationSlot>>(_slots.Values
                .Where(slot => StringComparer.Ordinal.Equals(slot.WorkflowDefinitionId, workflowDefinitionId))
                .OrderBy(slot => slot.SlotName, StringComparer.Ordinal)
                .ToArray());
    }

    public ValueTask<PublicationSlotTransitionResult> TryActivateAsync(
        string workflowDefinitionId,
        string slotName,
        string publicationId,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var slotId = SlotId(workflowDefinitionId, slotName, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);

        lock (_gate)
        {
            var current = CurrentOrEmpty(slotId, workflowDefinitionId, slotName, updatedAt);
            if (current.Revision != expectedRevision)
                return ValueTask.FromResult(Failed(current, "slot_revision_conflict", "The publication slot revision changed."));

            if (_publicationSlots.TryGetValue(publicationId, out var existingSlotId) && !StringComparer.Ordinal.Equals(existingSlotId, slotId))
                return ValueTask.FromResult(Failed(current, "publication_already_active", "The publication is already active in another slot."));

            var replacedPublicationId = current.ActivePublicationId;
            if (replacedPublicationId is not null && !StringComparer.Ordinal.Equals(replacedPublicationId, publicationId))
                _publicationSlots.Remove(replacedPublicationId);

            var next = current with
            {
                ActivePublicationId = publicationId,
                Revision = current.Revision + 1,
                UpdatedAt = updatedAt
            };
            _slots[slotId] = next;
            _publicationSlots[publicationId] = slotId;
            return ValueTask.FromResult(new PublicationSlotTransitionResult(true, next, replacedPublicationId));
        }
    }

    public ValueTask<PublicationSlotTransitionResult> TryUnpublishAsync(
        string workflowDefinitionId,
        string slotName,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var slotId = SlotId(workflowDefinitionId, slotName, cancellationToken);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);

        lock (_gate)
        {
            var current = CurrentOrEmpty(slotId, workflowDefinitionId, slotName, updatedAt);
            if (current.Revision != expectedRevision)
                return ValueTask.FromResult(Failed(current, "slot_revision_conflict", "The publication slot revision changed."));

            var replacedPublicationId = current.ActivePublicationId;
            if (replacedPublicationId is not null)
                _publicationSlots.Remove(replacedPublicationId);

            var next = current with
            {
                ActivePublicationId = null,
                Revision = current.Revision + 1,
                UpdatedAt = updatedAt
            };
            _slots[slotId] = next;
            return ValueTask.FromResult(new PublicationSlotTransitionResult(true, next, replacedPublicationId));
        }
    }

    private PublicationSlot CurrentOrEmpty(string slotId, string workflowDefinitionId, string slotName, DateTimeOffset updatedAt) =>
        _slots.GetValueOrDefault(slotId) ?? new PublicationSlot(slotId, workflowDefinitionId, slotName, null, 0, updatedAt);

    private static PublicationSlotTransitionResult Failed(PublicationSlot slot, string code, string message) =>
        new(false, slot, Failure: new PublicationFailure(code, message));

    private static string SlotId(string workflowDefinitionId, string slotName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublicationSlotIdentity.Create(workflowDefinitionId, slotName);
    }
}

public sealed class InMemoryPublicationRecordStore : IPublicationRecordStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PublicationRecord> _records = new(StringComparer.Ordinal);

    public ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentException.ThrowIfNullOrWhiteSpace(publication.PublicationId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_records.TryGetValue(publication.PublicationId, out var existing) && existing != publication)
                throw new InvalidOperationException($"Publication '{publication.PublicationId}' already exists.");
            _records[publication.PublicationId] = publication;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(_records.GetValueOrDefault(publicationId));
    }

    public ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<PublicationRecord>>(_records.Values
                .Where(publication => StringComparer.Ordinal.Equals(publication.SlotId, slotId))
                .OrderBy(publication => publication.CreatedAt)
                .ThenBy(publication => publication.PublicationId, StringComparer.Ordinal)
                .ToArray());
    }

    public ValueTask<bool> TryTransitionAsync(PublicationRecord publication, PublicationStatus expectedStatus, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_records.TryGetValue(publication.PublicationId, out var current) || current.Status != expectedStatus)
                return ValueTask.FromResult(false);
            EnsureSameIdentity(current, publication);
            _records[publication.PublicationId] = publication;
            return ValueTask.FromResult(true);
        }
    }

    private static void EnsureSameIdentity(PublicationRecord current, PublicationRecord next)
    {
        if (current.PublicationId != next.PublicationId || current.SlotId != next.SlotId ||
            current.WorkflowDefinitionId != next.WorkflowDefinitionId ||
            current.WorkflowDefinitionVersionId != next.WorkflowDefinitionVersionId ||
            current.SlotName != next.SlotName ||
            current.ArtifactId != next.ArtifactId || current.ExpectedSlotRevision != next.ExpectedSlotRevision ||
            current.CreatedAt != next.CreatedAt)
            throw new InvalidOperationException("A publication lifecycle transition cannot change immutable publication identity.");
    }
}

public sealed class InMemoryPublicationPolicyStore : IPublicationPolicyStore
{
    private const string HostKey = "\0host";
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PublicationPolicy> _policies = new(StringComparer.Ordinal);

    public ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(_policies.GetValueOrDefault(Key(workflowDefinitionId)));
    }

    public ValueTask<PublicationPolicyWriteResult> TrySaveAsync(PublicationPolicy policy, long expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.DefaultSlotName);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = Key(policy.WorkflowDefinitionId);
            var currentRevision = _policies.TryGetValue(key, out var current) ? current.Revision : 0;
            if (currentRevision != expectedRevision)
                return ValueTask.FromResult(new PublicationPolicyWriteResult(false, current ?? policy));

            var saved = policy with { Revision = currentRevision + 1 };
            _policies[key] = saved;
            return ValueTask.FromResult(new PublicationPolicyWriteResult(true, saved));
        }
    }

    private static string Key(string? workflowDefinitionId) => workflowDefinitionId ?? HostKey;
}

public sealed class InMemoryPublicationProjectionIntentStore : IPublicationProjectionIntentStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PublicationProjectionIntent> _intents = new(StringComparer.Ordinal);

    public ValueTask SaveAsync(PublicationProjectionIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.IntentId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_intents.TryGetValue(intent.IntentId, out var existing) && existing != intent)
                throw new InvalidOperationException($"Publication projection intent '{intent.IntentId}' already exists.");
            _intents[intent.IntentId] = intent;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<PublicationProjectionIntent?> FindAsync(string intentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(_intents.GetValueOrDefault(intentId));
    }

    public ValueTask<IReadOnlyCollection<PublicationProjectionIntent>> ListByPublicationAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<PublicationProjectionIntent>>(_intents.Values
                .Where(intent => StringComparer.Ordinal.Equals(intent.PublicationId, publicationId))
                .OrderBy(intent => intent.IntentId, StringComparer.Ordinal)
                .ToArray());
    }

    public ValueTask<PublicationProjectionIntentTransitionResult> TryTransitionAsync(
        PublicationProjectionIntent intent,
        PublicationProjectionIntentStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_intents.TryGetValue(intent.IntentId, out var current) || current.Status != expectedStatus)
                return ValueTask.FromResult(new PublicationProjectionIntentTransitionResult(false, current ?? intent));
            if (current.PublicationId != intent.PublicationId || current.ProjectionKind != intent.ProjectionKind || current.Operation != intent.Operation)
                throw new InvalidOperationException("A projection-intent transition cannot change immutable delivery identity.");
            _intents[intent.IntentId] = intent;
            return ValueTask.FromResult(new PublicationProjectionIntentTransitionResult(true, intent));
        }
    }
}
