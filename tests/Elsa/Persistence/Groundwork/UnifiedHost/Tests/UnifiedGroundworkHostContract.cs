using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using global::Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>Provider-neutral assertions for a host that binds every Elsa persistence family to one target.</summary>
public static class UnifiedGroundworkHostContract
{
    public static async Task AssertRegistersOneDocumentStoreSharedByEveryLaneAsync(
        Func<Task<ServiceProvider>> buildHostAsync)
    {
        await using var provider = await buildHostAsync();

        var store1 = provider.GetRequiredService<IDocumentStore>();
        var store2 = provider.GetRequiredService<IDocumentStore>();

        Assert.Same(store1, store2);
        Assert.NotNull(provider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionStateStore>());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>());
    }

    public static async Task AssertOneDatabaseMaterializesAndServesAllThreeLanesAsync(
        Func<Task<ServiceProvider>> buildHostAsync)
    {
        await using var provider = await buildHostAsync();
        var store = provider.GetRequiredService<IDocumentStore>();

        await SaveAsync(store, ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-1", "workflowExecutionState");
        await SaveAsync(store, WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, "def-1", "workflowDefinition");
        await SaveAsync(store, ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "act-1", "activityDefinition");

        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, "run-1"));
        Assert.NotNull(await store.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, "def-1"));
        Assert.NotNull(await store.LoadAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "act-1"));
    }

    public static async Task AssertWorkflowsDesignReadsRunOffTheUnifiedDatabaseAsync(
        Func<Task<ServiceProvider>> buildHostAsync)
    {
        await using var provider = await buildHostAsync();
        var store = provider.GetRequiredService<IDocumentStore>();

        var definition = new WorkflowDefinition { Id = "wf-42", Name = "Order Processing", Description = "Handles orders" };
        var document = new GroundworkDocument<WorkflowDefinition>(
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            definition);
        await store.SaveAsync(new SaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, GroundworkDesignJson.Options)));

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>().FindByIdAsync("wf-42");

        Assert.NotNull(result);
        Assert.Equal("Order Processing", result!.Name);
    }

    public static async Task AssertActivitiesDesignReadsRunOffTheUnifiedDatabaseAsync(
        Func<Task<ServiceProvider>> buildHostAsync)
    {
        await using var provider = await buildHostAsync();
        var store = provider.GetRequiredService<IDocumentStore>();

        var activity = new ActivityDefinition { Id = "ad-7", ActivityTypeKey = "Acme.SendEmail", Category = "Email", DisplayName = "Send Email" };
        var document = new GroundworkDocument<ActivityDefinition>(
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            activity);
        await store.SaveAsync(new SaveDocumentRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            activity.Id,
            ActivitiesDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, GroundworkActivitiesDesignJson.Options)));

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>().GetAsync("ad-7");

        Assert.NotNull(result);
        Assert.Equal("Acme.SendEmail", result!.ActivityTypeKey);
    }

    public static async Task AssertDesignWritesAndReadsRunOffTheOneDatabaseAsync(
        Func<Task<ServiceProvider>> buildHostAsync)
    {
        await using var provider = await buildHostAsync();
        using var scope = provider.CreateScope();
        var add = scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>();

        var definition = new WorkflowDefinition { Id = "wf-write", Name = "Invoicing" };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-write",
            WorkflowDefinitionId = "wf-write",
            State = new WorkflowDefinitionState([], null, [], [], null),
        };

        await add.Execute(
            new DesignOperationKey("unified-host-contract.workflow-design-write"),
            definition,
            draft,
            CancellationToken.None);

        var definitionStore = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var draftStore = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>();
        var readDefinition = await definitionStore.FindByIdAsync("wf-write");
        var readDraft = await draftStore.FindByWorkflowDefinitionIdAsync("wf-write");

        Assert.NotNull(readDefinition);
        Assert.Equal("Invoicing", readDefinition!.Name);
        Assert.NotNull(readDraft);
        Assert.Equal("draft-write", readDraft!.Id);
    }

    // The synthetic documents carry every member a kind's non-nullable projected columns require;
    // MongoDB resolves projections strictly at save time where the relational providers coerce.
    private static Task SaveAsync(IDocumentStore store, string kind, string id, string collection)
    {
        var entity = collection switch
        {
            "activityDefinition" =>
                $"{{\"id\":\"{id}\",\"activityTypeKey\":\"Fixture.{id}\",\"category\":\"Fixtures\"}}",
            _ => $"{{\"id\":\"{id}\"}}"
        };
        return store.SaveAsync(new SaveDocumentRequest(
            kind, id, "1.0.0", $"{{\"collection\":\"{collection}\",\"entity\":{entity}}}"));
    }
}
