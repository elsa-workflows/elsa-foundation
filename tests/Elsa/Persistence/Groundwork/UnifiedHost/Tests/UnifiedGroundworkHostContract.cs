using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Contracts;
using global::Groundwork.Kernel;
using global::Groundwork.Query.Model;
using global::Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>Provider-neutral assertions for a host that binds every Elsa persistence family to one target.</summary>
public static class UnifiedGroundworkHostContract
{
    public static async Task AssertRegistersOneProviderConnectionSharedByEveryLaneAsync(
        Func<Task<ServiceProvider>> buildHostAsync)
    {
        await using var provider = await buildHostAsync();

        var connection = provider.GetRequiredService<IStorageProviderConnection>();

        // One physical store, shared by every lane and every scope: that is what makes cross-lane
        // transactions possible and is the whole claim of this preset.
        Assert.Same(connection, provider.GetRequiredService<IStorageProviderConnection>());
        using var scope = provider.CreateScope();
        Assert.Same(connection, scope.ServiceProvider.GetRequiredService<IStorageProviderConnection>());

        Assert.NotNull(provider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>());
    }

    public static async Task AssertOneDatabaseMaterializesAndServesEveryLaneAsync(
        Func<Task<ServiceProvider>> buildHostAsync,
        string storageScope)
    {
        await using var provider = await buildHostAsync();
        using var scope = provider.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IGroundworkStorageSessionSource>();
        var access = StorageAccess.Scoped(new StorageScope(storageScope));

        // A unit from each lane, queried through the one connection. Success proves every lane admitted
        // its own schema into the same database.
        foreach (var unitId in new[]
                 {
                     ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind,
                     WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                     ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                     PublishingGroundworkStorageManifest.PublicationSlotDocumentKind
                 })
        {
            var unit = sessions.Unit(unitId);
            Assert.Empty(sessions.Open(unitId, access).Query(new QueryRequest(
                new TableId(unit.Name),
                Predicate.AlwaysTrue.Instance,
                [],
                Projection.All,
                Paging.Keyset(1))).Rows);
        }
    }

    public static async Task AssertActivitiesDesignReadsRunOffTheUnifiedDatabaseAsync(
        Func<Task<ServiceProvider>> buildHostAsync,
        string storageScope)
    {
        await using var provider = await buildHostAsync();
        using var scope = provider.CreateScope();

        // Written through the lane's own low-level store, so the neutral read below can only succeed if
        // that port resolves against the same host connection.
        await scope.ServiceProvider.GetRequiredService<GroundworkV2ActivityDesignStore>().SaveAsync(
            new ActivityDesignSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                "ad-7",
                ActivitiesDesignStorageManifest.SchemaVersion,
                JsonSerializer.Serialize(
                    new GroundworkV2ActivityDesignDocument<ActivityDefinition>(
                        ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                        new ActivityDefinition
                        {
                            Id = "ad-7",
                            TenantId = storageScope,
                            ActivityTypeKey = "Acme.SendEmail",
                            Category = "Email",
                            DisplayName = "Send Email"
                        }),
                    GroundworkActivitiesDesignJson.Options)));

        var result = await scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>().GetAsync("ad-7");

        Assert.NotNull(result);
        Assert.Equal("Acme.SendEmail", result.ActivityTypeKey);
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
}
