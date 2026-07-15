using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Contracts;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using static Elsa.Persistence.Groundwork.RegistrationTests.GroundworkProviderRegistrationAssertions;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// End-to-end proof of the headline goal: <b>one host-selected database backs every Elsa module</b>. The host
/// composes a single feature (<c>AddGroundworkSqliteUnifiedPersistence</c>) which materializes all seven
/// selected feature manifests into <b>one</b> SQLite document database and points every family's neutral
/// ports at it. Nothing here is SQLite- or Groundwork-specific except the one host registration call.
/// </summary>
public class UnifiedGroundworkHostTests
{
    // The provider publishes its admitted session factory at startup. A bare service provider has no host
    // lifecycle, so drive that startup step explicitly before resolving scoped persistence adapters.
    private static async Task<ServiceProvider> BuildHostAsync()
    {
        var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection()
            .AddSingleton(_ => database)
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);
        var provider = services
            .AddGroundworkSqliteUnifiedPersistence(database.ConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _ = provider.GetRequiredService<TemporarySqliteDatabase>();
        await provider.ApplySqliteGroundworkSchemaAsync(database.ConnectionString);
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    private static async Task<ServiceProvider> BuildHostAsync(string connectionString)
    {
        var services = new ServiceCollection()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);
        var provider = services
            .AddGroundworkSqliteUnifiedPersistence(connectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.ApplySqliteGroundworkSchemaAsync(connectionString);
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    [Fact]
    public async Task Host_registers_one_document_store_shared_by_every_lane()
    {
        await using var provider = await BuildHostAsync();
        await using var scope = provider.CreateAsyncScope();

        var store1 = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var store2 = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        // One access-bound adapter instance backs everything within an operation scope.
        Assert.Same(store1, store2);

        // Runtime, IAM, secrets and distributed-runtime ports resolve.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISecretRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>());

        // Publishing authority uses the same durable store; the API's in-memory fallbacks must not win.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPublicationSlotStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPublicationRecordStore>());

        // Design lane ports resolve (scoped).
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>());

        await using var independentScope = provider.CreateAsyncScope();
        Assert.NotSame(store1, independentScope.ServiceProvider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public async Task Identity_only_composition_initializes_without_runtime_history_services()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        services.AddGroundworkIdentityStores();
        services.AddSqliteGroundworkDocumentStore(database.ConnectionString);
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        await provider.ApplySqliteGroundworkSchemaAsync(database.ConnectionString);
        await provider.InitializeGroundworkStoreAsync();

        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public async Task Selected_seven_family_composition_survives_dispose_and_reopen()
    {
        await using var database = new TemporarySqliteDatabase();
        var now = DateTimeOffset.Parse("2026-07-15T10:00:00Z");
        var bookmark = new BookmarkState(
            "bookmark-1",
            "execution-1",
            "activity-execution-1",
            "node-1",
            "resume-1",
            "stimulus",
            "sha256:stimulus",
            null,
            new Dictionary<string, string>(),
            now,
            null);
        var user = new UserRecord(
            "user-1",
            "tenant-1",
            "alice",
            "alice@example.com",
            "Alice",
            UserStatus.Active,
            ResourceOwnership.Foundation,
            new HashSet<string>(),
            new HashSet<string>());
        var secret = new Secret
        {
            Id = "secret-1",
            Name = "payments.api",
            DisplayName = "Payments API",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Versions =
            [
                new SecretVersion
                {
                    Version = 1,
                    Payload = SecretPayload.FromValue("v1")
                }
            ]
        };
        var placement = new ExecutionPlacementClaim("placement-execution-1", "node-a", now, now.AddMinutes(1));
        var workflowDefinition = new WorkflowDefinition { Id = "workflow-1", Name = "Order Processing" };
        var workflowDraft = new WorkflowDefinitionDraft
        {
            Id = "workflow-draft-1",
            WorkflowDefinitionId = workflowDefinition.Id,
            State = new WorkflowDefinitionState([], null, [], [], null, null),
        };
        var activityDefinition = new ActivityDefinition
        {
            Id = "activity-1",
            ActivityTypeKey = "Acme.Send",
            Category = "General",
            DisplayName = "Send"
        };
        var activityVersion = new ActivityDefinitionVersion("1.0.0", activityDefinition.Id)
        {
            Id = "activity-version-1",
            DescriptorType = "Acme.SendActivity",
            DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "send" }),
            SourceKind = "Json",
            SourceId = "asset-1",
            DesignFacets = [],
        };
        var publication = new PublicationRecord(
            "publication-1",
            PublicationSlotIdentity.Create("workflow-1", "default"),
            "workflow-1",
            "workflow-version-1",
            "artifact-1",
            "reference-1",
            0,
            PublicationStatus.Active,
            now,
            now,
            null,
            null);

        await using (var firstHost = await BuildHostAsync(database.ConnectionString))
        {
            await using var firstScope = firstHost.CreateAsyncScope();
            var firstServices = firstScope.ServiceProvider;
            await firstServices.GetRequiredService<IBookmarkStateStore>().SaveAsync(bookmark);
            await firstServices.GetRequiredService<IUserStore>().SaveAsync(user);
            Assert.True(await firstServices.GetRequiredService<ISecretRepository>().TryAddAsync(secret));

            var claim = await firstServices.GetRequiredService<IExecutionPlacementStore>().TryClaimAsync(placement, now);
            Assert.Equal(ExecutionPlacementClaimOutcome.Granted, claim.Outcome);

            using (var scope = firstHost.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>()
                    .Execute(workflowDefinition, workflowDraft, CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<IAddActivityDefinitionCommand>()
                    .Execute(activityDefinition, activityVersion, CancellationToken.None);
            }

            await firstServices.GetRequiredService<IPublicationRecordStore>().SaveAsync(publication);
        }

        await using var reopenedHost = await BuildHostAsync(database.ConnectionString);
        await using var reopenedScope = reopenedHost.CreateAsyncScope();
        var reopenedServices = reopenedScope.ServiceProvider;

        Assert.Equal(
            bookmark.StimulusHash,
            (await reopenedServices.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(bookmark.WorkflowExecutionId, bookmark.BookmarkId))?.StimulusHash);
        Assert.Equal(
            user.Email,
            (await reopenedServices.GetRequiredService<IUserStore>().FindAsync(user.TenantId, user.Id))?.Email);
        Assert.Equal(
            "v1",
            (await reopenedServices.GetRequiredService<ISecretRepository>().FindAsync(secret.Name))?
            .LatestActiveVersion?.Payload.Value);
        Assert.Equal(
            placement.OwnerId,
            (await reopenedServices.GetRequiredService<IExecutionPlacementStore>()
                .FindAsync(placement.WorkflowExecutionId))?.OwnerId);

        using (var scope = reopenedHost.CreateScope())
        {
            Assert.Equal(
                workflowDefinition.Name,
                (await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>()
                    .FindByIdAsync(workflowDefinition.Id))?.Name);
            Assert.Equal(
                activityDefinition.ActivityTypeKey,
                (await scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>()
                    .GetAsync(activityDefinition.Id)).ActivityTypeKey);
        }

        Assert.Equal(
            publication.ArtifactId,
            (await reopenedServices.GetRequiredService<IPublicationRecordStore>()
                .FindAsync(publication.PublicationId))?.ArtifactId);
    }

    [Fact]
    public async Task One_database_materializes_and_serves_all_four_lanes()
    {
        await using var provider = await BuildHostAsync();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        // A document kind from each lane, written and read back through the single store. Success proves the
        // union manifest materialized every lane's schema into the one SQLite database.
        await SaveAsync(store, ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-1", "workflowExecutionState");
        await SaveAsync(store, WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, "def-1", "workflowDefinition");
        await SaveAsync(store, ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "act-1", "activityDefinition");
        await SaveAsync(store, PublishingGroundworkStorageManifest.PublicationSlotDocumentKind, "slot-1", "publicationSlot");

        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-1"));
        Assert.NotNull(await store.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, "def-1"));
        Assert.NotNull(await store.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "act-1"));
        Assert.NotNull(await store.LoadAsync(PublishingGroundworkStorageManifest.PublicationSlotDocumentKind, "slot-1"));
    }

    [Fact]
    public async Task Publication_authority_survives_host_restart()
    {
        await using var database = new TemporarySqliteDatabase();
        var now = DateTimeOffset.Parse("2026-07-13T10:00:00Z");
        var slotId = PublicationSlotIdentity.Create("definition-1", "default");
        var record = new PublicationRecord(
            "publication-1", slotId, "definition-1", "version-1", "artifact-1", "reference-1", 0,
            PublicationStatus.Active, now, now, null, null);

        await using (var firstHost = await BuildHostAsync(database.ConnectionString))
        {
            await using var firstScope = firstHost.CreateAsyncScope();
            await firstScope.ServiceProvider.GetRequiredService<IPublicationRecordStore>().SaveAsync(record);
            var activation = await firstScope.ServiceProvider.GetRequiredService<IPublicationSlotStore>()
                .TryActivateAsync("definition-1", "default", record.PublicationId, 0, now);
            Assert.True(activation.Succeeded);
        }

        await using var restartedHost = await BuildHostAsync(database.ConnectionString);
        await using var restartedScope = restartedHost.CreateAsyncScope();
        var restoredSlot = await restartedScope.ServiceProvider.GetRequiredService<IPublicationSlotStore>()
            .FindAsync("definition-1", "default");
        var restoredRecord = await restartedScope.ServiceProvider.GetRequiredService<IPublicationRecordStore>()
            .FindAsync(record.PublicationId);

        Assert.Equal(record.PublicationId, restoredSlot?.ActivePublicationId);
        Assert.Equal(PublicationStatus.Active, restoredRecord?.Status);
    }

    [Fact]
    public async Task Publication_expiry_cleanup_uses_the_admitted_physical_range_route()
    {
        await using var provider = await BuildHostAsync();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPublicationSnapshotReviewStore>();
        var cutoff = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
        var oldestExpired = SnapshotReview("review-oldest", cutoff.AddMinutes(-2));
        var newestExpired = SnapshotReview("review-newest", cutoff.AddMinutes(-1));
        var active = SnapshotReview("review-active", cutoff.AddMinutes(1));

        Assert.True(await store.TryAddAsync(oldestExpired));
        Assert.True(await store.TryAddAsync(newestExpired));
        Assert.True(await store.TryAddAsync(active));

        var deleted = await store.DeleteExpiredAsync(cutoff, maxCount: 1);

        Assert.Equal(1, deleted);
        Assert.Null(await store.FindAsync(oldestExpired.PreflightToken));
        Assert.NotNull(await store.FindAsync(newestExpired.PreflightToken));
        Assert.NotNull(await store.FindAsync(active.PreflightToken));
    }

    [Fact]
    public async Task Workflows_design_reads_run_off_the_unified_database()
    {
        await using var provider = await BuildHostAsync();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        var definition = new WorkflowDefinition { Id = "wf-42", Name = "Order Processing", Description = "Handles orders" };
        var document = new GroundworkDocument<WorkflowDefinition>(
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection, definition);
        await store.SaveAsync(new SaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, GroundworkDesignJson.Options)));

        var readStore = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var result = await readStore.FindByIdAsync("wf-42");

        Assert.NotNull(result);
        Assert.Equal("Order Processing", result!.Name);
    }

    [Fact]
    public async Task Activities_design_reads_run_off_the_same_unified_database()
    {
        await using var provider = await BuildHostAsync();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        var activity = new ActivityDefinition { Id = "ad-7", ActivityTypeKey = "Acme.SendEmail", Category = "Email", DisplayName = "Send Email" };
        var document = new GroundworkDocument<ActivityDefinition>(
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection, activity);
        await store.SaveAsync(new SaveDocumentRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            activity.Id,
            ActivitiesDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, GroundworkActivitiesDesignJson.Options)));

        var readStore = scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>();
        var result = await readStore.GetAsync("ad-7");

        Assert.NotNull(result);
        Assert.Equal("Acme.SendEmail", result!.ActivityTypeKey);
    }

    [Fact]
    public async Task Design_writes_and_reads_run_off_the_one_database()
    {
        await using var provider = await BuildHostAsync();

        await using var scope = provider.CreateAsyncScope();
        var add = scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>();

        var definition = new WorkflowDefinition { Id = "wf-write", Name = "Invoicing" };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-write",
            WorkflowDefinitionId = "wf-write",
            State = new WorkflowDefinitionState([], null, [], [], null, null),
        };

        // Write through the neutral command; it is backed by the single host-selected Groundwork store.
        await add.Execute(definition, draft, CancellationToken.None);

        // Read back through the neutral read ports — same database, no provider-specific code.
        var definitionStore = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var draftStore = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>();

        var readDefinition = await definitionStore.FindByIdAsync("wf-write");
        var readDraft = await draftStore.FindByWorkflowDefinitionIdAsync("wf-write");

        Assert.NotNull(readDefinition);
        Assert.Equal("Invoicing", readDefinition!.Name);
        Assert.NotNull(readDraft);
        Assert.Equal("draft-write", readDraft!.Id);
    }

    private static Task SaveAsync(IDocumentStore store, string kind, string id, string collection) =>
        store.SaveAsync(new SaveDocumentRequest(kind, id, "1.0.0", $"{{\"collection\":\"{collection}\"}}"));

    private sealed class TenantAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public static TenantAccessContextAccessor Instance { get; } = new();

        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1"));
    }

    private static PublicationSnapshotReview SnapshotReview(string token, DateTimeOffset expiresAt) => new(
        token,
        "sha256:candidate",
        "definition-1",
        PublicationAction.Replace,
        "default",
        PublicationPolicySource.Request,
        PolicyRevision: null,
        RequestedAction: null,
        RequestedSlotName: null,
        RequestedExpectedPublicationId: null,
        SlotRevision: 0,
        ActivePublicationId: null,
        TenantId: null,
        ExpiresAt: expiresAt);

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-unified-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={_path}";

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}
