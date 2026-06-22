using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Contracts;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// End-to-end proof of the headline goal: <b>one host-selected database backs every Elsa module</b>. The host
/// composes a single feature (<c>AddGroundworkSqliteUnifiedPersistence</c>) which materializes the unioned
/// runtime + workflows-design + activities-design manifest into <b>one</b> SQLite document database and points
/// every lane's neutral ports at it. Elsa's runtime and design code reference only the provider-neutral ports;
/// nothing here is SQLite- or Groundwork-specific except the one host registration call.
/// </summary>
public class UnifiedGroundworkHostTests
{
    private static ServiceProvider BuildHost() =>
        new ServiceCollection()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddGroundworkSqliteUnifiedPersistence("Data Source=:memory:")
            .BuildServiceProvider();

    [Fact]
    public async Task Host_registers_one_document_store_shared_by_every_lane()
    {
        await using var provider = BuildHost();

        var store1 = provider.GetRequiredService<IDocumentStore>();
        var store2 = provider.GetRequiredService<IDocumentStore>();

        // One provider instance backs everything.
        Assert.Same(store1, store2);

        // Runtime lane port resolves.
        Assert.NotNull(provider.GetRequiredService<IWorkflowExecutionStateStore>());

        // Design lane ports resolve (scoped).
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>());
    }

    [Fact]
    public async Task One_database_materializes_and_serves_all_three_lanes()
    {
        await using var provider = BuildHost();
        var store = provider.GetRequiredService<IDocumentStore>();

        // A document kind from each lane, written and read back through the single store. Success proves the
        // union manifest materialized every lane's schema into the one SQLite database.
        await SaveAsync(store, ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-1", "workflowExecutionState");
        await SaveAsync(store, WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, "def-1", "workflowDefinition");
        await SaveAsync(store, ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "act-1", "activityDefinition");

        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-1"));
        Assert.NotNull(await store.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, "def-1"));
        Assert.NotNull(await store.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "act-1"));
    }

    [Fact]
    public async Task Workflows_design_reads_run_off_the_unified_database()
    {
        await using var provider = BuildHost();
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
        await using var provider = BuildHost();
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

    private static Task SaveAsync(IDocumentStore store, string kind, string id, string collection) =>
        store.SaveAsync(new SaveDocumentRequest(kind, id, "1.0.0", $"{{\"collection\":\"{collection}\"}}"));
}
