using Elsa.Locking.Core;
using System.Text.Json;
using CShells;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: EfToolingShellDefaults(typeof(Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.RuntimeSharedPersistenceTestShellDefaults))]

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class SharedPersistenceCompositionTests : IDisposable
{
    private const string ShellName = "shared-runtime-persistence";
    private const string ArtifactsFeature = "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence";
    private const string PublicationFeature = "WorkflowsRuntimePublicationEntityFrameworkCorePersistence";
    private const string RecoverySigningKey = "shared-runtime-recovery-signing-key-32-bytes";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-shared-runtime-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    [Fact]
    public async Task Artifact_and_test_scope_features_own_the_durable_state_without_the_resumption_runner()
    {
        const string testScopes = "WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence";
        await using var host = CreateHost(new Dictionary<string, string?>
        {
            [$"CShells:Shells:{ShellName}:Features:{ArtifactsFeature}"] = null,
            [$"CShells:Shells:{ShellName}:Features:{ArtifactsFeature}:RecoveryContinuationSigningKey"] = RecoverySigningKey,
            [$"CShells:Shells:{ShellName}:Features:{testScopes}"] = null,
            [$"CShells:Shells:{ShellName}:Features:{testScopes}:RecoveryContinuationSigningKey"] = RecoverySigningKey
        });
        var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var services = scope.ServiceProvider;

        Assert.Null(services.GetService<IRuntimeResumptionService>());
        Assert.Null(services.GetService<IWorkflowSchedulerDrainer>());
        Assert.Null(services.GetService<IWorkflowExecutionStateStore>());
        var database = services.GetRequiredService<RuntimeDbContext>();
        Assert.Equal(databasePath, database.Database.GetDbConnection().DataSource);
        Assert.NotEmpty(await database.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());

        var now = DateTimeOffset.UtcNow;
        var tenant = new PersistenceScope(PersistenceScope.DefaultValue);
        var test = new WorkflowTestScope("isolated-test", now.AddMinutes(5), tenant.Value, new(WorkflowExecutionPartition.DefaultValue));
        var lifecycle = Assert.IsType<EfWorkflowTestScopeStore>(services.GetRequiredService<IWorkflowTestScopeStore>());
        var admission = services.GetRequiredService<IWorkflowTestScopeAdmissionStore>();
        Assert.Same(lifecycle, admission);
        Assert.IsType<EfWorkflowTestScopeCleanupStore>(services.GetRequiredService<IWorkflowTestScopeCleanupStore>());
        await lifecycle.CreateAsync(test, now);
        await admission.AssertOpenAsync(test, now);

        var executable = CreateExecutable(now);
        var materials = Assert.IsType<EfWorkflowExecutableStore>(services.GetRequiredService<IWorkflowExecutableStore>());
        await materials.SaveAsync(executable);
        var pins = services.GetRequiredService<IRuntimeKernelExecutablePinStore>();
        Assert.True(await pins.TryPinAsync([new(tenant, tenant.Value, "actor", executable.Identity, "issuance")]));
        Assert.Contains(executable.Identity.ArtifactId, await pins.ListPinnedExecutableArtifactIdsAsync());
    }

    [Fact]
    public async Task Publication_feature_serves_and_reopens_its_activation_without_the_legacy_runner()
    {
        const string activationId = "activation";
        const string slotName = "default";
        const string node = "root";
        const string eventName = "OrdersReady";
        var features = new Dictionary<string, string?>
        {
            [$"CShells:Shells:{ShellName}:Features:{PublicationFeature}"] = null,
            [$"CShells:Shells:{ShellName}:Features:{ArtifactsFeature}:RecoveryContinuationSigningKey"] = RecoverySigningKey
        };
        var now = DateTimeOffset.UtcNow;
        var executable = CreateExecutable(now);
        var identity = executable.Identity;
        var persistence = new PersistenceScope(PersistenceScope.DefaultValue);
        var slotId = WorkflowActivationSlotIdentity.Create(identity.DefinitionId, slotName);
        var referenceId = WorkflowActivationReferenceIdentity.Create(activationId);
        var claim = RuntimeKernelEventPublication.ReservationKey(slotId, node);

        await using (var host = CreateHost(features))
        {
            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            Assert.Null(services.GetService<IRuntimeResumptionService>());
            Assert.Null(services.GetService<IWorkflowExecutionStateStore>());
            Assert.Null(services.GetService<IRuntimeCheckpointCommitStore>());
            Assert.Null(services.GetService<IStandaloneTriggerStore>());
            var database = services.GetRequiredService<RuntimeDbContext>();
            Assert.Equal(databasePath, database.Database.GetDbConnection().DataSource);
            Assert.NotEmpty(await database.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await database.Database.GetPendingMigrationsAsync());

            await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
            Assert.True(await services.GetRequiredService<IRuntimeKernelExecutablePinStore>()
                .TryPinAsync([new(persistence, persistence.Value, "publication-actor", identity, "issuance")]));
            await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new(referenceId,
                identity.ArtifactId, "definition", identity.DefinitionId, "1", identity.DefinitionId,
                identity.DefinitionVersionId, identity.ArtifactVersion, now, now, WorkflowExecutableReferenceScope.Published,
                ActivationId: activationId, SlotId: slotId));
            var bindings = services.GetRequiredService<IWorkflowTriggerBindingStore>();
            await bindings.PrepareActivationAsync(activationId,
                [new(WorkflowTriggerBinding.BuildId(activationId, identity.ArtifactId, node, claim), identity.ArtifactId,
                    identity.DefinitionId, identity.ArtifactVersion, identity.ArtifactHash, node,
                    RuntimeKernelEventPublication.ReservationType, claim, null,
                    new Dictionary<string, string> { [RuntimeKernelEventPublication.EventNameMetadata] = eventName },
                    now, activationId, slotId, TriggerCardinality.ReservationOnly, IsActive: false)]);
            var schedules = services.GetRequiredService<IRecurringTriggerScheduleStore>();
            await schedules.PrepareActivationAsync(activationId,
                [new(RecurringTriggerSchedule.BuildId(activationId, identity.ArtifactId, node), identity.ArtifactId,
                    node, "timer", "every-minute", RecurringScheduleKind.Interval, "PT1M", now.AddMinutes(1), now,
                    activationId, slotId, IsActive: false)]);
            var activation = Assert.IsType<EfWorkflowActivationSwitch>(services.GetRequiredService<IWorkflowActivationSwitch>());
            var transition = await activation.TryActivateAsync(new(identity.DefinitionId, slotName, activationId,
                WorkflowActivationSource.Publishing, ExpectedRevision: 0, now));
            Assert.True(transition.Succeeded);
        }

        await using (var host = CreateHost(features))
        {
            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var authority = services.GetRequiredService<IWorkflowActivationAuthority>();
            var slot = await authority.FindAsync(identity.DefinitionId, slotName);
            Assert.NotNull(slot);
            Assert.Equal(activationId, slot.ActiveActivationId);
            Assert.Equal(WorkflowActivationProjectionState.Active,
                await services.GetRequiredService<IWorkflowTriggerBindingStore>().FindActivationStateAsync(activationId));
            Assert.Equal(WorkflowActivationProjectionState.Active,
                await services.GetRequiredService<IRecurringTriggerScheduleStore>().FindActivationStateAsync(activationId));
            var triggerScope = new TriggerScope(persistence, TenantId: null);
            var publication = await services.GetRequiredService<IRuntimeKernelEventPublicationStore>()
                .FindAsync(triggerScope, slotId, slot.Revision);
            Assert.NotNull(publication);
            Assert.Equal(new RuntimeKernelEventEntry(node, eventName), Assert.Single(publication.Entries));
            Assert.Equal(identity, publication.Source?.Executable);
            var occurrences = services.GetRequiredService<IRuntimeKernelScheduleOccurrenceStore>();
            Assert.Same(occurrences, services.GetRequiredService<IRuntimeKernelScheduleOccurrenceDeliveryStore>());
            Assert.Same(occurrences, services.GetRequiredService<IRuntimeKernelScheduleOccurrenceRetirementStore>());
            Assert.NotNull(services.GetRequiredService<IRuntimeKernelScheduleControlReader>());
            Assert.Contains(identity.ArtifactId,
                await services.GetRequiredService<IRuntimeKernelExecutablePinStore>().ListPinnedExecutableArtifactIdsAsync());
        }
    }

    public void Dispose()
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private ServiceProvider CreateHost(IReadOnlyDictionary<string, string?> features)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Persistence:DefaultResource"] = "primary",
                ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
                ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
                ["ConnectionStrings:Shared"] = ConnectionString
            })
            .AddInMemoryCollection(features)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDistributedLockProvider, FixtureLockProvider>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEfPersistenceResources(configuration, typeof(SharedPersistenceCompositionTests).Assembly);
        services.AddCShells(shells => shells.WithAssemblies(
                typeof(RuntimeDbContext).Assembly,
                typeof(RuntimeArtifactsEntityFrameworkCoreFeature).Assembly,
                typeof(TasksFeature).Assembly)
            .WithConfigurationProvider(configuration));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FixtureLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static WorkflowExecutable CreateExecutable(DateTimeOffset now)
    {
        var root = new ExecutableNode("root", "root", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        return new(new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            root, new Dictionary<string, WorkflowExecutableResumeTarget>(), now, new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }
}

public sealed class RuntimeSharedPersistenceTestShellDefaults : IEfToolingShellDefaults
{
    public void Configure(CShells.Configuration.ShellBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
    }
}
