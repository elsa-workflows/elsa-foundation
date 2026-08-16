using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

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
