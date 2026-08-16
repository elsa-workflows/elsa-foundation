using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowTriggerBindingStore"/>. Like the other bridge stores it depends
/// only on the provider-neutral <see cref="IDocumentStore"/>; the concrete provider (SQLite, SQL Server,
/// PostgreSQL, MongoDB) is chosen by the host through feature composition and never leaks into this bridge
/// or into runtime domain code.
/// </summary>
public sealed class GroundworkWorkflowTriggerBindingStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkPublicationProjectionStore<WorkflowTriggerBinding>(store, serializer, ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind), IWorkflowTriggerBindingStore
{
    protected override string ProjectionKind => "triggerBindings";
    protected override string ProjectionNoun => "trigger-binding";

    protected override string ItemId(WorkflowTriggerBinding item) => item.TriggerBindingId;

    protected override WorkflowTriggerBinding WithActive(WorkflowTriggerBinding item, bool isActive) =>
        item with { IsActive = isActive };

    protected override object StoragePayload(WorkflowTriggerBinding item) => item;

    protected override async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListAllByPublicationCoreAsync(
        string publicationId,
        CancellationToken cancellationToken) =>
        await this.ListAllByPublicationAsync(publicationId, cancellationToken);
    private readonly IBoundedDocumentStore? _queries = boundedStore ?? store as IBoundedDocumentStore;

    private IBoundedDocumentStore Queries => _queries ?? throw new InvalidOperationException(
        "Workflow trigger-binding queries require an admitted bounded document-store runtime.");

    public async ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        WorkflowTriggerBinding.ValidateId(binding.TriggerBindingId);

        var existing = await Store.LoadAsync(DocumentKind, binding.TriggerBindingId, cancellationToken);
        var result = await SaveDocumentAsync(
            binding.TriggerBindingId,
            binding,
            cancellationToken,
            existing?.Version ?? 0);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return binding;
        if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
        {
            throw new InvalidOperationException(
                $"Groundwork rejected workflow trigger binding '{binding.TriggerBindingId}' with status '{result.Status}'.");
        }

        var winner = await Store.LoadAsync(DocumentKind, binding.TriggerBindingId, cancellationToken);
        if (winner is not null)
        {
            var winnerBinding = Serializer.Deserialize<WorkflowTriggerBinding>(winner);
            if (StringComparer.Ordinal.Equals(
                    Serializer.SerializeForComparison(winnerBinding),
                    Serializer.SerializeForComparison(binding)))
            {
                return binding;
            }
        }

        throw new InvalidOperationException(
            $"Workflow trigger binding '{binding.TriggerBindingId}' changed concurrently and was not overwritten.");
    }

    public async ValueTask PrepareActivationAsync(
        string publicationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(bindings);
        ValidatePublicationBindings(publicationId, bindings);

        await PreparePublicationCoreAsync(publicationId, bindings, cancellationToken);
    }

    public async ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(
        WorkflowTriggerBindingPublicationPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await QueryBindingsPageAsync(
            query,
            ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery,
            [Equal(ElsaRuntimeStorageManifest.PublicationIdField, query.PublicationId)],
            cancellationToken);
    }

    public async ValueTask ActivateAsync(
        string publicationId,
        string? replacedPublicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        if (replacedPublicationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(replacedPublicationId);

        await ActivatePublicationCoreAsync(publicationId, replacedPublicationId, cancellationToken);
    }

    public async ValueTask DeleteByActivationAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        await DeleteByPublicationCoreAsync(publicationId, cancellationToken);
    }

    public async ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var existing = await this.ListAllByArtifactAsync(artifactId, cancellationToken);
        var deleted = 0;

        foreach (var binding in existing)
        {
            var result = await DeleteDocumentAsync(binding.TriggerBindingId, cancellationToken);

            if (result.Status == DocumentStoreWriteStatus.Deleted)
                deleted++;
        }

        return deleted;
    }

    public async ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(
        WorkflowTriggerBindingPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await Queries.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField,
                        StimulusLookupKey.FromPair(query.StimulusType, query.StimulusHash)),
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                        bool.TrueString.ToLowerInvariant())
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.TriggerBindingIdField)],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);

        return new WorkflowTriggerBindingPage(
            query,
            result.Documents.Select(Serializer.Deserialize<WorkflowTriggerBinding>).ToArray(),
            result.TotalCount,
            result.NextContinuation);
    }

    public async ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(
        WorkflowTriggerBindingArtifactPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await QueryBindingsPageAsync(
            query,
            ElsaRuntimeStorageManifest.ListTriggerBindingsByArtifactQuery,
            [Equal(ElsaRuntimeStorageManifest.ArtifactIdField, query.ArtifactId)],
            cancellationToken);
    }

    public async ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(
        WorkflowTriggerBindingTypePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await Queries.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField,
                        StimulusLookupKey.FromType(query.StimulusType)),
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                        bool.TrueString.ToLowerInvariant())
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.TriggerBindingIdField)],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);
        return new WorkflowTriggerBindingPage(
            query,
            result.Documents.Select(Serializer.Deserialize<WorkflowTriggerBinding>).ToArray(),
            result.TotalCount,
            result.NextContinuation);
    }

    private async ValueTask<WorkflowTriggerBindingPage> QueryBindingsPageAsync(
        WorkflowTriggerBindingPageRequest query,
        string queryIdentity,
        IReadOnlyList<DocumentQueryClause> clauses,
        CancellationToken cancellationToken)
    {
        var result = await Queries.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                clauses,
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.TriggerBindingIdField)],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);
        return new WorkflowTriggerBindingPage(
            query,
            result.Documents.Select(Serializer.Deserialize<WorkflowTriggerBinding>).ToArray(),
            result.TotalCount,
            result.NextContinuation);
    }

    private static DocumentQueryClause Equal(string fieldPath, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value));

    private static void ValidatePublicationBindings(
        string publicationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            WorkflowTriggerBinding.ValidateId(binding.TriggerBindingId);
            if (!StringComparer.Ordinal.Equals(binding.ActivationId, publicationId))
                throw new ArgumentException($"Binding '{binding.TriggerBindingId}' does not belong to publication '{publicationId}'.", nameof(bindings));
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.SlotId);
        }
    }

}
