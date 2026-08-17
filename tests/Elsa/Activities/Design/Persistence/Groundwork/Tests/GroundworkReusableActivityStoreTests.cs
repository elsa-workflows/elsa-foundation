using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkReusableActivityStoreTests
{
    [Theory]
    [InlineData(null, ActivityDefinitionVersionResolutionKind.AuthorableActivity)]
    [InlineData("draft-legacy", ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary)]
    public void Legacy_publication_without_resolution_kind_uses_existing_draft_provenance(
        string? sourceDraftId,
        ActivityDefinitionVersionResolutionKind expected)
    {
        var json = JsonSerializer.SerializeToNode(Publication("publication-legacy", "version-legacy", "definition-legacy", "1.0.0"), GroundworkActivitiesDesignJson.Options)!.AsObject();
        json.Remove("resolutionKind");
        if (sourceDraftId is null)
            json.Remove("sourceDraftId");
        else
            json["sourceDraftId"] = sourceDraftId;
        var publication = json.Deserialize<ActivityDefinitionVersionPublication>(GroundworkActivitiesDesignJson.Options)!;
        Assert.Equal(ActivityDefinitionVersionResolutionKind.Unspecified, publication.ResolutionKind);
        Assert.Equal(expected, publication.ResolveWorkflowResolutionKind());
    }

    [Fact]
    public async Task CreateDefinition_Commits_Definition_Authoring_Draft_And_Layout()
    {
        using var harness = await SeededAsync();
        Assert.Equal("definition-1", (await harness.Stores.FindAsync("definition-1"))!.DefinitionId);
        Assert.Equal("draft-1", (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!.Id);
        Assert.Equal("node-1", Assert.Single((await harness.Stores.FindDraftLayoutAsync("draft-1"))!.Records).NodeId);
    }

    [Fact]
    public async Task Authoring_state_reads_are_scoped_and_deterministic()
    {
        using var harness = await SeededAsync();
        var states = await harness.Stores.ListAsync(["definition-1", "missing", "definition-1"]);
        Assert.Single(states);
        Assert.Equal("definition-1", states[0].DefinitionId);
    }

    [Fact]
    public async Task Draft_reads_round_trip_contract_provider_and_revision()
    {
        using var harness = await SeededAsync();
        var draft = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1");
        Assert.NotNull(draft);
        Assert.Equal(0, draft!.Revision);
        Assert.Equal("workflow", draft.State.Provider.ProviderKey);
    }

    [Fact]
    public async Task Draft_lists_are_ordered_by_identity()
    {
        using var harness = await SeededAsync();
        var drafts = await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync("definition-1");
        Assert.Equal(["draft-1"], drafts.Select(draft => draft.Id));
    }

    [Fact]
    public async Task Draft_layout_reads_round_trip_records()
    {
        using var harness = await SeededAsync();
        var layout = await harness.Stores.FindDraftLayoutAsync("draft-1");
        Assert.Equal("node-1", Assert.Single(layout!.Records).NodeId);
    }

    [Fact]
    public async Task Definition_authority_round_trips_without_navigation_artifacts()
    {
        using var harness = await SeededAsync();
        var authoring = await harness.Stores.FindAsync("definition-1");
        Assert.Equal(ActivityContentAuthorityKind.Design, authoring!.ContentAuthority.Kind);
        Assert.Equal("authoring-1", authoring.Id);
    }

    [Fact]
    public async Task Definition_and_draft_rows_share_the_explicit_tenant_scope()
    {
        using var harness = await SeededAsync();
        Assert.Equal("tenant-a", (await harness.Stores.FindAsync("definition-1"))!.TenantId);
        Assert.Equal("tenant-a", (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!.TenantId);
    }

    [Fact]
    public async Task Definition_presentation_update_preserves_immutable_identity_and_authority()
    {
        using var harness = await SeededAsync();
        var updated = await harness.Stores.ExecuteAsync(new UpdateActivityDefinitionPresentationRequest(
            "definition-1", "tenant-a", "Finance", "Calculate invoice total", "Updated description",
            new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero)));

        var persisted = await new GroundworkActivityDefinitionStore(harness.Persistence.Store).GetAsync("definition-1");
        var authoring = await harness.Stores.FindAsync("definition-1");
        Assert.Equal("Finance", persisted.Category);
        Assert.Equal("Calculate invoice total", persisted.DisplayName);
        Assert.Equal("Updated description", persisted.Description);
        Assert.Equal("Acme.Sample", persisted.ActivityTypeKey);
        Assert.Equal(ActivityContentAuthorityKind.Design, authoring!.ContentAuthority.Kind);
        Assert.Equal("Finance", updated.Category);
    }

    [Fact]
    public async Task Authoring_reads_preserve_fork_metadata_shape()
    {
        using var harness = await SeededAsync();
        var authoring = await harness.Stores.FindAsync("definition-1");
        Assert.Null(authoring!.ForkedFrom);
        Assert.Equal("design", authoring.ContentAuthority.AuthorityKey);
    }

    [Fact]
    public async Task Draft_state_preserves_authored_label_projection()
    {
        using var harness = await SeededAsync();
        var draft = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1");
        Assert.Equal("initial", draft!.State.Options["label"]);
    }

    [Fact]
    public async Task Layout_rows_preserve_json_node_payloads()
    {
        using var harness = await SeededAsync();
        var record = Assert.Single((await harness.Stores.FindDraftLayoutAsync("draft-1"))!.Records);
        Assert.Equal(JsonValueKind.Object, record.Data.ValueKind);
    }

    [Fact]
    public async Task Scoped_store_does_not_return_rows_from_another_tenant()
    {
        using var harness = await SeededAsync();
        harness.PersistenceAccess.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        Assert.Null(await harness.Stores.FindAsync("definition-1"));
    }

    [Fact]
    public async Task Fork_candidate_apply_is_idempotent_and_commits_reserved_identity()
    {
        using var harness = Harness.Create();
        var candidate = ForkCandidate();
        await SaveForkSourceAsync(harness, candidate);
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(candidate));

        var request = ForkApply(candidate, "operation-1");
        var applied = await harness.Stores.ExecuteAsync(request);
        var replay = await harness.Stores.ExecuteAsync(request);

        Assert.False(applied.AlreadyApplied);
        Assert.True(replay.AlreadyApplied);
        Assert.Equal(candidate.ReservedDefinition.Id, applied.Receipt.DefinitionId);
        Assert.Equal(candidate.ReservedDraft.Id, applied.Receipt.DraftId);
        Assert.NotNull(await harness.Stores.FindReceiptAsync(request.ReceiptId));
        Assert.Equal(ActivityForkCandidateStatus.Applied, (await harness.Stores.FindCandidateAsync(candidate.Id))!.Status);
    }

    [Fact]
    public async Task Fork_candidate_collision_rejects_without_reserved_identity_writes()
    {
        using var harness = Harness.Create();
        var candidate = ForkCandidate();
        await SaveForkSourceAsync(harness, candidate);
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(candidate));
        await harness.Stores.ExecuteAsync(CreateRequest(tenantId: null));

        await Assert.ThrowsAsync<ActivityForkCollisionException>(() =>
            harness.Stores.ExecuteAsync(ForkApply(candidate, "operation-1")));

        Assert.Null(await harness.Stores.FindReceiptAsync(ActivityForkReceiptIdentity.Compute(null, "actor-a", "operation-1")));
        Assert.Equal(ActivityForkCandidateStatus.Reserved, (await harness.Stores.FindCandidateAsync(candidate.Id))!.Status);
        Assert.Null(await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(candidate.ReservedDraft.Id));
    }

    [Fact]
    public async Task Fork_preview_replay_returns_the_first_reservation_and_changed_material_conflicts()
    {
        using var harness = Harness.Create();
        var first = ForkCandidate();
        var retry = ForkCandidate(definitionId: "losing-definition", draftId: "losing-draft");
        var changed = ForkCandidate(
            definitionId: "changed-definition",
            draftId: "changed-draft",
            requestFingerprint: $"sha256:{new string('f', 64)}");

        var reserved = await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(first));
        var replay = await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(retry));
        await Assert.ThrowsAsync<ActivityForkPreviewIdempotencyConflictException>(() =>
            harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(changed)));

        Assert.Equal(first.ReservedDefinition.Id, reserved.ReservedDefinition.Id);
        Assert.Equal(first.ReservedDefinition.Id, replay.ReservedDefinition.Id);
        Assert.Single(harness.Persistence.Rows(ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind));
    }

    [Fact]
    public async Task Fork_candidate_retention_prunes_candidates_but_preserves_receipts()
    {
        using var harness = Harness.Create();
        var candidate = ForkCandidate();
        await SaveForkSourceAsync(harness, candidate);
        await harness.Stores.ExecuteAsync(new SaveActivityForkCandidateRequest(candidate));
        var request = ForkApply(candidate, "operation-1");
        await harness.Stores.ExecuteAsync(request);

        var pruned = await harness.Stores.ExecuteAsync(candidate.RetainUntil, 1);

        Assert.Equal(1, pruned);
        Assert.Null(await harness.Stores.FindCandidateAsync(candidate.Id));
        Assert.NotNull(await harness.Stores.FindReceiptAsync(request.ReceiptId));
    }

    [Fact]
    public async Task Replace_draft_uses_revision_cas_and_updates_layout_together()
    {
        using var harness = await SeededAsync();
        await harness.Stores.ExecuteAsync(new ReplaceActivityDraftRequest(
            "draft-1", 0, State("updated"), [LayoutRecord("node-2")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(
            new ReplaceActivityDraftRequest("draft-1", 0, State("stale"), [LayoutRecord("stale-node")] )));

        var draft = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1");
        var layout = await harness.Stores.FindDraftLayoutAsync("draft-1");
        Assert.Equal(1, draft!.Revision);
        Assert.Equal("updated", draft.State.Options["label"]);
        Assert.Equal(1, layout!.Revision);
        Assert.Equal("node-2", Assert.Single(layout.Records).NodeId);
    }

    [Fact]
    public async Task Contract_proposal_requires_exact_provider_fingerprint_and_preserves_layout()
    {
        using var harness = await SeededAsync();
        var before = (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!;
        var fingerprint = ActivityProviderManifestFingerprint.Compute(before.State.Provider);
        var contract = new ActivityContract(
            "proposal",
            [new("note", "Note", new("String", Elsa.Primitives.Models.CollectionKind.Single), true, true, null, "elsa.json")],
            [],
            []);

        var applied = await harness.Stores.ExecuteAsync(new ApplyActivityContractProposalRequest(
            before.Id, before.TenantId, before.Revision, before.State.Provider.ProviderKey,
            before.State.Provider.SchemaVersion, fingerprint, contract));

        Assert.Equal(1, applied.Revision);
        Assert.Equal("proposal", applied.State.Contract.ContractSchemaVersion);
        Assert.Equal("node-1", Assert.Single((await harness.Stores.FindDraftLayoutAsync(before.Id))!.Records).NodeId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(
            new ApplyActivityContractProposalRequest(
                before.Id, before.TenantId, applied.Revision, before.State.Provider.ProviderKey,
                before.State.Provider.SchemaVersion, "sha256:stale", contract)));
    }

    [Fact]
    public async Task Publication_layout_edge_and_dependency_reads_round_trip()
    {
        using var harness = Harness.Create();
        await harness.SaveAsync(new ActivityDefinition { Id = "definition-parent", ActivityTypeKey = "Acme.Parent", Category = "Samples" });
        await harness.SaveAsync(new ActivityDefinitionAuthoringState
        {
            Id = "authoring-parent", DefinitionId = "definition-parent",
            ContentAuthority = new(ActivityContentAuthorityKind.Design, "design"), HeadVersionId = "version-parent"
        });
        await harness.SaveAsync(Publication("publication-parent", "version-parent", "definition-parent", "1.0.0"));
        await harness.SaveAsync(Publication("publication-child", "version-child", "definition-child", "2.0.0"));
        await harness.SaveAsync(new ActivityDefinitionVersionLayout
        {
            Id = "version-layout-parent", DefinitionVersionId = "version-parent", Records = [LayoutRecord("node-parent")]
        });
        await harness.SaveAsync(new ActivityDependencyEdge
        {
            Id = "edge-1", OwnerVersionId = "version-parent", OwnerTemplateHash = "hash-version-parent",
            DependencyVersionId = "version-child", DependencyTemplateHash = "hash-version-child", OccurrenceId = "occurrence-1",
            NodeOrigin = [new ActivityNodeOrigin("Node", "child-node")]
        });

        var publication = await ((IActivityDefinitionVersionPublicationStore)harness.Stores).FindAsync("version-parent");
        var layout = await harness.Stores.FindVersionLayoutAsync("version-parent");
        var edges = await harness.Stores.ListOutboundAsync("version-parent");
        var page = await harness.Stores.ReadAsync(new(
            "version-parent", new(ActivityDependencyDirection.Outbound, false, new HashSet<string> { "Versions" }),
            null, null, 0, 10));

        Assert.Equal("version-parent", publication!.DefinitionVersionId);
        Assert.Equal("node-parent", Assert.Single(layout!.Records).NodeId);
        Assert.Equal("edge-1", Assert.Single(edges).Id);
        Assert.Equal("version-child", Assert.Single(page.Items).Dependency.VersionId);
    }

    [Fact]
    public async Task Dependency_projection_rebuild_exposes_a_bound_watermark_and_expires_old_cursors()
    {
        using var harness = Harness.Create();
        await harness.SaveAsync(Publication("publication-child", "version-child", "definition-child", "2.0.0"));
        var projection = new GroundworkActivityDependencyProjection(harness.Persistence.Store, harness.Stores);
        var child = new ActivityDefinitionReference("ActivityVersion", "definition-child", "version-child", "2.0.0", TemplateHash: "hash-version-child");
        var items = new[]
        {
            DependencyItem("activity-draft-use", new("ActivityDraft", "definition-activity-owner", DraftId: "draft-a", Revision: 4), child, "node-a"),
            DependencyItem("workflow-version-use", new("WorkflowVersion", "definition-workflow-owner", "workflow-v1", "1.0.0"), child, "node-w")
        };
        var asOf = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        await projection.RebuildAsync(new("rebuild-1", 17, asOf, items));
        var first = await projection.ReadAsync(new(
            "version-child", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions", "Drafts"])),
            null, null, 0, 1));
        var second = await projection.ReadAsync(new(
            "version-child", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions", "Drafts"])),
            null, first.Watermark, first.NextOffset!.Value, 1));

        Assert.Equal(17, first.Consistency.AsOfSequence);
        Assert.Equal(["ActivityDraft", "WorkflowVersion"], first.Items.Concat(second.Items).Select(x => x.Owner.Kind));
        await projection.RebuildAsync(new("rebuild-2", 18, asOf.AddMinutes(1), items));
        await Assert.ThrowsAsync<ActivityDependencyWatermarkExpiredException>(() => projection.ReadAsync(new(
            "version-child", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions"])),
            null, first.Watermark, 0, 10)));
    }

    private static async Task<TestHarness> SeededAsync()
    {
        var persistence = ActivityDesignV2TestHarness.Create();
        var stores = CreateStores(persistence);
        await stores.ExecuteAsync(CreateRequest());
        return new TestHarness(persistence, stores);
    }

    private static GroundworkReusableActivityStores CreateStores(ActivityDesignV2TestHarness persistence)
    {
        var locks = new ImmediateDistributedLockProvider();
        return new(
            persistence.Store,
            new FixedClock(),
            locks,
            persistence.Store,
            new GroundworkActivityManagementProjectionWriter(persistence.Store, locks, persistence.Store));
    }

    private static CreateActivityDefinitionRequest CreateRequest(string? tenantId = "tenant-a")
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new(
            new ActivityDefinition
            {
                Id = "definition-1", TenantId = tenantId, ActivityTypeKey = "Acme.Sample", Category = "Samples",
                CreatedAt = now, LastModifiedAt = now
            },
            new ActivityDefinitionAuthoringState
            {
                Id = "authoring-1", TenantId = tenantId, DefinitionId = "definition-1",
                ContentAuthority = new(ActivityContentAuthorityKind.Design, "design"), CreatedAt = now, LastModifiedAt = now
            },
            new ActivityDefinitionDraft
            {
                Id = "draft-1", TenantId = tenantId, DefinitionId = "definition-1", Revision = 0,
                State = new(new("1", [], [], []), new("workflow", "1", JsonElement.Parse("{}")), new Dictionary<string, string> { ["label"] = "initial" }),
                CreatedAt = now, LastModifiedAt = now
            },
            new ActivityDefinitionDraftLayout
            {
                Id = "draft-layout-1", TenantId = tenantId, DraftId = "draft-1", Revision = 0,
                Records = [new("node-1", JsonElement.Parse("{}"))], CreatedAt = now, LastModifiedAt = now
            });
    }

    private static ActivityDefinitionVersionPublication Publication(
        string id,
        string versionId,
        string definitionId,
        string version,
        int directDependencies = 0) => new()
    {
        Id = id, DefinitionVersionId = versionId, DefinitionId = definitionId, Version = version,
        ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary, SourceDraftId = "draft-legacy",
        Contract = Contract(), Provider = Provider(), TemplateId = $"template-{versionId}", TemplateHash = $"hash-{versionId}",
        SourceReferenceId = $"source-{versionId}", ProviderFingerprint = "provider-fingerprint",
        DirectDependencyCount = directDependencies, ClosedTemplateCount = directDependencies + 1,
        RuntimeRequirements = [new ActivityRuntimeRequirementDeclaration("runtime.graph", "1")],
        PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

    private static ActivityForkCandidate ForkCandidate(
        string candidateId = "candidate-1",
        string definitionId = "target-definition-1",
        string draftId = "target-draft-1",
        string? requestFingerprint = null,
        string activityTypeKey = "Acme.Sample")
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var definition = new ActivityDefinition
        {
            Id = definitionId, ActivityTypeKey = activityTypeKey, Category = "Samples", DisplayName = "Forked",
            CreatedAt = createdAt, LastModifiedAt = createdAt
        };
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = $"authoring-{definitionId}", DefinitionId = definitionId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
            ForkedFrom = new("source-definition", "source-version", "1.0.0"), CreatedAt = createdAt, LastModifiedAt = createdAt
        };
        var draft = new ActivityDefinitionDraft
        {
            Id = draftId, DefinitionId = definitionId, Revision = 1, SourceVersionId = "source-version",
            State = State("initial"), CreatedAt = createdAt, LastModifiedAt = createdAt
        };
        var layout = new ActivityDefinitionDraftLayout
        {
            Id = $"layout-{draftId}", DraftId = draftId, Revision = 1, Records = [LayoutRecord("node-1")],
            CreatedAt = createdAt, LastModifiedAt = createdAt
        };
        var sourceContract = Contract();
        var sourceProvider = Provider();
        return new()
        {
            Id = candidateId, CandidateId = $"public-{candidateId}", PreviewIdempotencyKey = $"preview-{candidateId}",
            RequestFingerprint = requestFingerprint ?? $"sha256:{new string('a', 64)}",
            AccessBindingFingerprint = $"sha256:{new string('b', 64)}", ActorId = "actor-a", AuthorizationProfile = "profile-a",
            SourceDefinitionId = "source-definition", SourceVersionId = "source-version", SourceVersion = "1.0.0",
            SourceLifecycle = ActivityDefinitionVersionLifecycle.Active,
            SourceProviderFingerprint = ActivityProviderManifestFingerprint.Compute(sourceProvider),
            TargetProviderFingerprint = $"sha256:{new string('d', 64)}", ReservedDefinition = definition,
            ReservedAuthoringState = authoring, ReservedDraft = draft, ReservedLayout = layout,
            SourceContractFingerprint = ActivityForkMaterialFingerprint.Compute(sourceContract),
            TargetContractFingerprint = $"sha256:{new string('e', 64)}", ExpiresAt = createdAt.AddMinutes(15),
            RetainUntil = createdAt.AddDays(1), RetentionKey = ActivityForkCandidateIdentity.RetentionKey(createdAt.AddDays(1)),
            CreatedAt = createdAt, LastModifiedAt = createdAt
        };
    }

    private static async Task SaveForkSourceAsync(Harness harness, ActivityForkCandidate candidate)
    {
        var createdAt = candidate.CreatedAt.AddHours(-1);
        await harness.SaveAsync(new ActivityDefinitionAuthoringState
        {
            Id = $"authoring-{candidate.SourceDefinitionId}", DefinitionId = candidate.SourceDefinitionId,
            ContentAuthority = new(ActivityContentAuthorityKind.ProviderSource, "source.provider"),
            HeadVersionId = candidate.SourceVersionId, RecommendedVersionId = candidate.SourceVersionId,
            CreatedAt = createdAt, LastModifiedAt = createdAt
        });
        await harness.SaveAsync(new ActivityDefinitionVersionPublication
        {
            Id = $"publication-{candidate.SourceVersionId}", DefinitionVersionId = candidate.SourceVersionId,
            DefinitionId = candidate.SourceDefinitionId, Version = candidate.SourceVersion, ActivityTypeKey = "Acme.Source",
            Contract = Contract(), Provider = Provider(), TemplateId = $"template-{candidate.SourceVersionId}",
            TemplateHash = $"hash-{candidate.SourceVersionId}", SourceReferenceId = $"source-{candidate.SourceVersionId}",
            ProviderFingerprint = candidate.SourceProviderFingerprint, DirectDependencyCount = 0, ClosedTemplateCount = 1,
            RuntimeRequirements = [], Lifecycle = candidate.SourceLifecycle, PublishedAt = createdAt,
            CreatedAt = createdAt, LastModifiedAt = createdAt
        });
    }

    private static ApplyActivityForkCandidateRequest ForkApply(ActivityForkCandidate candidate, string idempotencyKey) => new(
        candidate.Id, candidate.RequestFingerprint, candidate.AccessBindingFingerprint, candidate.ActorId,
        candidate.AuthorizationProfile, idempotencyKey,
        ActivityForkReceiptIdentity.Compute(candidate.TenantId, candidate.ActorId, idempotencyKey), candidate.CreatedAt.AddMinutes(1));

    private static ActivityDefinitionDraftState State(string label) =>
        new(Contract(), Provider(), new Dictionary<string, string> { ["label"] = label });

    private static ActivityContract Contract() => new("1", [], [], []);

    private static ActivityProviderManifest Provider() => new("workflow", "1", Json("{}"));

    private static ActivityLayoutRecord LayoutRecord(string nodeId) => new(nodeId, Json("{}"));

    private static ActivityDependencyItem DependencyItem(
        string relationshipId,
        ActivityDefinitionReference owner,
        ActivityDefinitionReference dependency,
        string occurrenceId) => new(
        relationshipId, owner, dependency, new(occurrenceId, [new("AuthoredNode", occurrenceId)]), true, 1, [owner, dependency]);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed record TestHarness(ActivityDesignV2TestHarness Persistence, GroundworkReusableActivityStores Stores) : IDisposable
    {
        public MutableActivityDesignAccess PersistenceAccess => Persistence.Access;
        public void Dispose() => Persistence.Dispose();
    }

    private sealed class Harness : IDisposable
    {
        private Harness(ActivityDesignV2TestHarness persistence, GroundworkReusableActivityStores stores)
        {
            Persistence = persistence;
            Stores = stores;
        }

        public ActivityDesignV2TestHarness Persistence { get; }

        public GroundworkReusableActivityStores Stores { get; }

        public static Harness Create()
        {
            var persistence = ActivityDesignV2TestHarness.Create();
            var locks = new ImmediateDistributedLockProvider();
            return new(
                persistence,
                new GroundworkReusableActivityStores(
                    persistence.Store,
                    new FixedClock(),
                    locks,
                    persistence.Store,
                    new GroundworkActivityManagementProjectionWriter(persistence.Store, locks, persistence.Store)));
        }

        public Task SaveAsync<TEntity>(TEntity entity) where TEntity : Entity
        {
            var (kind, collection) = entity switch
            {
                ActivityDefinition => (ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionCollection),
                ActivityDefinitionAuthoringState => (ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection),
                ActivityDefinitionDraft => (ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection),
                ActivityDefinitionDraftLayout => (ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection),
                ActivityDefinitionVersionPublication => (ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection),
                ActivityDefinitionVersionLayout => (ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutCollection),
                ActivityDependencyEdge => (ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind, ActivitiesDesignStorageManifest.ActivityDependencyEdgeCollection),
                _ => throw new ArgumentOutOfRangeException(nameof(entity))
            };
            var request = GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
                kind, collection, ActivitiesDesignStorageManifest.SchemaVersion, entity, GroundworkActivitiesDesignJson.Options);
            return Persistence.Store.SaveAsync(request);
        }

        public void Dispose() => Persistence.Dispose();
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
    }
}
