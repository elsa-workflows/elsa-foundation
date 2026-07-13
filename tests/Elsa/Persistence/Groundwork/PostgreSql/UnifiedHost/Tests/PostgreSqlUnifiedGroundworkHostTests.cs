using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Secrets.Core.Contracts;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using global::Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests;

/// <summary>
/// End-to-end proof of the headline goal against <b>PostgreSQL</b>: one host-selected database backs every Elsa
/// module. The host composes a single feature (<c>AddGroundworkPostgreSqlUnifiedPersistence</c>) which
/// materializes the unioned runtime + workflows-design + activities-design manifest into <b>one</b> PostgreSQL
/// database and points every lane's neutral ports at it. Elsa's runtime and design code reference only the
/// provider-neutral ports; nothing here is PostgreSQL- or Groundwork-specific except the one host registration
/// call. Skips gracefully when Docker is unavailable.
/// </summary>
[Collection(PostgresContainerCollection.Name)]
public sealed class PostgreSqlUnifiedGroundworkHostTests(PostgresContainerFixture fixture)
{
    private async Task<ServiceProvider> BuildHostAsync()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddGroundworkPostgreSqlUnifiedPersistence(await fixture.CreateIsolatedDatabaseAsync())
            .BuildServiceProvider();
        // A bare provider has no host lifecycle; drive the startup initializer that materializes the store.
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    [SkippableFact]
    public async Task Host_registers_one_document_store_shared_by_every_lane()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        await using var provider = await BuildHostAsync();

        var store1 = provider.GetRequiredService<IDocumentStore>();
        var store2 = provider.GetRequiredService<IDocumentStore>();

        // One provider instance backs everything.
        Assert.Same(store1, store2);

        // Runtime lane port resolves.
        Assert.NotNull(provider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionStateStore>());

        // Design lane ports resolve (scoped).
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>());
        Assert.NotNull(provider.GetRequiredService<IStudioPreferenceStore>());
        Assert.NotNull(provider.GetRequiredService<ISecretRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowRunHealthDataSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
    }

    [SkippableFact]
    public async Task Studio_preferences_round_trip_with_cas_on_postgresql()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        await using var provider = await BuildHostAsync();
        var preferences = provider.GetRequiredService<IStudioPreferenceStore>();
        var key = new StudioPreferenceKey("user-1", "tenant-1", "studio-1", "dashboard");
        using var value = JsonDocument.Parse("{\"refreshIntervalMinutes\":5}");

        var created = await preferences.WriteAsync(key, new(1, value.RootElement.Clone()), StudioPreferenceWriteCondition.MustNotExist, DateTimeOffset.UtcNow);
        var stale = await preferences.WriteAsync(key, new(1, value.RootElement.Clone()), StudioPreferenceWriteCondition.MustNotExist, DateTimeOffset.UtcNow);

        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, created.Status);
        Assert.Equal("rev-1", created.Document!.Revision);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, stale.Status);
        Assert.Equal("rev-1", (await preferences.FindAsync(key))!.Revision);
    }

    [SkippableFact]
    public async Task One_database_materializes_and_serves_all_three_lanes()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        await using var provider = await BuildHostAsync();
        var store = provider.GetRequiredService<IDocumentStore>();

        // A document kind from each lane, written and read back through the single store. Success proves the
        // union manifest materialized every lane's schema into the one PostgreSQL database.
        await SaveAsync(store, ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-1", "workflowExecutionState");
        await SaveAsync(store, WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, "def-1", "workflowDefinition");
        await SaveAsync(store, ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "act-1", "activityDefinition");

        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-1"));
        Assert.NotNull(await store.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, "def-1"));
        Assert.NotNull(await store.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "act-1"));
    }

    [SkippableFact]
    public async Task Workflows_design_reads_run_off_the_unified_database()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

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

    [SkippableFact]
    public async Task Activities_design_reads_run_off_the_same_unified_database()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

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

    [SkippableFact]
    public async Task Design_writes_and_reads_run_off_the_one_database()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

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
}
