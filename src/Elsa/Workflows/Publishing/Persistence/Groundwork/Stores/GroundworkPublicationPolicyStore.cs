using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkPublicationPolicyStore(
    GroundworkPublishingStorage storage,
    PublishingGroundworkDocumentSerializer serializer)
    : GroundworkPublishingStore(storage, serializer, PublishingGroundworkStorageManifest.PublicationPolicyDocumentKind),
        IPublicationPolicyStore
{
    public ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load<PolicyDocument>(Key(workflowDefinitionId))?.Document.Policy);
    }

    public ValueTask<PublicationPolicyWriteResult> TrySaveAsync(
        PublicationPolicy policy,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.DefaultSlotName);
        cancellationToken.ThrowIfCancellationRequested();

        var id = Key(policy.WorkflowDefinitionId);
        var loaded = Load<PolicyDocument>(id);
        var currentRevision = loaded?.Document.Policy.Revision ?? 0;
        if (currentRevision != expectedRevision)
            return ValueTask.FromResult(new PublicationPolicyWriteResult(false, loaded?.Document.Policy ?? policy));

        var saved = policy with { Revision = currentRevision + 1 };
        if (Save(id, new PolicyDocument(policy.WorkflowDefinitionId, saved), loaded?.Entry.Version, Projections(policy.WorkflowDefinitionId)))
            return ValueTask.FromResult(new PublicationPolicyWriteResult(true, saved));

        var winner = Load<PolicyDocument>(id)?.Document.Policy ?? policy;
        return ValueTask.FromResult(new PublicationPolicyWriteResult(false, winner));
    }

    private static IReadOnlyDictionary<string, object?> Projections(string? workflowDefinitionId) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.WorkflowDefinitionIdField] = workflowDefinitionId
        };

    /// <summary>
    /// The host-wide policy owns a sentinel key. A definition's key carries the definition-id length so
    /// no definition id can be spelled to collide with another definition's key, or with the sentinel.
    /// </summary>
    private static string Key(string? workflowDefinitionId) => workflowDefinitionId is null
        ? "host"
        : $"workflow:{workflowDefinitionId.Length}:{workflowDefinitionId}";

    private sealed record PolicyDocument(string? WorkflowDefinitionId, PublicationPolicy Policy);
}
