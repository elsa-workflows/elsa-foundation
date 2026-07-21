using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
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

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBookmarkStateStore) &&
            descriptor.ImplementationType == typeof(GroundworkBookmarkStateStore));
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

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBookmarkStateStore) &&
            descriptor.ImplementationType == typeof(GroundworkBookmarkStateStore));
        AssertDesignLanePresent(services);
    }

    private static void AssertDesignLanePresent(ServiceCollection services)
    {
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowDefinitionStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityDefinitionStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDesignAtomicWriter));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IGroundworkStorageManifestSource) &&
            descriptor.ImplementationType == typeof(WorkflowsDesignGroundworkStorageManifestSource));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IGroundworkStorageManifestSource) &&
            descriptor.ImplementationType == typeof(ActivitiesDesignGroundworkStorageManifestSource));
    }

    private static void AssertDesignLaneAbsent(ServiceCollection services)
    {
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IWorkflowDefinitionStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IActivityDefinitionStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IDesignAtomicWriter));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IGroundworkStorageManifestSource) &&
            descriptor.ImplementationType == typeof(WorkflowsDesignGroundworkStorageManifestSource));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IGroundworkStorageManifestSource) &&
            descriptor.ImplementationType == typeof(ActivitiesDesignGroundworkStorageManifestSource));
    }
}
