using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class RuntimeEntityFrameworkCoreRegistrationTests
{
    private const string ConnectionString = "Data Source=:memory:";
    private const string RecoverySigningKey = "runtime-ef-aggregate-recovery-key-32-bytes";
    private const string HierarchySigningKey = "runtime-ef-aggregate-hierarchy-key-32-bytes";

    [Fact]
    public void Groundwork_runtime_switches_all_checkpoint_participants_as_one_EF_composition()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();

        services.AddRuntimeEntityFrameworkCore(Options());

        Assert.Equal(RuntimeOperationalStateStoreBackend.EntityFramework, RuntimeOperationalStateStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeArtifactStoreBackend.EntityFramework, RuntimeArtifactStoreBackend.Find(services)!.Name);
        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, RuntimeActivityExecutionStoreBackend.Find(services)!.Name);
        Assert.Equal(BookmarkStateStoreBackend.EntityFramework, BookmarkStateStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeWorkflowAlterationStoreBackend.EntityFramework, RuntimeWorkflowAlterationStoreBackend.Find(services)!.Name);
        Assert.Equal(WorkflowTestScopeStoreBackend.EntityFramework, WorkflowTestScopeStoreBackend.Find(services)!.Name);
        Assert.Equal(SchedulerWorkQueueStoreBackend.EntityFramework, SchedulerWorkQueueStoreBackend.Find(services)!.Name);
        Assert.Equal(DurableTimerStoreBackend.EntityFramework, DurableTimerStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeWorkflowDispatchStoreBackend.EntityFramework, RuntimeWorkflowDispatchStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimePostCommitOutboxStoreBackend.EntityFramework, RuntimePostCommitOutboxStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeCheckpointCommitStoreBackend.EntityFramework, RuntimeCheckpointCommitStoreBackend.Find(services)!.Name);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfRuntimeCheckpointCommitStore>(scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>());
    }

    [Fact]
    public void Failed_late_participant_transition_restores_services_and_groundwork_registry()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();
        services.AddScoped<IWorkflowDispatchStore>(_ => throw new InvalidOperationException("foreign dispatch store"));
        var beforeServices = services.ToArray();
        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        var beforeUnits = registry.Registrations;

        void Register() => services.AddRuntimeEntityFrameworkCore(Options());
        Assert.Throws<InvalidOperationException>(Register);

        Assert.Equal(beforeServices, services);
        Assert.Equal(beforeUnits, registry.Registrations);
        Assert.Equal(RuntimeCheckpointCommitStoreBackend.Groundwork, RuntimeCheckpointCommitStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeActivityExecutionStoreBackend.Groundwork, RuntimeActivityExecutionStoreBackend.Find(services)!.Name);
    }

    private static RuntimeEntityFrameworkCoreOptions Options() => new()
    {
        Provider = "Sqlite",
        ConnectionString = ConnectionString,
        HierarchyCursorSigningKey = HierarchySigningKey,
        RecoveryContinuationSigningKey = RecoverySigningKey
    };
}
