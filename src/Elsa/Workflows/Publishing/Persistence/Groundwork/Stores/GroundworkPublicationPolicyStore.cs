using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkPublicationPolicyStore(IDocumentStore store, PublishingGroundworkDocumentSerializer serializer)
    : GroundworkPublishingStore(store, serializer, PublishingGroundworkStorageManifest.PublicationPolicyDocumentKind), IPublicationPolicyStore
{
    public async ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default) =>
        (await LoadAsync<PolicyDocument>(Key(workflowDefinitionId), cancellationToken))?.Document.Policy;

    public async ValueTask<PublicationPolicyWriteResult> TrySaveAsync(
        PublicationPolicy policy,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.DefaultSlotName);
        var id = Key(policy.WorkflowDefinitionId);
        var loaded = await LoadAsync<PolicyDocument>(id, cancellationToken);
        var currentRevision = loaded?.Document.Policy.Revision ?? 0;
        if (currentRevision != expectedRevision)
            return new PublicationPolicyWriteResult(false, loaded?.Document.Policy ?? policy);

        var saved = policy with { Revision = currentRevision + 1 };
        var result = await SaveAsync(id, new PolicyDocument(policy.WorkflowDefinitionId, saved), loaded?.Envelope.Version ?? 0, cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return new PublicationPolicyWriteResult(true, saved);
        var winner = await FindAsync(policy.WorkflowDefinitionId, cancellationToken) ?? policy;
        return new PublicationPolicyWriteResult(false, winner);
    }

    private static string Key(string? workflowDefinitionId) => workflowDefinitionId is null
        ? "host"
        : $"workflow:{workflowDefinitionId.Length}:{workflowDefinitionId}";

    private sealed record PolicyDocument(string? WorkflowDefinitionId, PublicationPolicy Policy);
}
