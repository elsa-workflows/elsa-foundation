using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Contracts;
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
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// End-to-end proof of the headline goal: <b>one host-selected database backs every Elsa module</b>. The host
/// composes a single feature (<c>AddGroundworkSqliteUnifiedPersistence</c>) which materializes the unioned
/// runtime + workflows-design + activities-design + workflows-publishing manifest into <b>one</b> SQLite document database and points
/// every lane's neutral ports at it. Elsa's runtime and design code reference only the provider-neutral ports;
/// nothing here is SQLite- or Groundwork-specific except the one host registration call.
/// </summary>
public class UnifiedGroundworkHostTests
{
    // The store is materialized at startup by a hosted service / shell initializer that populates the holder;
    // a bare provider has no host lifecycle, so drive that startup step explicitly before resolving the store.
    private static async Task<ServiceProvider> BuildHostAsync()
    {
        var database = new TemporarySqliteDatabase();
        var provider = new ServiceCollection()
            .AddSingleton(_ => database)
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddGroundworkSqliteUnifiedPersistence(database.ConnectionString)
            .BuildServiceProvider();
        _ = provider.GetRequiredService<TemporarySqliteDatabase>();
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    private static async Task<ServiceProvider> BuildHostAsync(string connectionString)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddGroundworkSqliteUnifiedPersistence(connectionString)
            .BuildServiceProvider();
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    [Fact]
    public async Task Host_registers_one_document_store_shared_by_every_lane()
    {
        await using var provider = await BuildHostAsync();

        var store1 = provider.GetRequiredService<IDocumentStore>();
        var store2 = provider.GetRequiredService<IDocumentStore>();

        // One provider instance backs everything.
        Assert.Same(store1, store2);

        // Runtime lane port resolves.
        Assert.NotNull(provider.GetRequiredService<IWorkflowExecutionStateStore>());

        // Publishing authority uses the same durable store; the API's in-memory fallbacks must not win.
        Assert.NotNull(provider.GetRequiredService<IPublicationSlotStore>());
        Assert.NotNull(provider.GetRequiredService<IPublicationRecordStore>());

        // Design lane ports resolve (scoped).
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>());
    }

    [Fact]
    public async Task One_database_materializes_and_serves_all_four_lanes()
    {
        await using var provider = await BuildHostAsync();
        var store = provider.GetRequiredService<IDocumentStore>();

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
            await firstHost.GetRequiredService<IPublicationRecordStore>().SaveAsync(record);
            var activation = await firstHost.GetRequiredService<IPublicationSlotStore>()
                .TryActivateAsync("definition-1", "default", record.PublicationId, 0, now);
            Assert.True(activation.Succeeded);
        }

        await using var restartedHost = await BuildHostAsync(database.ConnectionString);
        var restoredSlot = await restartedHost.GetRequiredService<IPublicationSlotStore>()
            .FindAsync("definition-1", "default");
        var restoredRecord = await restartedHost.GetRequiredService<IPublicationRecordStore>()
            .FindAsync(record.PublicationId);

        Assert.Equal(record.PublicationId, restoredSlot?.ActivePublicationId);
        Assert.Equal(PublicationStatus.Active, restoredRecord?.Status);
    }

    [Fact]
    public async Task Workflows_design_reads_run_off_the_unified_database()
    {
        await using var provider = await BuildHostAsync();
        var store = provider.GetRequiredService<IDocumentStore>();

        var definition = new WorkflowDefinition { Id = "wf-42", Name = "Order Processing", Description = "Handles orders" };
        var document = new GroundworkDocument<WorkflowDefinition>(
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection, definition);
        await store.SaveAsync(new SaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, GroundworkDesignJson.Options)));

        using var scope = provider.CreateScope();
        var readStore = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var result = await readStore.FindByIdAsync("wf-42");

        Assert.NotNull(result);
        Assert.Equal("Order Processing", result!.Name);
    }

    [Fact]
    public async Task Activities_design_reads_run_off_the_same_unified_database()
    {
        await using var provider = await BuildHostAsync();
        var store = provider.GetRequiredService<IDocumentStore>();

        var activity = new ActivityDefinition { Id = "ad-7", ActivityTypeKey = "Acme.SendEmail", Category = "Email", DisplayName = "Send Email" };
        var document = new GroundworkDocument<ActivityDefinition>(
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection, activity);
        await store.SaveAsync(new SaveDocumentRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            activity.Id,
            ActivitiesDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, GroundworkActivitiesDesignJson.Options)));

        using var scope = provider.CreateScope();
        var readStore = scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>();
        var result = await readStore.GetAsync("ad-7");

        Assert.NotNull(result);
        Assert.Equal("Acme.SendEmail", result!.ActivityTypeKey);
    }

    [Fact]
    public async Task Design_writes_and_reads_run_off_the_one_database()
    {
        await using var provider = await BuildHostAsync();

        using var scope = provider.CreateScope();
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
