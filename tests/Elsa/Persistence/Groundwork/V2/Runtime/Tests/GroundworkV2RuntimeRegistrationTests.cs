using CShells.Features;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Groundwork.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2RuntimeRegistrationTests
{
    [Fact]
    public void Runtime_shell_feature_exposes_and_threads_the_clean_break_contract()
    {
        var defaults = new GroundworkWorkflowRuntimeFeature();
        var feature = new GroundworkWorkflowRuntimeFeature
        {
            Target = "runtime",
            CacheWorkflowExecutables = false,
            WorkflowExecutableCacheCapacity = 41
        };
        var metadata = Assert.Single(
            typeof(GroundworkWorkflowRuntimeFeature)
                .GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());
        var services = new ServiceCollection();

        feature.ConfigureServices(services);

        Assert.Equal("GroundworkWorkflowRuntime", metadata.Name);
        Assert.False(typeof(GroundworkWorkflowRuntimeFeature).IsSealed);
        Assert.Contains("WorkflowsRuntimeResumption", metadata.DependsOn.Select(dependency => dependency?.ToString()));
        Assert.True(defaults.CacheWorkflowExecutables);
        Assert.Equal(WorkflowExecutableCacheOptions.DefaultCapacity, defaults.WorkflowExecutableCacheCapacity);
        var options = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkflowExecutableCacheOptions));
        var configured = Assert.IsType<WorkflowExecutableCacheOptions>(options.ImplementationInstance);
        Assert.False(configured.Enabled);
        Assert.Equal(41, configured.Capacity);
        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.All(registry.Registrations, registration => Assert.Equal("runtime", registration.TargetName));
        Assert.Contains(
            typeof(GroundworkWorkflowRuntimeFeature).GetProperties(),
            property => property.Name == nameof(GroundworkWorkflowRuntimeFeature.Target)
                        && property.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "ManifestSettingAttribute"));
    }

    [Fact]
    public void Aggregate_registration_declares_the_complete_manifest_and_replaces_every_runtime_boundary()
    {
        var services = new ServiceCollection();

        services.AddGroundworkV2RuntimeStores(new WorkflowExecutableCacheOptions { Enabled = false });

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.Equal(
            ElsaRuntimeV2StorageManifest.CreateUnits().Select(unit => unit.Id.Value).Order(StringComparer.Ordinal),
            registry.Registrations.Select(registration => registration.Unit.Id.Value));

        AssertScopedAlias<IBookmarkStateStore, GroundworkV2BookmarkStateStore>(services);
        AssertScopedAlias<IBookmarkStimulusIndex, GroundworkV2BookmarkStateStore>(services);
        AssertScopedAlias<IWorkflowExecutableStore, GroundworkV2WorkflowExecutableStore>(services);
        AssertScopedAlias<IExecutableActivityTemplateStore, GroundworkV2ExecutableActivityTemplateStore>(services);
        AssertScopedAlias<IExecutableActivityTemplateReader, GroundworkV2ExecutableActivityTemplateStore>(services);
        AssertScopedAlias<IExecutableActivityTemplateWriter, GroundworkV2ExecutableActivityTemplateStore>(services);
        AssertScopedAlias<IWorkflowExecutableSourceReferenceStore, GroundworkV2WorkflowExecutableSourceReferenceStore>(services);
        AssertScopedAlias<IWorkflowExecutableSourceReferenceReader, GroundworkV2WorkflowExecutableSourceReferenceStore>(services);
        AssertScopedAlias<IWorkflowExecutableSourceReferenceWriter, GroundworkV2WorkflowExecutableSourceReferenceStore>(services);
        AssertScopedAlias<IActivityExecutionStateStore, GroundworkV2ActivityExecutionStateStore>(services);
        AssertScopedAlias<IActivityExecutionInspectionStore, GroundworkV2ActivityExecutionInspectionStore>(services);
        AssertScopedAlias<IActivityExecutionInspectionWriter, GroundworkV2ActivityExecutionInspectionStore>(services);
        AssertScopedAlias<IActivityExecutionHierarchyStore, GroundworkV2ActivityExecutionHierarchyStore>(services);
        AssertScopedAlias<IActivityExecutionHierarchyReader, GroundworkV2ActivityExecutionHierarchyStore>(services);
        AssertScopedAlias<IActivityExecutionHierarchyWriter, GroundworkV2ActivityExecutionHierarchyStore>(services);
        AssertScopedAlias<IWorkflowExecutionStateStore, GroundworkV2WorkflowExecutionStateStore>(services);
        AssertScopedAlias<IWorkflowAlterationStore, GroundworkV2WorkflowAlterationStore>(services);
        AssertScopedAlias<IWorkflowTestScopeStore, GroundworkV2WorkflowTestScopeStore>(services);
        AssertScopedAlias<IWorkflowTestScopeAdmissionStore, GroundworkV2WorkflowTestScopeStore>(services);
        AssertScopedAlias<IWorkflowTestScopeCleanupStore, GroundworkV2WorkflowTestScopeCleanupStore>(services);
        AssertScopedAlias<IDurableValueStateStore, GroundworkV2DurableValueStateStore>(services);
        AssertScopedAlias<ISchedulerStateStore, GroundworkV2SchedulerStateStore>(services);
        AssertScopedAlias<IExecutionLivenessStateStore, GroundworkV2ExecutionLivenessStateStore>(services);
        AssertScopedAlias<IRuntimeRecoveryScanner, GroundworkV2RuntimeRecoveryScanner>(services);
        AssertScopedAlias<IWorkflowHoldStateStore, GroundworkV2WorkflowHoldStateStore>(services);
        AssertScopedAlias<IIncidentStateStore, GroundworkV2IncidentStateStore>(services);
        AssertScopedAlias<IWorkflowRuntimeAttentionQuery, GroundworkV2WorkflowRuntimeAttentionQuery>(services);
        AssertScopedAlias<IWorkflowDispatchStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchQueryStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchDeleteStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchRetentionRootStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchAdmissionStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchCancellationStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IRuntimeCheckpointCommitStore, GroundworkV2RuntimeCheckpointWriter>(services);
        AssertScopedAlias<IRuntimePostCommitOutboxStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IPostCommitOutboxLookupStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IRuntimePostCommitOutboxClaimStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IRuntimePostCommitOutboxClaimCompletionStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IWorkflowDispatchRedriveStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IWorkflowSchedulerWorkQueue, GroundworkV2WorkflowSchedulerWorkQueue>(services);
        AssertScopedAlias<IWorkflowSchedulerWorkClaimInspection, GroundworkV2WorkflowSchedulerWorkQueue>(services);
        AssertScopedAlias<IWorkflowSchedulerPoisonStore, GroundworkV2WorkflowSchedulerPoisonStore>(services);
        AssertScopedAlias<IDurableTimerStore, GroundworkV2DurableTimerStateStore>(services);
        AssertScopedAlias<IWorkflowTriggerBindingStore, GroundworkV2WorkflowTriggerBindingStore>(services);
        AssertScopedAlias<IRecurringTriggerScheduleStore, GroundworkV2RecurringTriggerScheduleStore>(services);
        AssertScopedAlias<IWorkflowActivationAuthority, GroundworkV2WorkflowActivationAuthority>(services);
        Assert.Equal(RuntimeWorkflowAlterationStoreBackend.Groundwork, RuntimeWorkflowAlterationStoreBackend.Find(services)!.Name);
        Assert.Equal(WorkflowTestScopeStoreBackend.Groundwork, WorkflowTestScopeStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeWorkflowDispatchStoreBackend.Groundwork, RuntimeWorkflowDispatchStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimePostCommitOutboxStoreBackend.Groundwork, RuntimePostCommitOutboxStoreBackend.Find(services)!.Name);
    }

    [Fact]
    public void Groundwork_dispatch_and_outbox_transition_to_EF_after_operational_switch()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();
        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);

        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        var beforeOutbox = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimePostCommitOutboxEntityFrameworkCore());
        Assert.Equal(beforeOutbox, services);

        services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
        services.AddRuntimePostCommitOutboxEntityFrameworkCore();

        Assert.Equal(RuntimeWorkflowDispatchStoreBackend.EntityFramework, RuntimeWorkflowDispatchStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimePostCommitOutboxStoreBackend.EntityFramework, RuntimePostCommitOutboxStoreBackend.Find(services)!.Name);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowDispatchStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2RuntimePostCommitOutboxStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(EfWorkflowDispatchStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(EfRuntimePostCommitOutboxStore));
        Assert.Equal(RuntimeArtifactStoreBackend.Groundwork, RuntimeArtifactStoreBackend.Find(services)!.Name);
        Assert.Equal(RuntimeActivityExecutionStoreBackend.Groundwork, RuntimeActivityExecutionStoreBackend.Find(services)!.Name);
        Assert.DoesNotContain(registry.Registrations, registration =>
            registration.Unit.Id.Value is ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind or
            ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind);
    }

    [Fact]
    public void Groundwork_dispatch_transition_refuses_unowned_contract_without_mutation()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddGroundworkV2RuntimeStores();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.AddScoped<IWorkflowDispatchStore>(_ => throw new InvalidOperationException("foreign"));
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeWorkflowDispatchEntityFrameworkCore());
        Assert.Equal(before, services);
        Assert.Equal(RuntimeWorkflowDispatchStoreBackend.Groundwork, RuntimeWorkflowDispatchStoreBackend.Find(services)!.Name);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowDispatchStore));
    }

    [Fact]
    public void Groundwork_registration_resolves_recovery_protector_from_a_stable_configured_key()
    {
        var services = new ServiceCollection();
        services.Configure<RuntimeRecoveryContinuationOptions>(options =>
            options.SigningKey = "stable-groundwork-recovery-signing-key-32-bytes");
        services.AddGroundworkV2RuntimeStores();

        using var provider = services.BuildServiceProvider();
        var codec = provider.GetRequiredService<IRuntimeRecoveryContinuationCodec>();
        var token = codec.Encode("recovery-page", [1, 2, 3]);

        Assert.Equal([1, 2, 3], codec.Decode("recovery-page", token));
    }

    [Fact]
    public void Groundwork_registration_refuses_recovery_protector_without_a_stable_key()
    {
        var services = new ServiceCollection();
        services.AddGroundworkV2RuntimeStores();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IRuntimeRecoveryContinuationCodec>());

        Assert.StartsWith(
            "Runtime recovery continuation signing key must be configured for durable recovery paging.",
            exception.Message);
        Assert.Contains(nameof(GroundworkWorkflowRuntimeFeature.RecoveryContinuationSigningKey), exception.Message);
    }

    [Fact]
    public void Operational_state_switches_between_groundwork_and_ef_without_stale_units()
    {
        var services = new ServiceCollection();
        services.Configure<RuntimeRecoveryContinuationOptions>(options =>
            options.SigningKey = "shared-runtime-recovery-signing-key-32-bytes");
        services.AddGroundworkV2RuntimeStores();

        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = "shared-runtime-recovery-signing-key-32-bytes"
        });

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.Equal(RuntimeOperationalStateStoreBackend.EntityFramework, RuntimeOperationalStateStoreBackend.Find(services)!.Name);
        Assert.DoesNotContain(registry.Registrations, registration =>
            registration.Unit.Id.Value is ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind or ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(EfDurableValueStateStore) && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(EfSchedulerStateStore) && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDurableValueStateStore) && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ISchedulerStateStore) && descriptor.Lifetime == ServiceLifetime.Scoped);

        services.AddGroundworkV2RuntimeStores();
        services.AddGroundworkV2RuntimeStores();

        Assert.Equal(RuntimeOperationalStateStoreBackend.Groundwork, RuntimeOperationalStateStoreBackend.Find(services)!.Name);
        AssertScopedAlias<IDurableValueStateStore, GroundworkV2DurableValueStateStore>(services);
        AssertScopedAlias<ISchedulerStateStore, GroundworkV2SchedulerStateStore>(services);
        Assert.Equal(
            ElsaRuntimeV2StorageManifest.CreateUnits().Select(unit => unit.Id.Value).Order(StringComparer.Ordinal),
            registry.Registrations.Select(registration => registration.Unit.Id.Value).Order(StringComparer.Ordinal));

        var efFirst = new ServiceCollection();
        efFirst.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = "shared-runtime-recovery-signing-key-32-bytes"
        });
        efFirst.AddGroundworkV2RuntimeStores();

        Assert.Equal(RuntimeOperationalStateStoreBackend.Groundwork, RuntimeOperationalStateStoreBackend.Find(efFirst)!.Name);
        AssertScopedAlias<IDurableValueStateStore, GroundworkV2DurableValueStateStore>(efFirst);
        AssertScopedAlias<ISchedulerStateStore, GroundworkV2SchedulerStateStore>(efFirst);
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(EfDurableValueStateStore));
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(EfSchedulerStateStore));
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.DoesNotContain(efFirst, descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
    }

    // Recovery paging is only exercised by the background resumption sweep, so a missing key would otherwise
    // surface as a repeating sweep failure. The startup task moves that failure to shell activation.
    [Fact]
    public void Groundwork_registration_validates_the_recovery_protector_once_during_startup()
    {
        var services = new ServiceCollection();

        services.AddGroundworkV2RuntimeStores();
        services.AddGroundworkV2RuntimeStores();

        Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IStartupTask) &&
                          descriptor.ImplementationType == typeof(ValidateRuntimeRecoveryContinuationCodecStartupTask));
    }

    [Fact]
    public void Repeated_default_registration_is_idempotent_and_retains_the_bounded_cache_boundary()
    {
        var services = new ServiceCollection();

        services.AddGroundworkV2RuntimeStores();
        services.AddGroundworkV2RuntimeStores();

        var options = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkflowExecutableCacheOptions));
        var configured = Assert.IsType<WorkflowExecutableCacheOptions>(options.ImplementationInstance);
        Assert.True(configured.Enabled);
        Assert.Equal(WorkflowExecutableCacheOptions.DefaultCapacity, configured.Capacity);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableStore) && !descriptor.IsKeyedService);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableStore) && descriptor.IsKeyedService);
        Assert.Equal(
            4,
            services.Count(descriptor => descriptor.ServiceType == typeof(IWorkflowDispatchDurabilityEvidence)));

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.Equal(ElsaRuntimeV2StorageManifest.CreateUnits().Count, registry.Registrations.Count);
    }

    [Fact]
    public void Conflicting_runtime_targets_are_refused_during_composition()
    {
        var services = new ServiceCollection();
        services.AddGroundworkV2RuntimeStores(targetName: "runtime-a");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddGroundworkV2RuntimeStores(targetName: "runtime-b"));

        Assert.Contains("runtime-a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("runtime-b", exception.Message, StringComparison.Ordinal);
        Assert.Contains("one physical target", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregate_registration_restores_the_exact_service_collection_when_backend_registration_fails()
    {
        var services = new ThrowingServiceCollection(
            descriptor => descriptor.ServiceType == typeof(RuntimeArtifactStoreBackend));
        services.Add(ServiceDescriptor.Singleton<object>(new object()));
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkV2RuntimeStores());

        Assert.Equal(before, services);
    }

    [Fact]
    public void Ef_replacement_failure_preserves_groundwork_unit_declarations()
    {
        var source = new ServiceCollection();
        source.AddWorkflowRuntime();
        source.AddGroundworkV2RuntimeStores(targetName: "runtime");
        var services = new ThrowingServiceCollection(descriptor =>
            descriptor.ServiceType == typeof(RuntimeArtifactStoreBackend) &&
            descriptor.ImplementationInstance is RuntimeArtifactStoreBackend backend &&
            backend.Name == RuntimeArtifactStoreBackend.EntityFramework);
        services.DisableThrowing();
        foreach (var descriptor in source)
            services.Add(descriptor);
        services.EnableThrowing();
        var before = services.ToArray();
        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        var beforeIds = registry.Registrations.Select(registration => registration.Unit.Id.Value).ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeArtifactsEntityFrameworkCore(new()));

        Assert.Equal(before, services);
        Assert.Equal(beforeIds, registry.Registrations.Select(registration => registration.Unit.Id.Value));
    }

    [Fact]
    public void Groundwork_replacement_failure_after_withdraw_preserves_unit_declarations()
    {
        var source = new ServiceCollection();
        source.AddGroundworkV2RuntimeStores(targetName: "runtime");
        var throwOnce = true;
        var services = new ThrowingServiceCollection(descriptor =>
            descriptor.ServiceType == typeof(RuntimeArtifactStoreBackend) &&
            descriptor.ImplementationInstance is RuntimeArtifactStoreBackend backend &&
            backend.Name == RuntimeArtifactStoreBackend.Groundwork &&
            Interlocked.Exchange(ref throwOnce, false));
        services.DisableThrowing();
        foreach (var descriptor in source)
            services.Add(descriptor);
        services.EnableThrowing();
        var before = services.ToArray();
        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        var beforeIds = registry.Registrations.Select(registration => registration.Unit.Id.Value).ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkV2RuntimeStores(targetName: "runtime"));

        Assert.Equal(before, services);
        Assert.Equal(beforeIds, registry.Registrations.Select(registration => registration.Unit.Id.Value));
    }

    [Fact]
    public void Named_public_provider_composition_resolves_cache_modes_and_atomic_durability_evidence()
    {
        using var connection = new SqliteProviderFactory().Create("Data Source=:memory:");
        var services = new ServiceCollection()
            .AddGroundworkStorageProviderConnection(connection, "runtime")
            .AddGroundworkV2RuntimeStores(targetName: "runtime");
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using (var ordinaryScope = provider.CreateScope())
        {
            var store = ordinaryScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            Assert.IsType<CachingWorkflowExecutableStore>(store);
        }

        using (var privilegedScope = provider.CreateScope())
        {
            privilegedScope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("runtime-maintenance")));
            var store = privilegedScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            Assert.IsType<InvalidatingWorkflowExecutableStore>(store);

            var checkpoint = Assert.Single(
                privilegedScope.ServiceProvider.GetServices<IWorkflowDispatchDurabilityEvidence>(),
                evidence => evidence.Component == WorkflowDispatchDurabilityComponents.Checkpoint);
            Assert.Equal(WorkflowDispatchDurabilityLevel.Durable, checkpoint.Level);
        }
    }

    private static void AssertScopedAlias<TContract, TImplementation>(IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        var implementation = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TImplementation));
        Assert.Equal(ServiceLifetime.Scoped, implementation.Lifetime);
        Assert.NotNull(implementation.ImplementationFactory);

        var contract = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TContract));
        Assert.Equal(ServiceLifetime.Scoped, contract.Lifetime);
        Assert.NotNull(contract.ImplementationFactory);
    }

    private sealed class ThrowingServiceCollection(Func<ServiceDescriptor, bool> shouldThrow) : IServiceCollection
    {
        private readonly List<ServiceDescriptor> descriptors = [];
        private bool throwing = true;

        public ServiceDescriptor this[int index] { get => descriptors[index]; set => descriptors[index] = value; }
        public int Count => descriptors.Count;
        public bool IsReadOnly => false;
        public void Add(ServiceDescriptor item)
        {
            if (throwing && shouldThrow(item))
                throw new InvalidOperationException("synthetic service registration failure");
            descriptors.Add(item);
        }
        public void Clear() => descriptors.Clear();
        public bool Contains(ServiceDescriptor item) => descriptors.Contains(item);
        public void CopyTo(ServiceDescriptor[] array, int arrayIndex) => descriptors.CopyTo(array, arrayIndex);
        public IEnumerator<ServiceDescriptor> GetEnumerator() => descriptors.GetEnumerator();
        public int IndexOf(ServiceDescriptor item) => descriptors.IndexOf(item);
        public void Insert(int index, ServiceDescriptor item)
        {
            if (throwing && shouldThrow(item))
                throw new InvalidOperationException("synthetic service registration failure");
            descriptors.Insert(index, item);
        }
        public bool Remove(ServiceDescriptor item) => descriptors.Remove(item);
        public void RemoveAt(int index) => descriptors.RemoveAt(index);
        public void DisableThrowing() => throwing = false;
        public void EnableThrowing() => throwing = true;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
