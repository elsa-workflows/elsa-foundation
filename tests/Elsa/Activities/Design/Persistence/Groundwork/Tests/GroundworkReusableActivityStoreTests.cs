using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkReusableActivityStoreTests
{
    [Fact]
    public async Task CreateDefinition_Commits_Definition_Authoring_Draft_And_Layout()
    {
        var harness = Harness.Create();

        await harness.Stores.ExecuteAsync(CreateRequest());

        Assert.Single(harness.Documents.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Equal("definition-1", (await harness.Stores.FindAsync("definition-1"))!.DefinitionId);
        Assert.Equal("draft-1", (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!.Id);
        Assert.Equal("node-1", Assert.Single((await harness.Stores.FindDraftLayoutAsync("draft-1"))!.Records).NodeId);
    }

    [Fact]
    public async Task UpdateDefinitionPresentation_Uses_Document_Cas_And_Preserves_Immutable_Identity_State()
    {
        var harness = Harness.Create();
        var create = CreateRequest();
        await harness.Stores.ExecuteAsync(create);

        var updated = await harness.Stores.ExecuteAsync(new UpdateActivityDefinitionPresentationRequest(
            "definition-1",
            null,
            "Finance",
            "Calculate invoice total",
            "Updated description",
            new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero)));

        var persisted = await new GroundworkActivityDefinitionStore(harness.Documents).GetAsync("definition-1");
        var authoring = await harness.Stores.FindAsync("definition-1");
        Assert.Equal("Finance", persisted.Category);
        Assert.Equal("Calculate invoice total", persisted.DisplayName);
        Assert.Equal("Updated description", persisted.Description);
        Assert.Equal(create.Definition.ActivityTypeKey, persisted.ActivityTypeKey);
        Assert.Equal(create.Definition.TenantId, persisted.TenantId);
        Assert.Equal(create.AuthoringState.ContentAuthority, authoring!.ContentAuthority);
        Assert.Equal(create.AuthoringState.HeadVersionId, authoring.HeadVersionId);
        Assert.Equal(persisted.Category, updated.Category);
    }

    [Fact]
    public async Task UpdateDefinitionPresentation_Rejects_Source_Authority_Without_Writes()
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest(ActivityContentAuthorityKind.ProviderSource));

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(new UpdateActivityDefinitionPresentationRequest(
            "definition-1", null, "Finance", "Updated", null, DateTimeOffset.UtcNow)));

        Assert.Equal("Samples", (await new GroundworkActivityDefinitionStore(harness.Documents).GetAsync("definition-1")).Category);
    }

    [Fact]
    public async Task UpdateDefinitionPresentation_Rejects_A_Concurrent_Document_Write_By_Cas()
    {
        var harness = Harness.Create();
        var create = CreateRequest();
        await harness.Stores.ExecuteAsync(create);
        var envelope = await harness.Documents.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            create.Definition.Id);
        var concurrent = create.Definition;
        concurrent.Category = "Concurrent update";
        var candidate = GroundworkDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            concurrent,
            GroundworkActivitiesDesignJson.Options);
        var concurrentWrite = new SaveDocumentRequest(
            candidate.DocumentKind,
            candidate.Id,
            candidate.SchemaVersion,
            candidate.ContentJson,
            envelope!.Version);
        var racingStore = new RaceInjectingDocumentStore(harness.Documents, concurrentWrite);
        var stores = new GroundworkReusableActivityStores(
            racingStore,
            new FakeClock(),
            new ImmediateDistributedLockProvider(),
            new TestBoundedDocumentStore(racingStore),
            new GroundworkActivityManagementProjectionWriter(
                racingStore,
                new ImmediateDistributedLockProvider(),
                harness.Documents));

        await Assert.ThrowsAsync<DocumentAtomicWriteException>(() => stores.ExecuteAsync(new UpdateActivityDefinitionPresentationRequest(
            "definition-1", null, "Losing update", "Losing update", null, DateTimeOffset.UtcNow)));

        Assert.Equal("Concurrent update", (await new GroundworkActivityDefinitionStore(harness.Documents).GetAsync("definition-1")).Category);
    }

    [Fact]
    public async Task Replace_Uses_Revision_Cas_And_Updates_Draft_And_Layout_Together()
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest());

        await harness.Stores.ExecuteAsync(new ReplaceActivityDraftRequest(
            "draft-1",
            0,
            State("updated"),
            [LayoutRecord("node-2")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(
            new ReplaceActivityDraftRequest("draft-1", 0, State("stale"), [LayoutRecord("stale-node")])));

        var draft = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1");
        var layout = await harness.Stores.FindDraftLayoutAsync("draft-1");
        Assert.Equal(1, draft!.Revision);
        Assert.Equal("updated", draft.State.Options["label"]);
        Assert.Equal(1, layout!.Revision);
        Assert.Equal("node-2", Assert.Single(layout.Records).NodeId);
    }

    [Fact]
    public async Task ApplyContractProposal_Uses_Exact_Provider_Binding_And_Changes_Only_Contract_And_Revisions()
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest());
        var before = (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!;
        var fingerprint = ActivityProviderManifestFingerprint.Compute(before.State.Provider);
        var contract = new ActivityContract(
            "proposal",
            [new("note", "Note", new("String", Elsa.Primitives.Models.CollectionKind.Single), true, true, null, "elsa.json")],
            [new("result", "Result", new("String", Elsa.Primitives.Models.CollectionKind.Single), true, false, "elsa.json")],
            [new("done", "Done", true)]);

        var applied = await harness.Stores.ExecuteAsync(new ApplyActivityContractProposalRequest(
            before.Id,
            before.TenantId,
            before.Revision,
            before.State.Provider.ProviderKey,
            before.State.Provider.SchemaVersion,
            fingerprint,
            contract));

        var layout = await harness.Stores.FindDraftLayoutAsync(before.Id);
        Assert.Equal(1, applied.Revision);
        Assert.Equal("proposal", applied.State.Contract.ContractSchemaVersion);
        Assert.True(Assert.Single(applied.State.Contract.Inputs).IsNullable);
        Assert.False(Assert.Single(applied.State.Contract.Outputs).IsNullable);
        Assert.Equal(before.State.Provider.ProviderKey, applied.State.Provider.ProviderKey);
        Assert.Equal(before.State.Provider.SchemaVersion, applied.State.Provider.SchemaVersion);
        Assert.Equal(before.State.Provider.Payload.GetRawText(), applied.State.Provider.Payload.GetRawText());
        Assert.Equal(before.State.Options["label"], applied.State.Options["label"]);
        Assert.Equal(1, layout!.Revision);
        Assert.Equal("node-1", Assert.Single(layout.Records).NodeId);
    }

    [Fact]
    public async Task ApplyContractProposal_Rejects_Stale_Manifest_Fingerprint_Without_Writes()
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest());
        var before = (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-1"))!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(new ApplyActivityContractProposalRequest(
            before.Id,
            before.TenantId,
            before.Revision,
            before.State.Provider.ProviderKey,
            before.State.Provider.SchemaVersion,
            "sha256:stale",
            new("proposal", [], [], []))));

        var persisted = (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(before.Id))!;
        Assert.Equal(before.Revision, persisted.Revision);
        Assert.Equal(before.State.Contract, persisted.State.Contract);
        Assert.Equal(before.Revision, (await harness.Stores.FindDraftLayoutAsync(before.Id))!.Revision);
    }

    [Theory]
    [InlineData(ActivityContentAuthorityKind.Design, "unexpected-head")]
    [InlineData(ActivityContentAuthorityKind.ProviderSource, null)]
    public async Task CreateDraft_Rejects_Stale_Head_Or_Provider_Authority_Without_Writes(
        ActivityContentAuthorityKind authority,
        string? expectedHead)
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest(authority));
        var request = new CreateActivityDraftRequest(
            Draft("draft-2", "definition-1"),
            DraftLayout("draft-layout-2", "draft-2"),
            expectedHead);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(request));

        Assert.Null(await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync("draft-2"));
        Assert.Null(await harness.Stores.FindDraftLayoutAsync("draft-2"));
    }

    [Fact]
    public async Task Validation_Is_Pinned_To_The_Current_Draft_Revision()
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest());
        var validation = Validation(0);
        await harness.Stores.ExecuteAsync(validation);

        await harness.Stores.ExecuteAsync(new ReplaceActivityDraftRequest(
            "draft-1", 0, State("updated"), [LayoutRecord("node-2")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(Validation(0, "validation-stale")));

        var stored = await ((IActivityDraftValidationStore)harness.Stores).FindAsync("draft-1", 0);
        Assert.NotNull(stored);
        Assert.Equal("activity.test", Assert.Single(stored!.Diagnostics).Code);
        Assert.Null(await ((IActivityDraftValidationStore)harness.Stores).FindAsync("draft-1", 1));
    }

    [Fact]
    public async Task Publication_Layout_Edge_Lifecycle_And_Dependency_Reads_RoundTrip()
    {
        var harness = Harness.Create();
        await harness.SaveAsync(Publication("publication-parent", "version-parent", "definition-parent", "1.0.0", 1));
        await harness.SaveAsync(Publication("publication-child", "version-child", "definition-child", "2.0.0", 0));
        await harness.SaveAsync(new ActivityDefinitionVersionLayout
        {
            Id = "version-layout-parent",
            DefinitionVersionId = "version-parent",
            Records = [LayoutRecord("node-parent")]
        });
        await harness.SaveAsync(new ActivityDependencyEdge
        {
            Id = "edge-1",
            OwnerVersionId = "version-parent",
            OwnerTemplateHash = "hash-version-parent",
            DependencyVersionId = "version-child",
            DependencyTemplateHash = "hash-version-child",
            OccurrenceId = "occurrence-1",
            NodeOrigin = [new ActivityNodeOrigin("Node", "child-node")]
        });

        var publication = await ((IActivityDefinitionVersionPublicationStore)harness.Stores).FindAsync("version-parent");
        var layout = await harness.Stores.FindVersionLayoutAsync("version-parent");
        var edges = await harness.Stores.ListOutboundAsync("version-parent");
        var page = await harness.Stores.ReadAsync(new(
            "version-parent",
            new ActivityDependencyQuery(ActivityDependencyDirection.Outbound, false, new HashSet<string> { "Versions" }),
            null,
            null,
            0,
            10));
        var retired = await harness.Stores.ExecuteAsync(new ChangeActivityVersionLifecycleRequest(
            "version-parent",
            ActivityDefinitionVersionLifecycle.Active,
            ActivityDefinitionVersionLifecycle.Retired,
            "No longer selected"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Stores.ExecuteAsync(
            new ChangeActivityVersionLifecycleRequest(
                "version-parent",
                ActivityDefinitionVersionLifecycle.Active,
                ActivityDefinitionVersionLifecycle.Revoked,
                "Stale administrator view")));

        Assert.Equal(1, publication!.DirectDependencyCount);
        Assert.Equal(2, publication.ClosedTemplateCount);
        Assert.Equal("runtime.graph", Assert.Single(publication.RuntimeRequirements).ConsumerKey);
        Assert.Equal("node-parent", Assert.Single(layout!.Records).NodeId);
        Assert.Equal("edge-1", Assert.Single(edges).Id);
        Assert.Equal(ActivityDependencyConsistencyKind.DerivedProjection, page.Consistency.Kind);
        Assert.Equal("version-child", Assert.Single(page.Items).Dependency.VersionId);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Retired, retired.Lifecycle);
        Assert.Equal(
            ActivityDefinitionVersionLifecycle.Retired,
            (await ((IActivityDefinitionVersionPublicationStore)harness.Stores).FindAsync("version-parent"))!.Lifecycle);
    }

    [Fact]
    public async Task Recommendation_move_picker_and_lifecycle_replacement_share_exact_groundwork_cas()
    {
        var harness = Harness.Create();
        await harness.Stores.ExecuteAsync(CreateRequest(
            headVersionId: "version-2",
            recommendedVersionId: "version-1"));
        await harness.SaveAsync(Publication("publication-1", "version-1", "definition-1", "1.0.0", 0));
        await harness.SaveAsync(Publication("publication-2", "version-2", "definition-1", "2.0.0", 0));

        await harness.Stores.ExecuteAsync(new SetActivityDefinitionRecommendationRequest(
            "definition-1",
            null,
            "version-2",
            "version-1",
            "version-2",
            ActivityDefinitionVersionLifecycle.Active,
            DateTimeOffset.UtcNow));
        var picker = await harness.Stores.ReadAsync(null, 0, 25);
        await harness.Stores.ExecuteAsync(new ChangeActivityVersionLifecycleRequest(
            "version-2",
            ActivityDefinitionVersionLifecycle.Active,
            ActivityDefinitionVersionLifecycle.Retired,
            "Superseded",
            null,
            new(
                "version-2",
                "version-2",
                ActivityRecommendationDisposition.Replace,
                "version-1",
                ActivityDefinitionVersionLifecycle.Active)));

        Assert.Equal("version-2", Assert.Single(picker.Items).Version.DefinitionVersionId);
        Assert.Equal("version-1", (await harness.Stores.FindAsync("definition-1"))!.RecommendedVersionId);
        Assert.Equal(
            ActivityDefinitionVersionLifecycle.Retired,
            (await ((IActivityDefinitionVersionPublicationStore)harness.Stores).FindAsync("version-2"))!.Lifecycle);
    }

    [Fact]
    public async Task Mixed_owner_dependency_projection_rebuilds_with_a_bound_watermark()
    {
        var harness = Harness.Create();
        await harness.SaveAsync(Publication("publication-child", "version-child", "definition-child", "2.0.0", 0));
        var projection = new GroundworkActivityDependencyProjection(harness.Documents, harness.Stores);
        var child = new ActivityDefinitionReference("ActivityVersion", "definition-child", "version-child", "2.0.0", TemplateHash: "hash-version-child");
        var items = new[]
        {
            DependencyItem("activity-draft-use", new("ActivityDraft", "definition-activity-owner", DraftId: "draft-a", Revision: 4), child, "node-a"),
            DependencyItem("workflow-version-use", new("WorkflowVersion", "definition-workflow-owner", "workflow-v1", "1.0.0"), child, "node-w")
        };
        var asOf = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        await projection.RebuildAsync(new("rebuild-1", 17, asOf, items));
        var first = await projection.ReadAsync(new(
            "version-child",
            new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions", "Drafts"])),
            null,
            null,
            0,
            1));
        var second = await projection.ReadAsync(new(
            "version-child",
            new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions", "Drafts"])),
            null,
            first.Watermark,
            first.NextOffset!.Value,
            1));

        Assert.Equal(17, first.Consistency.AsOfSequence);
        Assert.Equal(asOf, first.Consistency.AsOf);
        Assert.Equal("rebuild-1", first.Consistency.RebuildId);
        Assert.Equal(["ActivityDraft", "WorkflowVersion"], first.Items.Concat(second.Items).Select(x => x.Owner.Kind));

        await projection.RebuildAsync(new("rebuild-2", 18, asOf.AddMinutes(1), items));
        await Assert.ThrowsAsync<ActivityDependencyWatermarkExpiredException>(() => projection.ReadAsync(new(
            "version-child",
            new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions"])),
            null,
            first.Watermark,
            0,
            10)));
    }

    [Fact]
    public async Task CreateDefinition_Late_Batch_Conflict_Rolls_Back_Earlier_Writes()
    {
        var documents = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var conflictingLayout = new ActivityDefinitionDraftLayout
        {
            Id = "draft-layout-1",
            DraftId = "unrelated-draft",
            Revision = 0,
            Records = []
        };
        var conflict = GroundworkDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            conflictingLayout,
            GroundworkActivitiesDesignJson.Options);
        var racingStore = new RaceInjectingDocumentStore(documents, conflict);
        var stores = new GroundworkReusableActivityStores(
            racingStore,
            new FakeClock(),
            new ImmediateDistributedLockProvider(),
            new TestBoundedDocumentStore(racingStore),
            new GroundworkActivityManagementProjectionWriter(
                racingStore,
                new ImmediateDistributedLockProvider(),
                documents));

        await Assert.ThrowsAsync<DocumentAtomicWriteException>(() => stores.ExecuteAsync(CreateRequest()));

        Assert.Empty(documents.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Empty(documents.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind));
        Assert.Empty(documents.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind));
        Assert.Single(documents.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind));
    }

    private static CreateActivityDefinitionRequest CreateRequest(
        ActivityContentAuthorityKind authority = ActivityContentAuthorityKind.Design,
        string? headVersionId = null,
        string? recommendedVersionId = null)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new CreateActivityDefinitionRequest(
            new ActivityDefinition
            {
                Id = "definition-1",
                ActivityTypeKey = "Acme.Sample",
                Category = "Samples",
                CreatedAt = now,
                LastModifiedAt = now
            },
            new ActivityDefinitionAuthoringState
            {
                Id = "authoring-1",
                DefinitionId = "definition-1",
                ContentAuthority = new(authority, authority == ActivityContentAuthorityKind.Design ? "design" : "provider"),
                HeadVersionId = headVersionId,
                RecommendedVersionId = recommendedVersionId,
                CreatedAt = now,
                LastModifiedAt = now
            },
            Draft("draft-1", "definition-1", now),
            DraftLayout("draft-layout-1", "draft-1", now));
    }

    private static ActivityDefinitionDraft Draft(string id, string definitionId, DateTimeOffset? now = null) => new()
    {
        Id = id,
        DefinitionId = definitionId,
        Revision = 0,
        State = State("initial"),
        CreatedAt = now ?? default,
        LastModifiedAt = now ?? default
    };

    private static ActivityDefinitionDraftLayout DraftLayout(string id, string draftId, DateTimeOffset? now = null) => new()
    {
        Id = id,
        DraftId = draftId,
        Revision = 0,
        Records = [LayoutRecord("node-1")],
        CreatedAt = now ?? default,
        LastModifiedAt = now ?? default
    };

    private static ActivityDraftValidationState Validation(long revision, string id = "validation-1") => new()
    {
        Id = id,
        DraftId = "draft-1",
        Revision = revision,
        ValidatedAt = new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero),
        Diagnostics =
        [
            new ActivityDiagnostic(
                "activity.test",
                ActivityDiagnosticSeverity.Warning,
                "Test diagnostic",
                new ActivityDiagnosticSubject("Draft", "draft-1", Revision: revision))
        ]
    };

    private static ActivityDefinitionVersionPublication Publication(
        string id,
        string versionId,
        string definitionId,
        string version,
        int directDependencies) => new()
    {
        Id = id,
        DefinitionVersionId = versionId,
        DefinitionId = definitionId,
        Version = version,
        Contract = Contract(),
        Provider = Provider(),
        TemplateId = $"template-{versionId}",
        TemplateHash = $"hash-{versionId}",
        SourceReferenceId = $"source-{versionId}",
        ProviderFingerprint = "provider-fingerprint",
        DirectDependencyCount = directDependencies,
        ClosedTemplateCount = directDependencies + 1,
        RuntimeRequirements = [new ActivityRuntimeRequirementDeclaration("runtime.graph", "1")],
        PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

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
        relationshipId,
        owner,
        dependency,
        new(occurrenceId, [new("AuthoredNode", occurrenceId)]),
        true,
        1,
        [owner, dependency]);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class Harness(InMemoryDocumentStore documents, GroundworkReusableActivityStores stores)
    {
        public InMemoryDocumentStore Documents { get; } = documents;
        public GroundworkReusableActivityStores Stores { get; } = stores;

        public static Harness Create()
        {
            var documents = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
            return new Harness(documents, new GroundworkReusableActivityStores(
                documents,
                new FakeClock(),
                new ImmediateDistributedLockProvider(),
                new TestBoundedDocumentStore(documents),
                new GroundworkActivityManagementProjectionWriter(
                    documents,
                    new ImmediateDistributedLockProvider(),
                    documents)));
        }

        public Task SaveAsync<TEntity>(TEntity entity) where TEntity : Entity
        {
            var (kind, collection) = entity switch
            {
                ActivityDefinitionVersionPublication => (
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection),
                ActivityDefinitionVersionLayout => (
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutCollection),
                ActivityDefinitionDraftLayout => (
                    ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection),
                ActivityDependencyEdge => (
                    ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDependencyEdgeCollection),
                _ => throw new ArgumentOutOfRangeException(nameof(entity))
            };
            return SaveCoreAsync(kind, collection, entity);
        }

        private async Task SaveCoreAsync<TEntity>(string kind, string collection, TEntity entity) where TEntity : Entity
        {
            var result = await Documents.SaveAsync(GroundworkDocumentWriter.ToSaveRequest(
                kind,
                collection,
                ActivitiesDesignStorageManifest.SchemaVersion,
                entity,
                GroundworkActivitiesDesignJson.Options));
            Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
        }
    }

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
    }

    private sealed class RaceInjectingDocumentStore(InMemoryDocumentStore inner, SaveDocumentRequest conflict) : IDocumentStore
    {
        private int _injected;

        public DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _injected, 1) == 0)
                Assert.Equal(DocumentStoreWriteStatus.Saved, (await inner.SaveAsync(conflict, cancellationToken)).Status);
            return await inner.BeginAsync(scope, cancellationToken);
        }
    }

    private sealed class TestBoundedDocumentStore(IDocumentStore documents) : IBoundedDocumentStore
    {
        public async Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            var index = query.QueryIdentity switch
            {
                ActivitiesDesignStorageManifest.ListAllQuery => ActivitiesDesignStorageManifest.ByCollectionIndex,
                "list-by-definition" => ActivitiesDesignStorageManifest.ByDefinitionIndex,
                "list-by-head-version" => ActivitiesDesignStorageManifest.ByHeadVersionIndex,
                "list-by-draft" => ActivitiesDesignStorageManifest.ByDraftIndex,
                "list-by-definition-version" => ActivitiesDesignStorageManifest.ByDefinitionVersionIndex,
                "list-by-owner-version" => ActivitiesDesignStorageManifest.ByOwnerVersionIndex,
                "list-by-dependency-version" => ActivitiesDesignStorageManifest.ByDependencyVersionIndex,
                _ => throw new InvalidOperationException($"Unexpected activity-design test query '{query.QueryIdentity}'.")
            };
            var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
            Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
            var value = Assert.Single(comparison.Values)!;

#pragma warning disable GW0004
            var matches = await documents.QueryAsync(new DocumentStoreQuery(query.DocumentKind, index, value), cancellationToken);
#pragma warning restore GW0004
            IEnumerable<DocumentEnvelope> page = matches.Skip(query.Skip ?? 0);
            if (query.Take is { } take)
                page = page.Take(take);
            return new DocumentQueryResult(page.ToArray(), matches.Count);
        }

        public async Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            (await QueryAsync(query, cancellationToken)).TotalCount;

        public async Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            (await QueryAsync(query.Page(query.Skip, 1), cancellationToken)).Documents.FirstOrDefault();

        public async Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            await FirstOrDefaultAsync(query, cancellationToken) is not null;
    }

}

internal sealed class ImmediateDistributedLockProvider : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();

    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

    public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

    private sealed class Handle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
