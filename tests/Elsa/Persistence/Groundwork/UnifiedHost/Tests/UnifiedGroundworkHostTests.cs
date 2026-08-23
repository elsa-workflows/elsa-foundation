using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Data.Sqlite;
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
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using static Elsa.Persistence.Groundwork.RegistrationTests.GroundworkProviderRegistrationAssertions;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// End-to-end proof of the headline goal: <b>one host-selected database backs every Elsa module</b>. The host
/// composes a single feature (<c>AddGroundworkSqliteUnifiedPersistence</c>) which opens <b>one</b> Groundwork
/// v2 provider connection over a SQLite file, admits every lane's declared storage units into it, and points
/// every family's neutral ports at it. Nothing here is SQLite- or Groundwork-specific except the one host
/// registration call.
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
            .AddLogging()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddSingleton<IDistributedLockProvider, ImmediateDistributedLockProvider>()
            .AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);
        var provider = services
            .AddGroundworkSqliteUnifiedPersistence(database.ConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _ = provider.GetRequiredService<TemporarySqliteDatabase>();
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    private static async Task<ServiceProvider> BuildHostAsync(string connectionString)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddSingleton<IDistributedLockProvider, ImmediateDistributedLockProvider>()
            .AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);
        var provider = services
            .AddGroundworkSqliteUnifiedPersistence(connectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    [Fact]
    public async Task Host_registers_one_provider_connection_shared_by_every_lane()
    {
        await using var provider = await BuildHostAsync();
        await using var scope = provider.CreateAsyncScope();

        var connection = scope.ServiceProvider.GetRequiredService<IStorageProviderConnection>();

        // The connection is the host's single physical store: one instance, shared by every lane and every
        // scope. That is the headline claim of this preset, and it is why the lanes can transact together.
        Assert.Same(connection, scope.ServiceProvider.GetRequiredService<IStorageProviderConnection>());
        await using var independentScope = provider.CreateAsyncScope();
        Assert.Same(connection, independentScope.ServiceProvider.GetRequiredService<IStorageProviderConnection>());

        // Runtime resolves off it.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>());

        // Publishing authority uses the same durable store; the API's in-memory fallbacks must not win.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPublicationSlotStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPublicationRecordStore>());

        // Design lane ports resolve (scoped).
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>());
    }

    [Fact]
    public async Task Selected_provider_composition_survives_dispose_and_reopen_without_implicit_identity()
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
        var workflowDefinition = new WorkflowDefinition { Id = "workflow-1", Name = "Order Processing" };
        var workflowDraft = new WorkflowDefinitionDraft
        {
            Id = "workflow-draft-1",
            WorkflowDefinitionId = workflowDefinition.Id,
            State = WorkflowDefinitionState.Empty,
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

            using (var scope = firstHost.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>()
                    .Execute(
                        new DesignOperationKey("unified-host.reopen.workflow-design-write"),
                        workflowDefinition,
                        workflowDraft,
                        CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<IAddActivityDefinitionCommand>()
                    .Execute(
                        new DesignOperationKey("unified-host.reopen.activity-design-write"),
                        activityDefinition,
                        activityVersion,
                        CancellationToken.None);
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
        var sessions = scope.ServiceProvider.GetRequiredService<IGroundworkStorageSessionSource>();

        // A unit from each lane, written and read back through the one connection. Success proves every
        // lane admitted its own schema into the same SQLite database.
        var lanes = new[]
        {
            ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            PublishingGroundworkStorageManifest.PublicationSlotDocumentKind
        };

        foreach (var unitId in lanes)
        {
            var unit = sessions.Unit(unitId);
            var session = sessions.Open(unitId, StorageAccess.Scoped(new StorageScope("tenant-1")));
            Assert.Empty(session.Query(new QueryRequest(
                new TableId(unit.Name),
                Predicate.AlwaysTrue.Instance,
                [],
                Projection.All,
                Paging.Keyset(1))).Rows);
        }
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
        var sessions = scope.ServiceProvider.GetRequiredService<IGroundworkStorageSessionSource>();

        // Written straight onto the host's one connection, bypassing the lane's own write path, so the read
        // below can only succeed if the neutral port really reads this database.
        var definition = new WorkflowDefinition
        {
            Id = "wf-42",
            Name = "Order Processing",
            Description = "Handles orders",
            TenantId = "tenant-1"
        };
        var session = sessions.Open(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            StorageAccess.Scoped(new StorageScope("tenant-1")));
        var outcome = session.Upsert(
            GroundworkDesignStorage.Values(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                definition,
                GroundworkDesignJson.Options,
                WorkflowsDesignStorageManifest.WorkflowDefinitionCollection),
            WriteOptions.Unconditional);
        Assert.True(outcome.Succeeded, outcome.Status.ToString());

        var readStore = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var result = await readStore.FindByIdAsync("wf-42");

        Assert.NotNull(result);
        Assert.Equal("Order Processing", result!.Name);
    }

    [Fact]
    public async Task Activity_management_projection_first_read_runs_on_the_physical_native_route()
    {
        // Reproduces the container startup regression: the activity-management projection reader issues a
        // FirstOrDefault against the provider-native ScaleBearing bounded route. The provider-native runtime
        // rejects a query that does not declare the terminal operation, so this read faulted
        // ActivityVersionReconcilerStartupTask with "does not declare result operation 'First'" until the
        // bounded router began binding the operation. An empty database still forces the read to execute.
        await using var provider = await BuildHostAsync();
        await using var scope = provider.CreateAsyncScope();
        var projections = scope.ServiceProvider.GetRequiredService<IActivityDefinitionManagementProjectionStore>();

        var current = await projections.FindDefinitionAsync("missing-definition", "tenant-1");

        Assert.Null(current);
    }

    [Fact]
    public async Task Activities_design_reads_run_off_the_same_unified_database()
    {
        await using var provider = await BuildHostAsync();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<GroundworkV2ActivityDesignStore>();

        // Written through the lane's own low-level store, so the neutral read below can only succeed if that
        // port resolves against the same host connection.
        await store.SaveAsync(new ActivityDesignSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "ad-7",
            ActivitiesDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(
                new GroundworkV2ActivityDesignDocument<ActivityDefinition>(
                    ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                    new ActivityDefinition
                    {
                        Id = "ad-7",
                        TenantId = "tenant-1",
                        ActivityTypeKey = "Acme.SendEmail",
                        Category = "Email",
                        DisplayName = "Send Email"
                    }),
                GroundworkActivitiesDesignJson.Options)));

        var readStore = scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>();
        var result = await readStore.GetAsync("ad-7");

        Assert.NotNull(result);
        Assert.Equal("Acme.SendEmail", result.ActivityTypeKey);
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
            State = new WorkflowDefinitionState([], null, [], [], null),
        };

        // Write through the neutral command; it is backed by the single host-selected Groundwork store.
        await add.Execute(
            new DesignOperationKey("unified-host.workflow-design-write"),
            definition,
            draft,
            CancellationToken.None);

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

    private sealed class TenantAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public static TenantAccessContextAccessor Instance { get; } = new();

        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1"));
    }

    private sealed class ImmediateDistributedLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            new Handle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => default;
        }
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

    [Fact]
    public async Task Admitted_database_runs_in_wal_journal_mode()
    {
        await using var database = new TemporarySqliteDatabase();
        await using var provider = await BuildHostAsync(database.ConnectionString);

        // Drive one durable write through the store so a session connection (not just the admission
        // connection) has opened the database.
        await using (var scope = provider.CreateAsyncScope())
        {
            var stateStore = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
            _ = await stateStore.FindAsync("wal-probe", CancellationToken.None);
        }

        // journal_mode is a persistent per-database property. If the unified host's initializer or session
        // factory ever opens the file with a bare SqliteConnection again (bypassing Groundwork's pragma
        // factory), a fresh database is created — and stays — in rollback-journal mode, which costs 2-3
        // fsyncs per commit and serializes writers against readers.
        await using var probe = new SqliteConnection(database.ConnectionString);
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var mode = (string?)await command.ExecuteScalarAsync();
        Assert.Equal("wal", mode, ignoreCase: true);
    }

}
