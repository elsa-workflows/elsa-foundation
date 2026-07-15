using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>
/// Commits the Design publication and Runtime execution material under one cross-unit Groundwork
/// transaction. A provider unable to honor this scope must reject it rather than expose partial state.
/// </summary>
public sealed class GroundworkActivityPublicationCommand(
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IGroundworkRuntimeDocumentSerializer runtimeSerializer,
    IActivityDefinitionVersionPublicationStore publications,
    GroundworkActivityDependencyProjection dependencyProjection)
    : ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>
{
    private static readonly JsonSerializerOptions DesignJson = GroundworkActivitiesDesignJson.Options;

    public async Task<ActivityPublicationResult> ExecuteAsync(
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ValidateCommit(commit);

        var draftEnvelope = await RequiredAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            commit.Design.DraftId,
            cancellationToken);
        var draft = DeserializeDesign<ActivityDefinitionDraft>(draftEnvelope);
        var authoringEnvelope = await RequiredAuthoringByDefinitionAsync(commit.Design.DefinitionId, cancellationToken);
        var authoring = DeserializeDesign<ActivityDefinitionAuthoringState>(authoringEnvelope);
        ValidateExpectedState(commit.Design, draft, authoring);

        await EnsureNewVersionAsync(commit.Design.CatalogVersion, cancellationToken);
        await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, commit.Design.Publication.Id, cancellationToken);
        await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind, commit.Design.Layout.Id, cancellationToken);
        foreach (var edge in commit.Design.DirectDependencies)
            await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind, edge.Id, cancellationToken);
        await EnsureAbsentAsync(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, commit.SourceReference.SourceReferenceId, cancellationToken);

        var requests = new List<SaveDocumentRequest>();
        var templateRequest = await CreateTemplateRequestAsync(commit.ExecutableTemplate, cancellationToken);
        if (templateRequest is not null)
            requests.Add(templateRequest);
        requests.Add(CreateRuntimeRequest(
            ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
            commit.SourceReference.SourceReferenceId,
            new SourceReferenceDocument(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection,
                commit.SourceReference.ArtifactId,
                commit.SourceReference),
            0));
        requests.Add(CreateRichDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection,
            commit.Design.CatalogVersion,
            0));
        requests.Add(CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection,
            commit.Design.Publication,
            0));
        requests.Add(CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutCollection,
            commit.Design.Layout,
            0));
        requests.AddRange(commit.Design.DirectDependencies.Select(edge => CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeCollection,
            edge,
            0)));

        draft.Status = ActivityDefinitionDraftStatus.Published;
        draft.PublishedVersionId = commit.Design.Publication.DefinitionVersionId;
        draft.LastModifiedAt = commit.Design.Publication.PublishedAt;
        authoring.HeadVersionId = commit.Design.Publication.DefinitionVersionId;
        authoring.LastModifiedAt = commit.Design.Publication.PublishedAt;
        requests.Add(CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection,
            draft,
            draftEnvelope.Version));
        requests.Add(CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
            authoring,
            authoringEnvelope.Version));
        var projection = await PrepareProjectionAsync(commit.Design, cancellationToken);
        requests.Add(projection.Request);

        try
        {
            await store.SaveAllAsync(
                DocumentCommitScope.Of(
                    ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
                    ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
                    ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind),
                requests,
                cancellationToken);
        }
        catch (DocumentAtomicWriteException exception)
        {
            throw Conflict($"Atomic activity publication failed at document '{exception.Id}' with status '{exception.Status}'.", exception);
        }

        return new(
            commit.Design.DefinitionId,
            commit.Design.Publication.DefinitionVersionId,
            commit.Design.DraftId,
            commit.ExecutableTemplate.TemplateId,
            commit.SourceReference.SourceReferenceId,
            commit.Design.Publication.PublishedAt);
    }

    private async Task<ActivityDependencyProjectionPreparedUpdate> PrepareProjectionAsync(
        ActivityPublicationDesignMutation mutation,
        CancellationToken cancellationToken)
    {
        var publication = mutation.Publication;
        var owner = new ActivityDefinitionReference(
            "ActivityVersion",
            publication.DefinitionId,
            publication.DefinitionVersionId,
            publication.Version,
            TemplateHash: publication.TemplateHash,
            TenantId: publication.TenantId,
            Lifecycle: publication.Lifecycle);
        var items = new List<ActivityDependencyItem>(mutation.DirectDependencies.Count);
        foreach (var edge in mutation.DirectDependencies.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal))
        {
            var target = await publications.FindAsync(edge.DependencyVersionId, cancellationToken)
                         ?? throw Conflict($"Dependency publication '{edge.DependencyVersionId}' was not found.");
            var dependency = new ActivityDefinitionReference(
                "ActivityVersion",
                target.DefinitionId,
                target.DefinitionVersionId,
                target.Version,
                TemplateHash: target.TemplateHash,
                TenantId: target.TenantId,
                Lifecycle: target.Lifecycle);
            items.Add(new(
                edge.Id,
                owner,
                dependency,
                new(edge.OccurrenceId, edge.NodeOrigin.ToArray()),
                true,
                1,
                [owner, dependency]));
        }
        var sourceDraft = new ActivityDefinitionReference(
            "ActivityDraft",
            mutation.DefinitionId,
            DraftId: mutation.DraftId,
            Revision: mutation.ExpectedDraftRevision,
            TenantId: publication.TenantId);
        return await dependencyProjection.PrepareUpdateAsync(
            [new(sourceDraft, owner, [], items)],
            publication.PublishedAt,
            cancellationToken);
    }

    private async Task<SaveDocumentRequest?> CreateTemplateRequestAsync(
        ExecutableActivityTemplate template,
        CancellationToken cancellationToken)
    {
        var existing = await store.LoadAsync(ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind, template.TemplateId, cancellationToken);
        if (existing is not null)
        {
            EnsureSameTemplate(runtimeSerializer.Deserialize<TemplateDocument>(existing).Template, template);
            return null;
        }

        var sameHash = await store.QueryAsync(new DocumentStoreQuery(
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateByHash,
            template.TemplateHash), cancellationToken);
        if (sameHash.Count > 0)
            throw Conflict($"Template hash '{template.TemplateHash}' is already bound to another template identity.");

        return CreateRuntimeRequest(
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
            template.TemplateId,
            new TemplateDocument(ElsaRuntimeStorageManifest.ExecutableActivityTemplateCollection, template.TemplateHash, template),
            0);
    }

    private void EnsureSameTemplate(ExecutableActivityTemplate existing, ExecutableActivityTemplate candidate)
    {
        if (!StringComparer.Ordinal.Equals(existing.TemplateHash, candidate.TemplateHash))
            throw Conflict($"Template id '{candidate.TemplateId}' is already bound to a different hash.");
        var existingJson = JsonNode.Parse(runtimeSerializer.SerializeForComparison(existing)) as JsonObject;
        var candidateJson = JsonNode.Parse(runtimeSerializer.SerializeForComparison(candidate)) as JsonObject;
        existingJson?.Remove("createdAt");
        candidateJson?.Remove("createdAt");
        if (!JsonNode.DeepEquals(existingJson, candidateJson))
            throw Conflict($"Template id '{candidate.TemplateId}' and hash '{candidate.TemplateHash}' are already bound to different content.");
    }

    private async Task EnsureNewVersionAsync(ActivityDefinitionVersion candidate, CancellationToken cancellationToken)
    {
        await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, candidate.Id, cancellationToken);
        var envelopes = await store.QueryAsync(new DocumentStoreQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.ByCollectionIndex,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection), cancellationToken);
        var richOptions = GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer);
        foreach (var envelope in envelopes)
        {
            var document = JsonSerializer.Deserialize<GroundworkDocument<ActivityDefinitionVersion>>(envelope.ContentJson, richOptions)
                           ?? throw Conflict($"Activity version document '{envelope.Id}' is unreadable.");
            if (StringComparer.Ordinal.Equals(document.Entity.DefinitionId, candidate.DefinitionId) &&
                StringComparer.Ordinal.Equals(document.Entity.SemVerSortKey, candidate.SemVerSortKey))
                throw Conflict($"Activity version '{candidate.Version}' already exists for definition '{candidate.DefinitionId}'.");
        }
    }

    private static void ValidateExpectedState(
        ActivityPublicationDesignMutation mutation,
        ActivityDefinitionDraft draft,
        ActivityDefinitionAuthoringState authoring)
    {
        if (draft.Revision != mutation.ExpectedDraftRevision)
            throw Conflict("The activity draft revision changed before publication committed.");
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw Conflict("The activity draft is no longer active.");
        if (!StringComparer.Ordinal.Equals(draft.DefinitionId, mutation.DefinitionId) ||
            !StringComparer.Ordinal.Equals(authoring.DefinitionId, mutation.DefinitionId))
            throw Conflict("The publication owner identities do not align.");
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, mutation.ExpectedDefinitionHeadVersionId))
            throw Conflict("The activity definition head changed before publication committed.");
        if (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw Conflict("The activity definition is not Design-owned.");
    }

    private static void ValidateCommit(ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit)
    {
        var design = commit.Design;
        var publication = design.Publication;
        var template = commit.ExecutableTemplate;
        var source = commit.SourceReference;
        if (!StringComparer.Ordinal.Equals(design.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(design.CatalogVersion.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(design.CatalogVersion.Id, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(design.Layout.DefinitionVersionId, publication.DefinitionVersionId))
            throw new ArgumentException("Publication definition/version identities do not align.", nameof(commit));
        if (!StringComparer.Ordinal.Equals(publication.TemplateId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(publication.TemplateHash, template.TemplateHash) ||
            !StringComparer.Ordinal.Equals(source.ArtifactId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(source.SourceReferenceId, publication.SourceReferenceId) ||
            !StringComparer.Ordinal.Equals(source.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(source.DefinitionVersionId, publication.DefinitionVersionId))
            throw new ArgumentException("Publication, template, and Source Reference identities do not align.", nameof(commit));
        if (publication.DirectDependencyCount != design.DirectDependencies.Count ||
            template.DirectDependencies.Count != design.DirectDependencies.Count ||
            publication.ClosedTemplateCount != template.ClosedTemplates.Count ||
            publication.ResumeTargetCount != template.ResumeTargets.Count)
            throw new ArgumentException("Publication dependency summary does not match authoritative material.", nameof(commit));
        if (design.DirectDependencies.Select(x => x.OccurrenceId).Distinct(StringComparer.Ordinal).Count() != design.DirectDependencies.Count)
            throw new ArgumentException("Direct dependency occurrence ids must be unique.", nameof(commit));

        var declaredRequirements = publication.RuntimeRequirements
            .Select(x => (x.ConsumerKey, x.SchemaVersion))
            .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
            .ToArray();
        var actualRequirements = template.RuntimeRequirements
            .Select(x => (x.ConsumerKey, x.SchemaVersion))
            .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
            .ToArray();
        if (!declaredRequirements.SequenceEqual(actualRequirements))
            throw new ArgumentException("Publication Runtime requirements do not match the template.", nameof(commit));

        foreach (var edge in design.DirectDependencies)
        {
            var dependency = template.DirectDependencies.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.OccurrenceId, edge.OccurrenceId));
            if (dependency is null ||
                !StringComparer.Ordinal.Equals(dependency.DefinitionVersionId, edge.DependencyVersionId) ||
                !StringComparer.Ordinal.Equals(dependency.TemplateHash, edge.DependencyTemplateHash) ||
                !StringComparer.Ordinal.Equals(edge.OwnerVersionId, publication.DefinitionVersionId) ||
                !StringComparer.Ordinal.Equals(edge.OwnerTemplateHash, publication.TemplateHash))
                throw new ArgumentException("A dependency edge does not match the template.", nameof(commit));
        }

        if (!SemVer.TryParse(publication.Version, out var publicationVersion) ||
            !SemVer.TryParse(design.CatalogVersion.Version, out var catalogVersion) ||
            publicationVersion != catalogVersion ||
            !StringComparer.Ordinal.Equals(source.ArtifactVersion, publication.Version))
            throw new ArgumentException("Publication version labels do not align.", nameof(commit));
        if (!StringComparer.Ordinal.Equals(publication.TenantId, design.CatalogVersion.TenantId) ||
            !StringComparer.Ordinal.Equals(publication.TenantId, design.Layout.TenantId) ||
            design.DirectDependencies.Any(x => !StringComparer.Ordinal.Equals(x.TenantId, publication.TenantId)))
            throw new ArgumentException("Publication Design document tenants do not align.", nameof(commit));
    }

    private async Task<DocumentEnvelope> RequiredAsync(string kind, string id, CancellationToken cancellationToken) =>
        await store.LoadAsync(kind, id, cancellationToken) ?? throw Conflict($"Required document '{kind}/{id}' was not found.");

    private async Task<DocumentEnvelope> RequiredAuthoringByDefinitionAsync(string definitionId, CancellationToken cancellationToken)
    {
        var matches = await store.QueryAsync(new DocumentStoreQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            ActivitiesDesignStorageManifest.ByDefinitionIndex,
            definitionId), cancellationToken);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw Conflict($"Authoring state for activity definition '{definitionId}' was not found."),
            _ => throw Conflict($"Multiple authoring states exist for activity definition '{definitionId}'.")
        };
    }

    private async Task EnsureAbsentAsync(string kind, string id, CancellationToken cancellationToken)
    {
        if (await store.LoadAsync(kind, id, cancellationToken) is not null)
            throw Conflict($"Document '{kind}/{id}' already exists.");
    }

    private static TEntity DeserializeDesign<TEntity>(DocumentEnvelope envelope)
        where TEntity : Entity =>
        JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(envelope.ContentJson, DesignJson)?.Entity
        ?? throw Conflict($"Document '{envelope.DocumentKind}/{envelope.Id}' is unreadable.");

    private static SaveDocumentRequest CreateDesignRequest<TEntity>(string kind, string collection, TEntity entity, long expectedVersion)
        where TEntity : Entity
    {
        var request = GroundworkDocumentWriter.ToSaveRequest(kind, collection, ActivitiesDesignStorageManifest.SchemaVersion, entity, DesignJson);
        return new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private SaveDocumentRequest CreateRichDesignRequest(string kind, string collection, ActivityDefinitionVersion entity, long expectedVersion)
    {
        var request = GroundworkDocumentWriter.ToSaveRequest(
            kind,
            collection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            entity,
            GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer));
        return new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private SaveDocumentRequest CreateRuntimeRequest<TDocument>(string kind, string id, TDocument document, long expectedVersion)
    {
        var serialized = runtimeSerializer.Serialize(kind, document);
        return new(kind, id, serialized.SchemaVersion, serialized.ContentJson, expectedVersion);
    }

    private static InvalidOperationException Conflict(string message, Exception? innerException = null) => new(message, innerException);

    private sealed record TemplateDocument(string Collection, string TemplateHash, ExecutableActivityTemplate Template);
    private sealed record SourceReferenceDocument(string Collection, string ArtifactId, WorkflowExecutableSourceReference Reference);
}
