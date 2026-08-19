using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Groundwork.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>
/// Commits an activity publication — design rows, runtime execution material and the publication
/// receipt — as one act.
/// <para>
/// All three lanes stage into a single transaction, so the publication is atomic with no phase order to
/// reason about and no obligation left standing afterwards: there is no window in which the publication
/// is durable but its receipt is not, which is what an interrupted retry used to have to resume. A host
/// that splits these lanes across databases is refused when the transaction opens.
/// </para>
/// </summary>
public sealed class GroundworkActivityPublicationCommand(
    IPayloadSerializer payloadSerializer,
    IActivityDefinitionVersionPublicationStore publications,
    GroundworkActivityDependencyProjection dependencyProjection,
    GroundworkActivityManagementProjectionWriter managementProjectionWriter,
    PublishingGroundworkDocumentSerializer publishingSerializer,
    GroundworkV2ActivityDesignStore designStore,
    GroundworkPublishingStorage publishingStorage,
    GroundworkV2ExecutableActivityTemplateStore templates,
    IWorkflowExecutableSourceReferenceStore sourceReferences,
    TimeProvider timeProvider)
    : ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>
{
    private static readonly JsonSerializerOptions DesignJson = GroundworkActivitiesDesignJson.Options;

    private static readonly string[] DesignKinds =
    [
        ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
        ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind
    ];

    public async Task<ActivityPublicationResult> ExecuteAsync(
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit,
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
        var definitionEnvelope = await RequiredAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            commit.Design.DefinitionId,
            cancellationToken);
        var definition = DeserializeDesign<ActivityDefinition>(definitionEnvelope);
        ValidateExpectedState(commit.Design, draft, authoring);

        await EnsureNewVersionAsync(commit.Design.CatalogVersion, cancellationToken);
        await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, commit.Design.Publication.Id, cancellationToken);
        await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind, commit.Design.Layout.Id, cancellationToken);
        foreach (var edge in commit.Design.DirectDependencies)
            await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind, edge.Id, cancellationToken);
        await EnsureAbsentAsync(
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            GroundworkActivityPublicationReceiptStore.Id(
                commit.Receipt.TenantId,
                commit.Receipt.IdempotencyKey),
            cancellationToken);

        var requests = new List<ActivityDesignSaveRequest>();
        // The lane owns the template's identity and hash-claim invariants, so it decides whether this
        // template still needs creating before anything is staged.
        var createsTemplate = await templates.RequiresCreateAsync(commit.ExecutableTemplate, cancellationToken);
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

        var establishesRecommendation = authoring.HeadVersionId is null && authoring.RecommendedVersionId is null;
        draft.Status = ActivityDefinitionDraftStatus.Published;
        draft.PublishedVersionId = commit.Design.Publication.DefinitionVersionId;
        draft.LastModifiedAt = commit.Design.Publication.PublishedAt;
        authoring.HeadVersionId = commit.Design.Publication.DefinitionVersionId;
        if (establishesRecommendation)
            authoring.RecommendedVersionId = commit.Design.Publication.DefinitionVersionId;
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
        await using var managementProjection = await managementProjectionWriter.PrepareAsync(
            new(
                commit.Design.Publication.PublishedAt,
                [new(definition, authoring)],
                [draft],
                [commit.Design.Publication]),
            cancellationToken);

        using var transaction = publishingStorage.BeginUnitOfWork(
        [
            .. DesignKinds,
            .. RuntimeKinds,
            .. managementProjection.DocumentKinds,
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind
        ]);
        var design = new ActivityDesignUnitOfWork(transaction.Inner, transaction.Units, timeProvider: timeProvider);
        foreach (var request in requests.Concat(managementProjection.Requests))
            design.StageSave(request);
        if (createsTemplate)
            GroundworkV2ExecutableActivityTemplateStore.StageCreate(transaction, commit.ExecutableTemplate);
        GroundworkV2WorkflowExecutableSourceReferenceStore.StageCreate(transaction, commit.SourceReference);
        transaction.StageInsert(
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            GroundworkActivityPublicationReceiptStore.Row(commit.Receipt, publishingSerializer),
            WriteOptions.CreateOnly);

        // One commit for the whole transaction: the design writer owns it, and the runtime and publishing
        // rows staged above ride the same provider transaction.
        await design.CommitAsync(cancellationToken);

        return new(
            commit.Design.DefinitionId,
            commit.Design.Publication.DefinitionVersionId,
            commit.Design.DraftId,
            commit.ExecutableTemplate.TemplateId,
            commit.SourceReference.SourceReferenceId,
            commit.Design.Publication.PublishedAt);
    }

    private static readonly string[] RuntimeKinds =
    [
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind
    ];

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

    private async Task EnsureNewVersionAsync(ActivityDefinitionVersion candidate, CancellationToken cancellationToken)
    {
        await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, candidate.Id, cancellationToken);
        // The version kind keeps no doc-id-sorted route after the by-collection removal; a zero-clause
        // traversal on the by-definition route (declared version order) visits every version, and this
        // duplicate check only needs completeness, not a specific visit order.
        var documents = await ActivityDesignQueryPager.QueryAllAsync(
            designStore,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionVersionsByDefinitionQuery,
            [],
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionOrder,
            cancellationToken);
        var richOptions = GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer);
        foreach (var envelope in documents)
        {
            var document = JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<ActivityDefinitionVersion>>(envelope.ContentJson, richOptions)
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

    private static void ValidateCommit(ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit)
    {
        var design = commit.Design;
        var publication = design.Publication;
        var template = commit.ExecutableTemplate;
        var source = commit.SourceReference;
        var receipt = commit.Receipt;
        var expectedRequestFingerprint = ActivityPublicationRequestFingerprint.Compute(
            receipt.DraftId,
            receipt.ExpectedDraftRevision,
            receipt.ExpectedDefinitionHeadVersionId,
            receipt.RequestedVersion,
            receipt.ReviewToken);
        if (!StringComparer.Ordinal.Equals(design.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(design.CatalogVersion.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(design.CatalogVersion.Id, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(design.Layout.DefinitionVersionId, publication.DefinitionVersionId))
            throw new ArgumentException("Publication definition/version identities do not align.", nameof(commit));
        if (receipt.Status != ActivityPublicationReceiptStatus.Applied ||
            receipt.Outcome is null ||
            string.IsNullOrWhiteSpace(receipt.IdempotencyKey) ||
            !StringComparer.Ordinal.Equals(receipt.RequestFingerprint, expectedRequestFingerprint) ||
            string.IsNullOrWhiteSpace(receipt.ReviewToken) ||
            !StringComparer.Ordinal.Equals(receipt.TenantId, commit.OperationTenantId) ||
            !StringComparer.Ordinal.Equals(receipt.DraftId, design.DraftId) ||
            receipt.ExpectedDraftRevision != design.ExpectedDraftRevision ||
            !StringComparer.Ordinal.Equals(receipt.ExpectedDefinitionHeadVersionId, design.ExpectedDefinitionHeadVersionId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.DefinitionVersionId, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(receipt.RequestedVersion, publication.Version) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.Version, publication.Version) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.DraftId, design.DraftId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.TemplateId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.TemplateHash, template.TemplateHash) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.SourceReferenceId, source.SourceReferenceId) ||
            receipt.Outcome.PublishedAt != publication.PublishedAt ||
            receipt.UpdatedAt != publication.PublishedAt)
            throw new ArgumentException("Publication receipt does not match authoritative publication material.", nameof(commit));
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

    private async Task<ActivityDesignDocument> RequiredAsync(string kind, string id, CancellationToken cancellationToken) =>
        await designStore.LoadAsync(kind, id, cancellationToken) ?? throw Conflict($"Required document '{kind}/{id}' was not found.");

    private async Task<ActivityDesignDocument> RequiredAuthoringByDefinitionAsync(string definitionId, CancellationToken cancellationToken)
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
            1 => matches.Documents[0],
            0 => throw Conflict($"Authoring state for activity definition '{definitionId}' was not found."),
            _ => throw Conflict($"Multiple authoring states exist for activity definition '{definitionId}'.")
        };
    }

    private async Task EnsureAbsentAsync(string kind, string id, CancellationToken cancellationToken)
    {
        if (await designStore.LoadAsync(kind, id, cancellationToken) is not null)
            throw Conflict($"Document '{kind}/{id}' already exists.");
    }

    private static TEntity DeserializeDesign<TEntity>(ActivityDesignDocument document)
        where TEntity : Entity =>
        JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<TEntity>>(document.ContentJson, DesignJson)?.Entity
        ?? throw Conflict($"Document '{document.DocumentKind}/{document.Id}' is unreadable.");

    private static ActivityDesignSaveRequest CreateDesignRequest<TEntity>(string kind, string collection, TEntity entity, long expectedVersion)
        where TEntity : Entity =>
        GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
            kind, collection, ActivitiesDesignStorageManifest.SchemaVersion, entity, DesignJson)
            with { ExpectedVersion = expectedVersion };

    private ActivityDesignSaveRequest CreateRichDesignRequest(string kind, string collection, ActivityDefinitionVersion entity, long expectedVersion) =>
        GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
            kind,
            collection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            entity,
            GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer))
            with { ExpectedVersion = expectedVersion };

    private static InvalidOperationException Conflict(string message, Exception? innerException = null) => new(message, innerException);

}
