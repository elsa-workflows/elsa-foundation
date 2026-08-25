using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Groundwork.V2.Testing;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Services;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Store;
using ActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.GroundworkTests;

/// <summary>
/// The invariants that only <c>ActivityDefinitionPublicationTests</c> covered (#1427).
///
/// That suite was deleted with the v1 substrate and is deliberately not ported: most of it asserted the
/// publication-intent machinery — post-commit intent, the redrive, the receipt deliverer, and the
/// colocated/split branching — that the single transaction removed, so porting it would resurrect
/// assertions about a design that was intentionally deleted.
///
/// What did not die with it is <see cref="GroundworkActivityPublicationCommand"/> itself: publication is
/// now one transaction across design, runtime and publishing, and that behaviour still ships. Nothing under
/// <c>tests/</c> referenced the command at all, so these are the assertions nothing else makes.
///
/// The triage of the other 2472 lines is recorded in
/// <c>docs/reports/activity-publication-test-triage-2026-08.md</c>.
/// </summary>
public sealed class ActivityPublicationCommitTests
{
    private static readonly DateTimeOffset Seeded = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Published = new(2026, 7, 15, 12, 5, 0, TimeSpan.Zero);

    private const string Hash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TemplateId = "activity-template-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// The invariant the single-transaction rewrite exists for: one call leaves the design version, the
    /// runtime template, the source reference and the publishing receipt all present, and moves the
    /// authoring head and the draft with them. Under the old split path these landed in separate commits
    /// and the intent machinery reconciled them; there is nothing to reconcile now, so nothing may be
    /// missing either.
    /// </summary>
    [Fact]
    public async Task Publication_commits_design_runtime_and_publishing_together()
    {
        await using var harness = await Harness.CreateAsync();

        var result = await harness.Command.ExecuteAsync(harness.Commit);

        Assert.Equal("definition-1", result.DefinitionId);
        Assert.Equal("version-1", result.DefinitionVersionId);
        Assert.Equal("draft-1", result.DraftId);
        Assert.Equal(TemplateId, result.TemplateId);
        Assert.Equal("source-ref-1", result.SourceReferenceId);

        Assert.NotNull(harness.LoadDesign(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, "version-1"));
        Assert.NotNull(await harness.Templates.FindAsync(TemplateId));
        Assert.NotNull(await harness.SourceReferences.FindAsync("source-ref-1"));
        Assert.NotNull(await harness.Receipts.FindAsync(
            harness.Commit.Receipt.TenantId,
            harness.Commit.Receipt.IdempotencyKey));
    }

    /// <summary>
    /// The head advance and the draft transition ride in the same commit as the artifacts. A publication
    /// that wrote the version but left the draft unpublished would be exactly the torn state the single
    /// transaction was introduced to make impossible.
    /// </summary>
    [Fact]
    public async Task Publication_advances_the_authoring_head_and_publishes_the_draft()
    {
        await using var harness = await Harness.CreateAsync();

        await harness.Command.ExecuteAsync(harness.Commit);

        var authoring = harness.Design<ActivityDefinitionAuthoringState>(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            "authoring-generated-id");
        var draft = harness.Design<ActivityDefinitionDraft>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            "draft-1");

        Assert.Equal("version-1", authoring.HeadVersionId);
        Assert.Equal("version-1", authoring.RecommendedVersionId);
        Assert.Equal(ActivityDefinitionDraftStatus.Published, draft.Status);
    }

    /// <summary>
    /// Authoring state is keyed by its own generated document id, not by the definition id, so the command
    /// has to resolve it by definition. Seeding it under a different id is the whole point: a lookup that
    /// assumed the ids matched would pass against a fixture that made them match.
    /// </summary>
    [Fact]
    public async Task Publication_finds_authoring_by_definition_when_its_document_id_differs()
    {
        await using var harness = await Harness.CreateAsync();
        Assert.NotEqual("definition-1", "authoring-generated-id");

        await harness.Command.ExecuteAsync(harness.Commit);

        var authoring = harness.Design<ActivityDefinitionAuthoringState>(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            "authoring-generated-id");
        Assert.Equal("definition-1", authoring.DefinitionId);
        Assert.Equal("version-1", authoring.HeadVersionId);
    }

    /// <summary>
    /// A tenant publishing an authorized global resource commits under its own operation scope while the
    /// resource stays unscoped. The receipt carries the caller's tenant; the artifacts do not acquire one.
    /// </summary>
    [Fact]
    public async Task Publication_uses_the_tenant_operation_scope_for_an_authorized_global_resource()
    {
        await using var harness = await Harness.CreateAsync(operationTenantId: "tenant-a", scope: "tenant-a");

        var result = await harness.Command.ExecuteAsync(harness.Commit);

        Assert.Equal("version-1", result.DefinitionVersionId);
        Assert.NotNull(await harness.Receipts.FindAsync(
            harness.Commit.Receipt.TenantId,
            harness.Commit.Receipt.IdempotencyKey));
        Assert.NotNull(harness.LoadDesign(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, "version-1"));
    }

    /// <summary>
    /// The rollback invariant, and the one with no substitute anywhere else: a failure inside the
    /// transaction must leave no part of the publication behind. The commit is disturbed at the storage
    /// seam rather than by asking the command to fail, so what is asserted is the transaction's own
    /// atomicity and not an error path the command chose.
    /// </summary>
    [Fact]
    public async Task A_failure_inside_the_transaction_leaves_no_partial_publication()
    {
        await using var harness = await Harness.CreateAsync(failTransaction: true);

        await Assert.ThrowsAnyAsync<Exception>(() => harness.Command.ExecuteAsync(harness.Commit));

        Assert.Null(harness.LoadDesign(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, "version-1"));
        Assert.Null(await harness.Templates.FindAsync(TemplateId));
        Assert.Null(await harness.SourceReferences.FindAsync("source-ref-1"));
        Assert.Null(await harness.Receipts.FindAsync(
            harness.Commit.Receipt.TenantId,
            harness.Commit.Receipt.IdempotencyKey));

        // The seeded draft is untouched too: a rolled-back publication leaves the design where it was.
        var draft = harness.Design<ActivityDefinitionDraft>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            "draft-1");
        Assert.NotEqual(ActivityDefinitionDraftStatus.Published, draft.Status);
    }

    private sealed class Harness(
        GroundworkV2TestPersistence persistence,
        GroundworkV2ActivityDesignStore designStore,
        GroundworkPublishingStorage publishingStorage,
        GroundworkV2ExecutableActivityTemplateStore templates,
        GroundworkV2WorkflowExecutableSourceReferenceStore sourceReferences,
        GroundworkActivityPublicationReceiptStore receipts,
        GroundworkActivityPublicationCommand command,
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit) : IAsyncDisposable
    {
        public GroundworkV2ExecutableActivityTemplateStore Templates { get; } = templates;
        public GroundworkV2WorkflowExecutableSourceReferenceStore SourceReferences { get; } = sourceReferences;
        public GroundworkActivityPublicationReceiptStore Receipts { get; } = receipts;
        public GroundworkActivityPublicationCommand Command { get; } = command;
        public ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> Commit { get; } = commit;

        public static async Task<Harness> CreateAsync(
            string? operationTenantId = null,
            string scope = "default",
            bool failTransaction = false)
        {
            // A publication spans all three lanes in one transaction, so all three are declared against one
            // provider, exactly as a single-target host has them.
            var persistence = GroundworkV2TestPersistence.Create(
                "memory",
                ActivitiesDesignStorageManifest.CreateUnits(),
                ElsaRuntimeV2StorageManifest.CreateUnits(),
                PublishingGroundworkStorageManifest.CreateUnits());
            var access = persistence.Access(scope);
            var seedStore = new GroundworkV2ActivityDesignStore(persistence.Sessions, access);
            await SeedAsync(seedStore);

            // Seeding uses the raw source; only the command under test sees the disturbed one, so the
            // fixture is always built successfully and only the publication transaction fails.
            var sessions = failTransaction
                ? new FailingTransactionSessionSource(persistence.Sessions)
                : persistence.Sessions;
            var designStore = new GroundworkV2ActivityDesignStore(sessions, access);
            var publishingStorage = new GroundworkPublishingStorage(sessions, access);
            var templates = new GroundworkV2ExecutableActivityTemplateStore(sessions, access);
            var sourceReferences = new GroundworkV2WorkflowExecutableSourceReferenceStore(sessions, access);
            var publications = new InMemoryPublicationStore();
            var payloads = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
            var command = new GroundworkActivityPublicationCommand(
                payloads,
                publications,
                new GroundworkActivityDependencyProjection(designStore, publications),
                new GroundworkActivityManagementProjectionWriter(designStore, new ImmediateLockProvider(), designStore),
                new PublishingGroundworkDocumentSerializer(),
                designStore,
                publishingStorage,
                templates,
                sourceReferences);

            return new(
                persistence,
                new GroundworkV2ActivityDesignStore(persistence.Sessions, access),
                new GroundworkPublishingStorage(persistence.Sessions, access),
                new GroundworkV2ExecutableActivityTemplateStore(persistence.Sessions, access),
                new GroundworkV2WorkflowExecutableSourceReferenceStore(persistence.Sessions, access),
                new GroundworkActivityPublicationReceiptStore(
                    persistence.Sessions, access, new PublishingGroundworkDocumentSerializer()),
                command,
                CreateCommit(operationTenantId));
        }

        /// <summary>Reads a design row through the undisturbed store, so assertions see the real state.</summary>
        public ActivityDesignDocument? LoadDesign(string documentKind, string id) => designStore.Load(documentKind, id);

        public TEntity Design<TEntity>(string documentKind, string id) where TEntity : Elsa.Primitives.Entities.Entity =>
            JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<TEntity>>(
                LoadDesign(documentKind, id)!.ContentJson,
                GroundworkActivitiesDesignJson.Options)!.Entity;

        public ValueTask DisposeAsync() => persistence.DisposeAsync();

        private static async Task SeedAsync(GroundworkV2ActivityDesignStore store)
        {
            await SaveAsync(store,
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                new ActivityDefinition
                {
                    Id = "definition-1",
                    ActivityTypeKey = "test.activity",
                    Category = "Tests",
                    DisplayName = "Test activity",
                    CreatedAt = Seeded,
                    LastModifiedAt = Seeded
                });
            // Deliberately not keyed on the definition id: the command must resolve authoring by definition.
            await SaveAsync(store,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
                new ActivityDefinitionAuthoringState
                {
                    Id = "authoring-generated-id",
                    DefinitionId = "definition-1",
                    ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa.design"),
                    CreatedAt = Seeded,
                    LastModifiedAt = Seeded
                });
            await SaveAsync(store,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection,
                new ActivityDefinitionDraft
                {
                    Id = "draft-1",
                    DefinitionId = "definition-1",
                    Revision = 4,
                    State = new(Contract(), Provider(), new Dictionary<string, string>()),
                    CreatedAt = Seeded,
                    LastModifiedAt = Seeded
                });
        }

        private static Task SaveAsync<TEntity>(
            GroundworkV2ActivityDesignStore store,
            string kind,
            string collection,
            TEntity entity) where TEntity : Elsa.Primitives.Entities.Entity =>
            store.SaveAsync(GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
                kind, collection, ActivitiesDesignStorageManifest.SchemaVersion, entity, GroundworkActivitiesDesignJson.Options));
    }

    private static ActivityContract Contract() => new("1", [], [], []);
    private static ActivityProviderManifest Provider() => new("test.provider", "1", Json("{}"));
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> CreateCommit(
        string? operationTenantId)
    {
        var root = new ExecutableNode(
            "boundary",
            "boundary",
            "test.consumer",
            "1",
            new("test.consumer", "1", Json("{\"plan\":1}")),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        var template = new ExecutableActivityTemplate(
            TemplateId,
            Hash,
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [],
            [],
            [new RuntimeRequirement("test.consumer", "1")],
            "provider-fingerprint",
            new Dictionary<string, string>(),
            Published);
        var source = new WorkflowExecutableSourceReference(
            "source-ref-1",
            TemplateId,
            "ActivityDefinitionVersion",
            "version-1",
            "1.0.0",
            "definition-1",
            "version-1",
            "1.0.0",
            Published,
            Published,
            WorkflowExecutableReferenceScope.Published);
        var catalog = new ActivityDefinitionVersion("1.0.0", "definition-1")
        {
            Id = "version-1",
            ProviderKey = "test.provider",
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = "test.consumer",
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = root.Descriptor.Payload,
            SourceKind = "ActivityDefinitionDraft",
            SourceId = "draft-1",
            Hash = Hash,
            CreatedAt = Published,
            LastModifiedAt = Published
        };
        var publication = new ActivityDefinitionVersionPublication
        {
            Id = "version-1",
            DefinitionVersionId = "version-1",
            DefinitionId = "definition-1",
            Version = "1.0.0",
            ActivityTypeKey = "test.activity",
            ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
            SourceDraftId = "draft-1",
            Contract = Contract(),
            Provider = Provider(),
            TemplateId = TemplateId,
            TemplateHash = Hash,
            SourceReferenceId = "source-ref-1",
            ProviderFingerprint = "provider-fingerprint",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 0,
            RuntimeRequirements = [new("test.consumer", "1")],
            ResourceMeasurements = new(1, 1, 0, 1, 10, 20, 0),
            ResumeTargetCount = 0,
            PublishedAt = Published,
            CreatedAt = Published,
            LastModifiedAt = Published
        };
        var layout = new ActivityDefinitionVersionLayout
        {
            Id = "version-1",
            DefinitionVersionId = "version-1",
            Records = [new("boundary", Json("{\"x\":10,\"y\":20}"))],
            CreatedAt = Published,
            LastModifiedAt = Published
        };
        var receipt = new ActivityPublicationReceipt(
            operationTenantId,
            "publish-operation-1",
            ActivityPublicationRequestFingerprint.Compute("draft-1", 4, null, "1.0.0", "sha256:review"),
            ActivityPublicationReceiptStatus.Applied,
            "draft-1",
            4,
            null,
            "sha256:review",
            "1.0.0",
            new("definition-1", "version-1", "draft-1", "1.0.0", TemplateId, Hash, "source-ref-1", Published),
            null,
            [],
            Published);
        return new(
            operationTenantId,
            new("draft-1", 4, "definition-1", null, catalog, publication, layout, []),
            template,
            source,
            receipt);
    }

    /// <summary>
    /// Refuses the publication transaction at the storage seam. The command opens exactly one unit of work,
    /// so failing that is failing the publication — and it disturbs the provider rather than the command,
    /// which is what makes the assertion about the transaction's atomicity rather than about an error path
    /// the command chose to take.
    /// </summary>
    private sealed class FailingTransactionSessionSource(IGroundworkStorageSessionSource inner)
        : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            inner.Open(unitId, access, targetName);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            throw new PublicationTransactionFailure();

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);

        // Publishing refuses to stage without an evidenced atomic commit, so the capability seam has to be
        // forwarded or the command fails for the wrong reason before reaching the transaction.
        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) =>
            inner is IGroundworkStorageCapabilitySource capabilities
                ? capabilities.Capabilities(targetName)
                : throw new NotSupportedException("The wrapped session source reports no capabilities.");
    }

    private sealed class PublicationTransactionFailure() : Exception("The publication transaction failed.");

    private sealed class InMemoryPublicationStore : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionPublication?>(null);

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>([]);
    }

    private sealed class ImmediateLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
