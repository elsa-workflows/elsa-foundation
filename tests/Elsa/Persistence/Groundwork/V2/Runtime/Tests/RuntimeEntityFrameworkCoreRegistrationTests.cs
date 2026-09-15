using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        _ = new DbContextOptionsBuilder().UseSqlite(ConnectionString);

        services.AddRuntimeEntityFrameworkCore(Options());

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        var withdrawnCheckpointUnitIds = new[]
        {
            ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind,
            ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
            ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
            ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind,
            ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind,
            ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind,
            ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind,
            ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind,
            ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind
        };
        Assert.DoesNotContain(registry.Registrations, registration =>
            withdrawnCheckpointUnitIds.Contains(registration.Unit.Id.Value, StringComparer.Ordinal));
        // Groundwork registers no backend for scheduler poison or run health, so nothing withdraws those two
        // declarations. They are the only runtime units left behind, and no EF store reads them.
        var runtimeUnitIds = ElsaRuntimeV2StorageManifest.CreateUnits().Select(unit => unit.Id.Value).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            new[] { ElsaRuntimeV2StorageManifest.SchedulerPoisonDocumentKind, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind }.Order(StringComparer.Ordinal),
            registry.Registrations.Select(registration => registration.Unit.Id.Value).Where(runtimeUnitIds.Contains).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IWorkflowDispatchDurabilityEvidence) &&
            descriptor.ImplementationType?.Name?.StartsWith("GroundworkV2", StringComparison.Ordinal) == true);

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
        Assert.Equal(WorkflowSchedulerPoisonStoreBackend.EntityFramework, WorkflowSchedulerPoisonStoreBackend.Find(services)!.Name);
        Assert.Equal(WorkflowTriggerBindingStoreBackend.EntityFramework, WorkflowTriggerBindingStoreBackend.Find(services)!.Name);
        Assert.Equal(RecurringTriggerScheduleStoreBackend.EntityFramework, RecurringTriggerScheduleStoreBackend.Find(services)!.Name);
        Assert.Equal(WorkflowActivationAuthorityBackend.EntityFramework, WorkflowActivationAuthorityBackend.Find(services)!.Name);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowSchedulerPoisonStore));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfRuntimeCheckpointCommitStore>(scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>());
    }

    [Theory]
    [InlineData("Data Source=:memory:", WorkflowDispatchReadinessGuarantee.ProcessLocal)]
    [InlineData("DataSource=:memory:", WorkflowDispatchReadinessGuarantee.ProcessLocal)]
    [InlineData("Filename=:memory:", WorkflowDispatchReadinessGuarantee.ProcessLocal)]
    [InlineData("Data Source=file::memory:?cache=shared", WorkflowDispatchReadinessGuarantee.ProcessLocal)]
    [InlineData("Data Source=file:memorydb?mode=memory&cache=shared", WorkflowDispatchReadinessGuarantee.ProcessLocal)]
    [InlineData("Data Source=file:runtime-ef-readiness;Mode=Memory;Cache=Shared", WorkflowDispatchReadinessGuarantee.ProcessLocal)]
    [InlineData("Data Source=runtime-ef-readiness.db", WorkflowDispatchReadinessGuarantee.DurableReady)]
    public async Task EF_infrastructure_readiness_matches_the_actual_SQLite_database_lifetime(
        string connectionString, WorkflowDispatchReadinessGuarantee expected)
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();
        var options = Options();
        options.ConnectionString = connectionString;
        services.AddRuntimeEntityFrameworkCore(options);
        services.AddSingleton<IWorkflowDispatchDurabilityEvidence>(
            new WorkflowDispatchDurabilityEvidence(WorkflowDispatchDurabilityComponents.Resumption, WorkflowDispatchDurabilityLevel.Durable));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchReadinessAssessor>().AssessAsync();
        Assert.Equal(expected, report.Guarantee);
        Assert.Equal(expected == WorkflowDispatchReadinessGuarantee.DurableReady, report.Ready);
        foreach (var component in new[]
                 {
                     WorkflowDispatchDurabilityComponents.Checkpoint,
                     WorkflowDispatchDurabilityComponents.DispatchStore,
                     WorkflowDispatchDurabilityComponents.Outbox,
                     WorkflowDispatchDurabilityComponents.Scheduler
                 })
            Assert.Equal(expected == WorkflowDispatchReadinessGuarantee.DurableReady
                    ? WorkflowDispatchDurabilityLevel.Durable : WorkflowDispatchDurabilityLevel.ProcessLocal,
                Assert.Single(report.Components, x => x.Component == component).Level);
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

    /// <summary>
    /// The last participant refuses after earlier ones already withdrew Groundwork units (trigger bindings, recurring
    /// schedules and their shared projection state). Both the services and the storage-unit registry must come back.
    /// </summary>
    [Fact]
    public void A_refusal_from_the_last_participant_restores_every_withdrawn_Groundwork_unit()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();
        services.AddScoped<IWorkflowActivationAuthority>(_ => throw new InvalidOperationException("foreign activation authority"));
        var beforeServices = services.ToArray();
        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        var beforeUnits = registry.Registrations;

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeEntityFrameworkCore(Options()));

        Assert.Equal(beforeServices, services);
        Assert.Equal(beforeUnits, registry.Registrations);
        Assert.Contains(registry.Registrations, registration =>
            registration.Unit.Id.Value == ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind);
        Assert.Equal(WorkflowTriggerBindingStoreBackend.Groundwork, WorkflowTriggerBindingStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeCheckpointCommitStoreBackend.Groundwork, RuntimeCheckpointCommitStoreBackend.Find(services)!.Name);
    }

    [Fact]
    public async Task Aggregate_EF_checkpoint_writes_a_real_SQLite_marker_after_Groundwork_withdrawal()
    {
        var connectionString = $"Data Source=file:aggregate-ef-checkpoint-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddPersistenceCore("tenant-a");
        services.AddGroundworkV2RuntimeStores();
        var options = Options();
        options.ConnectionString = connectionString;
        services.AddRuntimeEntityFrameworkCore(options);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
        await context.Database.EnsureCreatedAsync();
        var commit = new RuntimeCheckpointCommit(
            "aggregate-ef-commit",
            new RuntimeCheckpoint("checkpoint-aggregate", "EmptyCheckpoint", "workflow-a", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());

        var writer = scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        Assert.IsType<EfRuntimeCheckpointCommitStore>(writer);
        await writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Single(await context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public void Aggregate_switch_withdraws_only_the_selected_Groundwork_target()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddGroundworkStorageUnit(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind), "other");
        services.AddGroundworkV2RuntimeStores("runtime");

        services.AddRuntimeEntityFrameworkCore(Options());

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.Equal(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind,
            registry.Require(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, "other").Unit.Id.Value);
        Assert.DoesNotContain(registry.Registrations, registration =>
            registration.TargetName == "runtime" &&
            registration.Unit.Id.Value == ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind);
    }

    private static RuntimeEntityFrameworkCoreOptions Options() => new()
    {
        Provider = "Sqlite",
        ConnectionString = ConnectionString,
        HierarchyCursorSigningKey = HierarchySigningKey,
        RecoveryContinuationSigningKey = RecoverySigningKey
    };
}
