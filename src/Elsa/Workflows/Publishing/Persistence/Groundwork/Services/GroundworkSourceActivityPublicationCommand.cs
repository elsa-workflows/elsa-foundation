using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Versioning;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>Atomic Groundwork closure for source-owned catalog versions and Runtime templates.</summary>
public sealed class GroundworkSourceActivityPublicationCommand(
    IDocumentStore store,
    IBoundedDocumentStore boundedStore,
    IPayloadSerializer payloadSerializer,
    IGroundworkRuntimeDocumentSerializer runtimeSerializer,
    GroundworkActivityManagementProjectionWriter managementProjectionWriter,
    GroundworkLaneTargets laneTargets)
    : ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>
{
    public async Task ExecuteAsync(
        SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit,
        CancellationToken cancellationToken = default)
    {
        Validate(commit);
        ActivityPublicationLaneColocation.EnsureColocated(laneTargets, "Source-owned activity publication");
        var requests = new List<SaveDocumentRequest>();
        var definitionEnvelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            commit.Definition.Id,
            cancellationToken);
        var effectiveDefinition = commit.Definition;
        if (definitionEnvelope is null)
            requests.Add(DesignRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                commit.Definition,
                GroundworkActivitiesDesignJson.Options,
                0));
        else
        {
            var existingDefinition = ReadDesign<ActivityDefinition>(definitionEnvelope, GroundworkActivitiesDesignJson.Options);
            if (!StringComparer.Ordinal.Equals(existingDefinition.ActivityTypeKey, commit.Definition.ActivityTypeKey))
                throw Conflict($"Activity definition '{commit.Definition.Id}' is already bound to another source identity.");
            effectiveDefinition = existingDefinition;
        }

        var authoringEnvelope = await FindAuthoringAsync(commit.Definition.Id, cancellationToken);
        var effectiveAuthoring = commit.AuthoringState;
        if (authoringEnvelope is null)
        {
            requests.Add(DesignRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
                commit.AuthoringState,
                GroundworkActivitiesDesignJson.Options,
                0));
        }
        else
        {
            var existing = ReadDesign<ActivityDefinitionAuthoringState>(authoringEnvelope, GroundworkActivitiesDesignJson.Options);
            if (existing.ContentAuthority.Kind != ActivityContentAuthorityKind.ProviderSource ||
                !StringComparer.Ordinal.Equals(existing.ContentAuthority.AuthorityKey, commit.AuthoringState.ContentAuthority.AuthorityKey) ||
                !StringComparer.Ordinal.Equals(existing.ContentAuthority.SourceId, commit.AuthoringState.ContentAuthority.SourceId))
                throw Conflict($"Activity definition '{commit.Definition.Id}' has a different content authority.");
            if (await ShouldAdvanceHead(existing.HeadVersionId, commit.CatalogVersion, cancellationToken))
                existing.HeadVersionId = commit.CatalogVersion.Id;
            existing.LastModifiedAt = commit.Publication.PublishedAt;
            requests.Add(DesignRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
                existing,
                GroundworkActivitiesDesignJson.Options,
                authoringEnvelope.Version));
            effectiveAuthoring = existing;
        }

        var catalogEnvelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            commit.CatalogVersion.Id,
            cancellationToken);
        if (catalogEnvelope is null)
        {
            requests.Add(DesignRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection,
                commit.CatalogVersion,
                GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer),
                0));
        }
        else
        {
            var existingVersion = ReadDesign<ActivityDefinitionVersion>(
                catalogEnvelope,
                GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer));
            if (!StringComparer.Ordinal.Equals(existingVersion.DefinitionId, commit.CatalogVersion.DefinitionId) ||
                !StringComparer.Ordinal.Equals(existingVersion.Version, commit.CatalogVersion.Version) ||
                !StringComparer.Ordinal.Equals(existingVersion.Hash, commit.CatalogVersion.Hash))
                throw Conflict($"Catalog version '{commit.CatalogVersion.Id}' is already bound to different content.");
        }
        await EnsureAbsent(ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, commit.Publication.Id, cancellationToken);
        await EnsureAbsent(ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind, commit.Layout.Id, cancellationToken);
        await EnsureAbsent(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, commit.SourceReference.SourceReferenceId, cancellationToken);
        requests.Add(DesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection,
            commit.Publication,
            GroundworkActivitiesDesignJson.Options,
            0));
        requests.Add(DesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutCollection,
            commit.Layout,
            GroundworkActivitiesDesignJson.Options,
            0));

        var templateRequest = await TemplateRequest(commit.ExecutableTemplate, cancellationToken);
        if (templateRequest is not null)
            requests.Add(templateRequest);
        requests.Add(RuntimeRequest(
            ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
            commit.SourceReference.SourceReferenceId,
            new SourceReferenceDocument(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection,
                commit.SourceReference.ArtifactId,
                commit.SourceReference),
            0));

        await using var managementProjection = await managementProjectionWriter.PrepareAsync(
            new(
                commit.Publication.PublishedAt,
                [new(effectiveDefinition, effectiveAuthoring)],
                [],
                [commit.Publication]),
            cancellationToken);
        await managementProjection.CommitAsync(
            [
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
                ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind
            ],
            requests,
            cancellationToken);
    }

    private async Task<DocumentEnvelope?> FindAuthoringAsync(string definitionId, CancellationToken cancellationToken)
    {
        var matches = await boundedStore.QueryAsync(
            new DocumentQuery(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                "list-by-definition",
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.DefinitionIdField,
                    definitionId))],
                ActivitiesDesignStorageManifest.ByDefinitionDocumentOrder,
                take: 2),
            cancellationToken);
        return matches.Documents.Count switch
        {
            0 => null,
            1 => matches.Documents[0],
            _ => throw Conflict($"Multiple authoring states exist for activity definition '{definitionId}'.")
        };
    }

    private async Task<bool> ShouldAdvanceHead(
        string? currentHeadVersionId,
        ActivityDefinitionVersion candidate,
        CancellationToken cancellationToken)
    {
        if (currentHeadVersionId is null)
            return true;
        var currentEnvelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            currentHeadVersionId,
            cancellationToken);
        if (currentEnvelope is null)
            return true;
        var current = ReadDesign<ActivityDefinitionVersion>(
            currentEnvelope,
            GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer));
        return SemVer.TryParse(candidate.Version, out var candidateVersion) &&
               SemVer.TryParse(current.Version, out var currentVersion) &&
               candidateVersion > currentVersion;
    }

    private async Task<SaveDocumentRequest?> TemplateRequest(ExecutableActivityTemplate template, CancellationToken cancellationToken)
    {
        var existing = await store.LoadAsync(ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind, template.TemplateId, cancellationToken);
        if (existing is not null)
        {
            var persisted = runtimeSerializer.Deserialize<TemplateDocument>(existing).Template;
            var before = JsonNode.Parse(runtimeSerializer.SerializeForComparison(persisted)) as JsonObject;
            var after = JsonNode.Parse(runtimeSerializer.SerializeForComparison(template)) as JsonObject;
            before?.Remove("createdAt");
            after?.Remove("createdAt");
            if (!StringComparer.Ordinal.Equals(persisted.TemplateHash, template.TemplateHash) || !JsonNode.DeepEquals(before, after))
                throw Conflict($"Template id '{template.TemplateId}' is already bound to different behavior.");
            return null;
        }

        return RuntimeRequest(
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
            template.TemplateId,
            new TemplateDocument(ElsaRuntimeStorageManifest.ExecutableActivityTemplateCollection, template.TemplateHash, template),
            0);
    }

    private async Task EnsureAbsent(string kind, string id, CancellationToken cancellationToken)
    {
        if (await store.LoadAsync(kind, id, cancellationToken) is not null)
            throw Conflict($"Document '{kind}/{id}' already exists.");
    }

    private static void Validate(SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit)
    {
        if (commit.AuthoringState.ContentAuthority.Kind != ActivityContentAuthorityKind.ProviderSource ||
            !StringComparer.Ordinal.Equals(commit.Definition.Id, commit.CatalogVersion.DefinitionId) ||
            !StringComparer.Ordinal.Equals(commit.Definition.Id, commit.Publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(commit.CatalogVersion.Id, commit.Publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(commit.Publication.TemplateId, commit.ExecutableTemplate.TemplateId) ||
            !StringComparer.Ordinal.Equals(commit.Publication.TemplateHash, commit.ExecutableTemplate.TemplateHash) ||
            !StringComparer.Ordinal.Equals(commit.Publication.SourceReferenceId, commit.SourceReference.SourceReferenceId) ||
            !StringComparer.Ordinal.Equals(commit.SourceReference.ArtifactId, commit.ExecutableTemplate.TemplateId))
            throw new ArgumentException("Source activity publication identities do not align.", nameof(commit));
    }

    private static T ReadDesign<T>(DocumentEnvelope envelope, JsonSerializerOptions options)
        where T : Entity =>
        JsonSerializer.Deserialize<GroundworkDocument<T>>(envelope.ContentJson, options)?.Entity
        ?? throw Conflict($"Document '{envelope.DocumentKind}/{envelope.Id}' is unreadable.");

    private static SaveDocumentRequest DesignRequest<T>(
        string kind,
        string collection,
        T entity,
        JsonSerializerOptions options,
        long expectedVersion)
        where T : Entity
    {
        var request = GroundworkDocumentWriter.ToSaveRequest(
            kind,
            collection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            entity,
            options);
        return new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private SaveDocumentRequest RuntimeRequest<T>(string kind, string id, T document, long expectedVersion)
    {
        var serialized = runtimeSerializer.Serialize(kind, document);
        return new(kind, id, serialized.SchemaVersion, serialized.ContentJson, expectedVersion);
    }

    private static InvalidOperationException Conflict(string message) => new(message);

    private sealed record TemplateDocument(string Collection, string TemplateHash, ExecutableActivityTemplate Template);
    private sealed record SourceReferenceDocument(string Collection, string ArtifactId, WorkflowExecutableSourceReference Reference);
}
