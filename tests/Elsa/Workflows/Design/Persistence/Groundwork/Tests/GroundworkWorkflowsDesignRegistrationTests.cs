using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Verifies <see cref="GroundworkWorkflowsDesignStoreRegistration.AddGroundworkWorkflowsDesignStores"/> wires
/// every workflow-design read port to its Groundwork (document) implementation and that the registration wins
/// over a previously-registered (e.g. EF Core) store — the single-provider host-composition contract.
/// </summary>
public class GroundworkWorkflowsDesignRegistrationTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? preRegister = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create()));
        services.AddSingleton<IPayloadSerializer, FakePayloadSerializer>();
        preRegister?.Invoke(services);
        services.AddGroundworkWorkflowsDesignStores();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_all_four_read_ports_as_groundwork_implementations()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.IsType<GroundworkWorkflowDefinitionStore>(sp.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.IsType<GroundworkWorkflowDefinitionVersionStore>(sp.GetRequiredService<IWorkflowDefinitionVersionStore>());
        Assert.IsType<GroundworkWorkflowDefinitionDraftStore>(sp.GetRequiredService<IWorkflowDefinitionDraftStore>());
        Assert.IsType<GroundworkWorkflowDefinitionVersionLayoutStore>(sp.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>());
    }

    [Fact]
    public void Groundwork_registration_overrides_a_prior_store()
    {
        using var provider = BuildProvider(services =>
            services.AddScoped<IWorkflowDefinitionStore, PriorStore>());
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();

        Assert.IsType<GroundworkWorkflowDefinitionStore>(resolved);
        Assert.Single(scope.ServiceProvider.GetServices<IWorkflowDefinitionStore>());
    }

    private sealed class PriorStore : IWorkflowDefinitionStore
    {
        public Task<Core.Entities.WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Core.Entities.WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Core.Entities.WorkflowDefinition>> ListAsync(Core.Filters.WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
