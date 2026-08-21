using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// Proves the reference-host deployment shapes keep their lanes honest at registration time:
/// a runtime-only composition never registers design stores or design manifest sources while
/// retaining the runtime store bridges, a design-only composition is the mirror image, and the
/// combined unified composition registers both lanes.
/// </summary>
public sealed class DeploymentShapeLaneExclusionTests
{
    private const string ConnectionString = "Data Source=:memory:";

    [Fact]
    public void Runtime_only_composition_excludes_the_design_lane_and_retains_runtime_stores()
    {
        var services = new ServiceCollection();
        services.AddSqliteGroundworkDocumentStore(ConnectionString);
        services.AddGroundworkRuntimeStores();

        // The runtime lane binds its adapters to a Groundwork target, so they register through a factory
        // and the implementation type is not visible on the descriptor.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IGroundworkStorageManifestSource) &&
            descriptor.ImplementationType == typeof(RuntimeGroundworkStorageManifestSource));

        AssertDesignLaneAbsent(services);
    }

    [Fact]
    public void Design_only_composition_excludes_the_runtime_bridges_and_retains_design_stores()
    {
        var services = new ServiceCollection();
        services.AddSqliteGroundworkDocumentStore(ConnectionString);
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();

        AssertDesignLanePresent(services);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IGroundworkStorageManifestSource) &&
            descriptor.ImplementationType == typeof(RuntimeGroundworkStorageManifestSource));
    }

    [Fact]
    public void Combined_unified_composition_registers_both_lanes()
    {
        var services = new ServiceCollection();
        services.AddGroundworkSqliteUnifiedPersistence(ConnectionString);

        // The runtime lane binds its adapters to a Groundwork target, so they register through a factory
        // and the implementation type is not visible on the descriptor.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        AssertDesignLanePresent(services);
    }

    /// <summary>
    /// Permanent deletion strands runtime state unless the deleting host can see publication state, and a
    /// design-only host cannot: the publication check ships with the publishing vertical, which this shape does
    /// not compose. It refuses instead of deleting rows it cannot prove are unreferenced (#1283). A reachability
    /// test under the default host would not catch this, because the default host composes publishing.
    /// </summary>
    [Fact]
    public async Task Design_only_composition_refuses_permanent_definition_deletion()
    {
        await using var database = new TemporarySqliteDatabase();
        await using var host = await BuildDesignHostAsync(database);
        using var scope = host.CreateScope();

        var exception = await Assert.ThrowsAsync<PermanentDeletionUnavailableException>(() =>
            PermanentDelete(scope, "definition-1"));

        Assert.Equal("definition-1", exception.DefinitionId);
        // The operator has to learn what to do instead without reading source.
        Assert.Contains("Soft-delete", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The other half: composing publishing supplies the check, and the delete proceeds on its merits.</summary>
    [Fact]
    public async Task Composing_publishing_restores_permanent_definition_deletion()
    {
        await using var database = new TemporarySqliteDatabase();
        await using var host = await BuildDesignHostAsync(
            database,
            services => new WorkflowsPublishingFeature().ConfigureServices(services));
        using var scope = host.CreateScope();

        // Past the composition gate: the command now fails on the definition rather than on the host shape.
        await Assert.ThrowsAsync<EntityNotFoundException>(() => PermanentDelete(scope, "definition-1"));
    }

    private static Task PermanentDelete(IServiceScope scope, string definitionId) =>
        scope.ServiceProvider.GetRequiredService<IDeleteWorkflowDefinitionPermanentlyCommand>().Execute(
            new DesignOperationKey($"permanent-delete-{definitionId}"),
            definitionId,
            CancellationToken.None);

    private static async Task<ServiceProvider> BuildDesignHostAsync(
        TemporarySqliteDatabase database,
        Action<IServiceCollection>? composeFurther = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPayloadSerializer, FakePayloadSerializer>();
        services.AddScoped(_ => GroundworkTestAccess.DefaultAccessContextAccessor);
        services.AddSqliteGroundworkDocumentStore(database.ConnectionString);
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();
        // After the lane, as a host composes it. (Only the composition-gate effect of the addition is asserted by
        // these tests; store non-displacement is a TryAdd property this fixture does not exercise.)
        composeFurther?.Invoke(services);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.ApplySqliteGroundworkSchemaAsync(database.ConnectionString);
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    /// <summary>
    /// The design catalogs declare their storage units directly against the public v2 catalog, so a lane's
    /// presence shows in its store contracts and its own atomic writer rather than in a contributed
    /// manifest source.
    /// </summary>
    private static void AssertDesignLanePresent(ServiceCollection services)
    {
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowDefinitionStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityDefinitionStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Workflows.Design.Persistence.Groundwork.IDesignAtomicWriter));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Activities.Design.Persistence.Groundwork.IDesignAtomicWriter));
    }

    private static void AssertDesignLaneAbsent(ServiceCollection services)
    {
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IWorkflowDefinitionStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IActivityDefinitionStore));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Workflows.Design.Persistence.Groundwork.IDesignAtomicWriter));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Activities.Design.Persistence.Groundwork.IDesignAtomicWriter));
    }
}
