using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>
/// Commits a source-owned catalog version and its runtime execution material as one act.
/// <para>
/// Design rows and runtime rows are staged into a single transaction spanning both lanes, so the
/// publication is atomic without a phase order to reason about: there is no window in which a template
/// exists without its catalog version, or the reverse. A host that splits the lanes across databases is
/// refused when the transaction opens.
/// </para>
/// </summary>
public sealed class GroundworkSourceActivityPublicationCommand(
    IPayloadSerializer payloadSerializer,
    GroundworkV2ActivityDesignStore designStore,
    GroundworkPublishingStorage publishingStorage,
    GroundworkV2ExecutableActivityTemplateStore templates,
    IWorkflowExecutableSourceReferenceStore sourceReferences,
    GroundworkActivityManagementProjectionWriter managementProjectionWriter,
    TimeProvider? timeProvider = null)
    : ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>
{
    private static readonly string[] DesignKinds =
    [
        ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind
    ];

    private static readonly string[] RuntimeKinds =
    [
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind
    ];

    public async Task ExecuteAsync(
        SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit,
        CancellationToken cancellationToken = default)
    {
        Validate(commit);
        var requests = new List<ActivityDesignSaveRequest>();

        var definitionDocument = await designStore.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            commit.Definition.Id,
            cancellationToken);
        var effectiveDefinition = commit.Definition;
        if (definitionDocument is null)
        {
            requests.Add(DesignRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                commit.Definition,
                GroundworkActivitiesDesignJson.Options,
                0));
        }
        else
        {
            var existingDefinition = ReadDesign<ActivityDefinition>(definitionDocument, GroundworkActivitiesDesignJson.Options);
            if (!StringComparer.Ordinal.Equals(existingDefinition.ActivityTypeKey, commit.Definition.ActivityTypeKey))
                throw Conflict($"Activity definition '{commit.Definition.Id}' is already bound to another source identity.");
            effectiveDefinition = existingDefinition;
        }

        var authoringDocument = await FindAuthoringAsync(commit.Definition.Id, cancellationToken);
        var effectiveAuthoring = commit.AuthoringState;
        if (authoringDocument is null)
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
            var existing = ReadDesign<ActivityDefinitionAuthoringState>(authoringDocument, GroundworkActivitiesDesignJson.Options);
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
                authoringDocument.Version));
            effectiveAuthoring = existing;
        }

        var catalogDocument = await designStore.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            commit.CatalogVersion.Id,
            cancellationToken);
        if (catalogDocument is null)
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
                catalogDocument,
                GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer));
            if (!StringComparer.Ordinal.Equals(existingVersion.DefinitionId, commit.CatalogVersion.DefinitionId) ||
                !StringComparer.Ordinal.Equals(existingVersion.Version, commit.CatalogVersion.Version) ||
                !StringComparer.Ordinal.Equals(existingVersion.Hash, commit.CatalogVersion.Hash))
                throw Conflict($"Catalog version '{commit.CatalogVersion.Id}' is already bound to different content.");
        }

        await EnsureDesignAbsent(ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, commit.Publication.Id, cancellationToken);
        await EnsureDesignAbsent(ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind, commit.Layout.Id, cancellationToken);
        if (await sourceReferences.FindAsync(commit.SourceReference.SourceReferenceId, cancellationToken) is not null)
            throw Conflict($"Workflow executable source reference '{commit.SourceReference.SourceReferenceId}' already exists.");

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

        // The lane owns the template's identity and hash-claim invariants, so it decides whether this
        // template still needs creating before anything is staged.
        var createsTemplate = await templates.RequiresCreateAsync(commit.ExecutableTemplate, cancellationToken);

        await using var managementProjection = await managementProjectionWriter.PrepareAsync(
            new(
                commit.Publication.PublishedAt,
                [new(effectiveDefinition, effectiveAuthoring)],
                [],
                [commit.Publication]),
            cancellationToken);

        using var transaction = publishingStorage.BeginUnitOfWork(
            [.. DesignKinds, .. RuntimeKinds, .. managementProjection.DocumentKinds]);
        var design = new ActivityDesignUnitOfWork(
            transaction.Inner,
            transaction.Units,
            timeProvider: timeProvider);
        foreach (var request in requests.Concat(managementProjection.Requests))
            design.StageSave(request);
        if (createsTemplate)
            GroundworkV2ExecutableActivityTemplateStore.StageCreate(transaction, commit.ExecutableTemplate);
        GroundworkV2WorkflowExecutableSourceReferenceStore.StageCreate(transaction, commit.SourceReference);

        // One commit for the whole transaction: the design writer owns it, and the runtime rows staged
        // above ride the same provider transaction.
        await design.CommitAsync(cancellationToken);
    }

    private async Task<ActivityDesignDocument?> FindAuthoringAsync(string definitionId, CancellationToken cancellationToken)
    {
        var matches = await designStore.QueryAsync(
            new ActivityDesignQuery(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                "list-by-definition",
                [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.DefinitionIdField,
                    definitionId))],
                ActivitiesDesignStorageManifest.ByDefinitionDocumentOrder,
                Take: 2),
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
        var currentDocument = await designStore.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            currentHeadVersionId,
            cancellationToken);
        if (currentDocument is null)
            return true;
        var current = ReadDesign<ActivityDefinitionVersion>(
            currentDocument,
            GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer));
        return SemVer.TryParse(candidate.Version, out var candidateVersion) &&
               SemVer.TryParse(current.Version, out var currentVersion) &&
               candidateVersion > currentVersion;
    }

    private async Task EnsureDesignAbsent(string kind, string id, CancellationToken cancellationToken)
    {
        if (await designStore.LoadAsync(kind, id, cancellationToken) is not null)
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

    private static T ReadDesign<T>(ActivityDesignDocument document, JsonSerializerOptions options)
        where T : Entity =>
        JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<T>>(document.ContentJson, options)?.Entity
        ?? throw Conflict($"Document '{document.DocumentKind}/{document.Id}' is unreadable.");

    private static ActivityDesignSaveRequest DesignRequest<T>(
        string kind,
        string collection,
        T entity,
        JsonSerializerOptions options,
        long expectedVersion)
        where T : Entity =>
        GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
            kind,
            collection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            entity,
            options) with { ExpectedVersion = expectedVersion };

    private static InvalidOperationException Conflict(string message) => new(message);
}
