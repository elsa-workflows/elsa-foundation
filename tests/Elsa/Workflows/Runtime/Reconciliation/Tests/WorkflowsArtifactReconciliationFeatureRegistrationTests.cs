using System.Text.Json;
using Elsa.Locking.Core;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Options;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Elsa.Workflows.Runtime.Reconciliation.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// Framework §2.23.1 registration coverage for the abstract <see cref="WorkflowsArtifactReconciliationFeature"/>,
/// exercised through a test double because the base carries no <c>[ShellFeature]</c> attribute and is never
/// composable on its own.
/// </summary>
/// <remarks>
/// Every assertion resolves the service rather than inspecting the descriptor list: a descriptor proves the
/// registration was written, only a resolution proves the object graph behind it can actually be constructed in a
/// runtime-only composition. That distinction is what caught the hasher regression recorded in this feature's
/// baselines.
/// </remarks>
public sealed class WorkflowsArtifactReconciliationFeatureRegistrationTests
{
    [Fact]
    public void Base_feature_registers_a_resolvable_reconciler()
    {
        using var provider = Build(new TestArtifactReconciliationFeature());

        using var scope = provider.CreateScope();
        Assert.IsType<WorkflowArtifactReconciler>(scope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>());
    }

    [Fact]
    public void A_host_registered_reconciler_survives_the_feature()
    {
        // §2.6.2: a single-implementation contract must not be settled by silent last-write-wins. TryAdd makes
        // replacement FIRST-wins (ADR 0033) — the same gesture as the runtime's IRuntimeRequirementChecker,
        // IWorkflowExecutableHasher and IWorkflowActivationAuthority — so a host registers its own before
        // composing the feature. Under the previous plain AddScoped this contract was the lone inverse, and an
        // implementer following the established pattern silently got the default.
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowArtifactReconciler, HostReconciler>();

        new TestArtifactReconciliationFeature().ConfigureServices(services);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IWorkflowArtifactReconciler));
        Assert.Equal(typeof(HostReconciler), descriptor.ImplementationType);
    }

    [Fact]
    public void Base_feature_registers_a_resolvable_startup_task()
    {
        using var provider = Build(new TestArtifactReconciliationFeature());

        using var scope = provider.CreateScope();
        var tasks = scope.ServiceProvider.GetServices<IStartupTask>().ToArray();

        Assert.Single(tasks, task => task is WorkflowArtifactReconcilerStartupTask);
    }

    [Fact]
    public void Base_feature_registers_its_startup_task_options()
    {
        using var provider = Build(new TestArtifactReconciliationFeature
        {
            StartupTaskOptions = { LockTimeoutMs = 1234 },
        });

        var options = provider.GetRequiredService<IOptions<WorkflowArtifactReconcilerStartupTaskOptions>>();

        Assert.Equal(1234, options.Value.LockTimeoutMs);
    }

    [Fact]
    public void Base_feature_arms_the_runtime_execution_spine()
    {
        // The base calls AddWorkflowRuntime() so a runtime-only engine can actually run what it imports. These are
        // the collaborators the import path itself needs; if the call were dropped, the reconciler would still
        // register and would fail to construct at first use instead of at composition.
        using var provider = Build(new TestArtifactReconciliationFeature());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableHasher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowActivationAuthority>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>());
    }

    [Fact]
    public void Sources_set_on_the_feature_register_as_singletons()
    {
        var source = new StubSource("mounted");
        using var provider = Build(new TestArtifactReconciliationFeature { Sources = [source] });

        var registered = provider.GetServices<IWorkflowArtifactReconciliationSource>().ToArray();

        Assert.Same(source, Assert.Single(registered));
    }

    [Fact]
    public void Base_feature_is_public_and_open_for_inheritance()
    {
        // §2.23.3 / §2.5: sealing the base would amputate the only sanctioned cross-feature coupling pattern —
        // there would be no way to add a blob or OCI source variant.
        var type = typeof(WorkflowsArtifactReconciliationFeature);

        Assert.True(type.IsPublic);
        Assert.True(type.IsAbstract);
        Assert.False(type.IsSealed);
    }

    [Fact]
    public void Base_feature_carries_no_shell_feature_attribute()
    {
        // Arming the lifecycle without a source would run an empty pass on every boot, so the base must not be
        // composable on its own.
        Assert.Empty(typeof(WorkflowsArtifactReconciliationFeature)
            .GetCustomAttributes(typeof(CShells.Features.ShellFeatureAttribute), inherit: false));
    }

    [Fact]
    public void ConfigureServices_is_virtual_so_a_variant_can_extend_it()
    {
        var method = typeof(WorkflowsArtifactReconciliationFeature).GetMethod(
            nameof(WorkflowsArtifactReconciliationFeature.ConfigureServices));

        Assert.NotNull(method);
        Assert.True(method!.IsVirtual);
        Assert.False(method.IsFinal);
    }

    internal static ServiceProvider Build(WorkflowsArtifactReconciliationFeature feature)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        // Contributed by the composed host rather than by AddWorkflowRuntime(): the trigger spine comes from
        // WorkflowsRuntimeTriggers, the payload serializer and well-known type registry from the Serialization
        // feature, and the lock provider from whichever locking feature the shell composes. Stubbing them here is
        // what makes the assertions below about *this* feature's registrations rather than about the shell's.
        services.AddSingleton<IWorkflowTriggerBindingExtractor, StubTriggerBindingExtractor>();
        services.AddSingleton<IPayloadSerializer, StubPayloadSerializer>();
        services.AddSingleton<IWellKnownTypeRegistry, WellKnownTypeRegistry>();
        services.AddSingleton<IDistributedLockProvider, StubDistributedLockProvider>();

        feature.ConfigureServices(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class TestArtifactReconciliationFeature : WorkflowsArtifactReconciliationFeature;

    private sealed class HostReconciler : IWorkflowArtifactReconciler
    {
        public ValueTask<Core.Models.WorkflowArtifactReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubSource(string id) : IWorkflowArtifactReconciliationSource
    {
        public string SourceId { get; } = id;
        public string SourceKind => "Stub";

        public async IAsyncEnumerable<WorkflowArtifactClosureFile> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubTriggerBindingExtractor : IWorkflowTriggerBindingExtractor
    {
        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable) => [];
    }

    private sealed class StubPayloadSerializer : IPayloadSerializer
    {
        private const string Message = "Registration test: the serializer should not have been called.";
        public string Serialize(object payload) => throw new NotSupportedException(Message);
        public JsonElement SerializeToElement(object payload) => throw new NotSupportedException(Message);
        public object Deserialize(string serializedData) => throw new NotSupportedException(Message);
        public object Deserialize(string serializedData, Type type) => throw new NotSupportedException(Message);
        public object Deserialize(JsonElement serializedData) => throw new NotSupportedException(Message);
        public T Deserialize<T>(string serializedData) => throw new NotSupportedException(Message);
        public T Deserialize<T>(JsonElement serializedData) => throw new NotSupportedException(Message);
        public JsonSerializerOptions GetOptions() => throw new NotSupportedException(Message);
    }

    internal sealed class StubDistributedLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            new StubHandle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new StubHandle());

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new StubHandle());

        private sealed class StubHandle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
