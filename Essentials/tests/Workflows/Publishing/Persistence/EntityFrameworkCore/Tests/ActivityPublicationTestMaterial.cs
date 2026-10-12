using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using ActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Internally consistent reusable-activity publication material: a seeded Design-owned draft and the commit
/// that publishes it, shaped the way the publisher builds it, so the commit passes the command's own
/// authoritative-material checks.
/// </summary>
internal static class ActivityPublicationTestMaterial
{
    internal static readonly DateTimeOffset Seeded = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset Published = new(2026, 7, 15, 12, 5, 0, TimeSpan.Zero);

    /// <summary>Seeds a Design-owned definition, its authoring state and an active revision-4 draft.</summary>
    internal static async Task SeedDraftAsync(ActivitiesDesignDbContext context, string definitionId, string draftId, string? tenantId = null)
    {
        var access = TestAccess.Scoped(tenantId ?? "default");
        var stores = new EfActivityDesignStores(context, access, null, new EfActivityManagementProjectionWriter(context, access));
        await stores.ExecuteAsync(new CreateActivityDefinitionRequest(
            new ActivityDefinition { Id = definitionId, TenantId = tenantId, ActivityTypeKey = $"test.{definitionId}", Category = "Tests", DisplayName = "Test activity", CreatedAt = Seeded, LastModifiedAt = Seeded },
            // Deliberately not keyed on the definition id: the design phase must resolve authoring by definition.
            new ActivityDefinitionAuthoringState { Id = $"authoring-{definitionId}", TenantId = tenantId, DefinitionId = definitionId, ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa.design"), CreatedAt = Seeded, LastModifiedAt = Seeded },
            new ActivityDefinitionDraft { Id = draftId, TenantId = tenantId, DefinitionId = definitionId, Revision = 4, State = new(Contract(), Provider(), new Dictionary<string, string>()), CreatedAt = Seeded, LastModifiedAt = Seeded },
            new ActivityDefinitionDraftLayout { Id = $"layout-{draftId}", TenantId = tenantId, DraftId = draftId, Revision = 4, Records = [], CreatedAt = Seeded, LastModifiedAt = Seeded }));
    }

    internal sealed record DependencySpec(string DefinitionId, string VersionId, string Version, string TemplateId, string TemplateHash, string OccurrenceId);

    internal static ActivityContract Contract() => new("1", [], [], []);
    internal static ActivityProviderManifest Provider() => new("test.provider", "1", Json("{}"));
    internal static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    internal static ExecutableNode Root() => new(
        "boundary",
        "boundary",
        "test.consumer",
        "1",
        new("test.consumer", "1", Json("{\"plan\":1}")),
        new Dictionary<string, RuntimeInputBinding>(),
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>());

    internal static ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> Commit(
        string definitionId = "definition-1",
        string draftId = "draft-1",
        string versionId = "version-1",
        char hashCharacter = 'a',
        string idempotencyKey = "publish-operation-1",
        string sourceReferenceId = "source-ref-1",
        long draftRevision = 4,
        string? operationTenant = null,
        DependencySpec? dependency = null,
        string? resourceTenant = null)
    {
        const string version = "1.0.0";
        var hash = "sha256:" + new string(hashCharacter, 64);
        var templateId = "activity-template-" + new string(hashCharacter, 64);
        var root = Root();
        var template = new ExecutableActivityTemplate(
            templateId,
            hash,
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            dependency is null
                ? []
                : [new ExecutableActivityTemplateDependency(
                    dependency.DefinitionId,
                    dependency.VersionId,
                    dependency.Version,
                    dependency.TemplateId,
                    dependency.TemplateHash,
                    dependency.OccurrenceId,
                    new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.AuthoredNode, dependency.OccurrenceId)]))],
            dependency is null ? [] : [new ExecutableActivityTemplateIdentity(dependency.TemplateId, dependency.TemplateHash)],
            [new RuntimeRequirement("test.consumer", "1")],
            "provider-fingerprint",
            new Dictionary<string, string>(),
            Published);
        var source = new WorkflowExecutableSourceReference(
            sourceReferenceId,
            templateId,
            "ActivityDefinitionVersion",
            versionId,
            version,
            definitionId,
            versionId,
            version,
            Published,
            Published,
            WorkflowExecutableReferenceScope.Published);
        var catalog = new ActivityDefinitionVersion(version, definitionId)
        {
            Id = versionId,
            TenantId = resourceTenant,
            ProviderKey = "test.provider",
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = "test.consumer",
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = root.Descriptor.Payload,
            SourceKind = "ActivityDefinitionDraft",
            SourceId = draftId,
            Hash = hash,
            CreatedAt = Published,
            LastModifiedAt = Published
        };
        var publication = new ActivityDefinitionVersionPublication
        {
            Id = versionId,
            TenantId = resourceTenant,
            DefinitionVersionId = versionId,
            DefinitionId = definitionId,
            Version = version,
            ActivityTypeKey = $"test.{definitionId}",
            ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
            SourceDraftId = draftId,
            Contract = Contract(),
            Provider = Provider(),
            TemplateId = templateId,
            TemplateHash = hash,
            SourceReferenceId = sourceReferenceId,
            ProviderFingerprint = "provider-fingerprint",
            DirectDependencyCount = dependency is null ? 0 : 1,
            ClosedTemplateCount = dependency is null ? 0 : 1,
            RuntimeRequirements = [new("test.consumer", "1")],
            ResourceMeasurements = new(1, 1, 0, 1, 10, 20, 0),
            ResumeTargetCount = 0,
            PublishedAt = Published,
            CreatedAt = Published,
            LastModifiedAt = Published
        };
        var layout = new ActivityDefinitionVersionLayout
        {
            Id = versionId,
            TenantId = resourceTenant,
            DefinitionVersionId = versionId,
            Records = [new("boundary", Json("{\"x\":10,\"y\":20}"))],
            CreatedAt = Published,
            LastModifiedAt = Published
        };
        IReadOnlyList<ActivityDependencyEdge> edges = dependency is null
            ? []
            : [new ActivityDependencyEdge
            {
                Id = $"edge-{versionId}",
                TenantId = resourceTenant,
                OwnerVersionId = versionId,
                OwnerTemplateHash = hash,
                DependencyVersionId = dependency.VersionId,
                DependencyTemplateHash = dependency.TemplateHash,
                OccurrenceId = dependency.OccurrenceId,
                NodeOrigin = [new ActivityNodeOrigin("AuthoredNode", dependency.OccurrenceId)],
                CreatedAt = Published,
                LastModifiedAt = Published
            }];
        var receipt = new ActivityPublicationReceipt(
            operationTenant,
            idempotencyKey,
            ActivityPublicationRequestFingerprint.Compute(draftId, draftRevision, null, version, "sha256:review"),
            ActivityPublicationReceiptStatus.Applied,
            draftId,
            draftRevision,
            null,
            "sha256:review",
            version,
            new(definitionId, versionId, draftId, version, templateId, hash, sourceReferenceId, Published),
            null,
            [],
            Published);
        return new(
            operationTenant,
            new(draftId, draftRevision, definitionId, null, catalog, publication, layout, edges),
            template,
            source,
            receipt);
    }

    internal static SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> SourceCommit(DateTimeOffset now)
    {
        const string definitionId = "source-definition-1";
        const string versionId = "source-version-1";
        var hash = "sha256:" + new string('e', 64);
        var templateId = "activity-template-" + new string('e', 64);
        var root = Root();
        var template = new ExecutableActivityTemplate(
            templateId, hash, root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [],
            [new RuntimeRequirement("test.consumer", "1")], "provider-fingerprint", new Dictionary<string, string>(), now);
        return new(
            new ActivityDefinition { Id = definitionId, ActivityTypeKey = "test.source", Category = "Tests", CreatedAt = now, LastModifiedAt = now },
            new ActivityDefinitionAuthoringState
            {
                Id = "source-authority-1",
                DefinitionId = definitionId,
                ContentAuthority = new(ActivityContentAuthorityKind.ProviderSource, "test.provider", "source-1"),
                HeadVersionId = versionId,
                RecommendedVersionId = versionId,
                CreatedAt = now,
                LastModifiedAt = now
            },
            new ActivityDefinitionVersion("1.0.0", definitionId)
            {
                Id = versionId,
                ProviderKey = "test.provider",
                ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
                ConsumerKey = "test.consumer",
                ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
                DescriptorPayload = root.Descriptor.Payload,
                SourceKind = "test.source",
                SourceId = "source-1",
                Hash = hash,
                CreatedAt = now,
                LastModifiedAt = now
            },
            new ActivityDefinitionVersionPublication
            {
                Id = versionId,
                DefinitionVersionId = versionId,
                DefinitionId = definitionId,
                Version = "1.0.0",
                ActivityTypeKey = "test.source",
                ResolutionKind = ActivityDefinitionVersionResolutionKind.AuthorableActivity,
                Contract = Contract(),
                Provider = Provider(),
                TemplateId = templateId,
                TemplateHash = hash,
                SourceReferenceId = "source-owned-ref-1",
                ProviderFingerprint = "provider-fingerprint",
                DirectDependencyCount = 0,
                ClosedTemplateCount = 0,
                RuntimeRequirements = [new("test.consumer", "1")],
                ResumeTargetCount = 0,
                PublishedAt = now,
                CreatedAt = now,
                LastModifiedAt = now
            },
            new ActivityDefinitionVersionLayout { Id = versionId, DefinitionVersionId = versionId, Records = [], CreatedAt = now, LastModifiedAt = now },
            template,
            new WorkflowExecutableSourceReference(
                "source-owned-ref-1", templateId, "test.source", "source-1", "1.0.0", definitionId, versionId, "1.0.0",
                now, now, WorkflowExecutableReferenceScope.Published));
    }
}
