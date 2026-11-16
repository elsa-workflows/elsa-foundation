using System.Reflection;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Locking.Core;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.Integration;

/// <summary>
/// End-to-end proof that closes the one assumption the unit tests cannot: that the
/// <see cref="IFeatureAssemblyProvider"/> seam (how Nuplane surfaces dynamically-loaded extension-builder
/// package assemblies, which live in a custom PackageAssemblyLoadContext — NOT the default AppDomain) is
/// actually reachable from a composed shell's DI scope, where <c>RegisterActivityTypesStartupTask</c> runs.
///
/// It boots a REAL CShells shell (Serialization → the well-known type registry + primitive seed; Tasks → the
/// shell-tasks initializer that replays IStartupTask on activation; Events → the event bus the runtime feature
/// uses; ActivitiesRuntime → the startup pass under test), registers a stub provider that surfaces this test
/// assembly's activity, activates the shell, then asserts from the shell scope that (1) the provider is
/// resolvable there, (2) the startup pass actually consulted it, and (3) the complex/enum I/O types resolved.
/// </summary>
public sealed class ShellScopedActivityIoTypeRegistrationTests
{
    private const string DefaultShellName = "default";

    [Fact]
    public async Task FeatureAssemblyProvider_IsReachableFromShellScope_AndStartupPassRegistersItsActivityIoTypes()
    {
        var provider = new RecordingFeatureAssemblyProvider(typeof(ShellExtensionActivity).Assembly);

        var services = new ServiceCollection();
        services.AddSingleton<IFeatureAssemblyProvider>(provider);
        services.AddCShells(builder => builder
            .WithAssemblies(
                typeof(Elsa.Serialization.SystemText.SerializationFeature).Assembly,
                typeof(Elsa.Tasks.TasksFeature).Assembly,
                typeof(Elsa.Events.EventsFeature).Assembly,
                typeof(ActivitiesRuntimeFeature).Assembly,
                typeof(ShellScopedActivityIoTypeRegistrationTests).Assembly)
            .WithAssemblyProvider<IFeatureAssemblyProvider>()
            .AddShell(DefaultShellName, shell => shell
                .WithFeature<Elsa.Serialization.SystemText.SerializationFeature>()
                .WithFeature<Elsa.Tasks.TasksFeature>()
                .WithFeature<Elsa.Events.EventsFeature>()
                .WithFeature<ActivitiesRuntimeFeature>()
                .WithFeature<TestInfraFeature>()));

        await using var root = services.BuildServiceProvider();
        var registry = root.GetRequiredService<IShellRegistry>();

        var shell = await registry.GetOrActivateAsync(DefaultShellName, CancellationToken.None);
        await using var scope = shell.BeginScope();
        var shellServices = scope.ServiceProvider;

        // (1) The provider seam survives into shell scope (it is NOT among the host-only excluded services).
        Assert.Contains(provider, shellServices.GetServices<IFeatureAssemblyProvider>());

        // (2) The startup pass ran in shell scope and actually consulted the provider.
        Assert.True(provider.WasInvoked, "RegisterActivityTypesStartupTask did not consult IFeatureAssemblyProvider in shell scope.");

        // (3) End-to-end: the provider-surfaced activity's complex/enum I/O element types resolved to their real
        //     CLR types via the shell's well-known type registry — not object.
        var wellKnownTypes = shellServices.GetRequiredService<IWellKnownTypeRegistry>();
        Assert.True(wellKnownTypes.TryGetType(typeof(ShellExtensionPayload).FullName!, out var payload));
        Assert.Equal(typeof(ShellExtensionPayload), payload);
        Assert.True(wellKnownTypes.TryGetType(typeof(ShellExtensionMode).FullName!, out var mode));
        Assert.Equal(typeof(ShellExtensionMode), mode);

        // (4) A Nullable<T> input over a reserved primitive (TimeSpan?) resolves via its "T?" companion alias
        //     instead of being skipped with a ReservedAliasNamespaceException (issue #949): the startup pass
        //     unwraps Nullable<T>, registers the underlying reserved primitive, and the registry derives "TimeSpan?".
        Assert.True(wellKnownTypes.TryGetType("TimeSpan?", out var nullableDelay));
        Assert.Equal(typeof(TimeSpan?), nullableDelay);
        Assert.True(wellKnownTypes.TryGetAlias(typeof(TimeSpan?), out var nullableDelayAlias));
        Assert.Equal("TimeSpan?", nullableDelayAlias);
    }

    private sealed class RecordingFeatureAssemblyProvider(params Assembly[] assemblies) : IFeatureAssemblyProvider
    {
        public bool WasInvoked { get; private set; }

        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return Task.FromResult(assemblies.AsEnumerable());
        }
    }
}

/// <summary>
/// Supplies the one infrastructure dependency the Tasks feature's executor requires (a distributed lock
/// provider) so the shell composes. The startup pass under test never acquires a lock, so a throwing no-op
/// is sufficient and keeps the shell free of file-system locking machinery.
/// </summary>
[ShellFeature(name: "TestInfra", DisplayName = "Test Infrastructure", Description = "No-op infra for shell composition in tests.")]
public sealed class TestInfraFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IDistributedLockProvider, NoopDistributedLockProvider>();

    private sealed class NoopDistributedLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

/// <summary>Stands in for a dynamically-loaded extension-builder activity surfaced via IFeatureAssemblyProvider.</summary>
public sealed class ShellExtensionActivity : Activity<ActivityUnit>
{
    [ActivityInput]
    public ShellExtensionPayload Payload { get; set; } = null!;

    [ActivityInput]
    public ShellExtensionMode Mode { get; set; }

    [ActivityInput]
    public TimeSpan? Delay { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

public sealed class ShellExtensionPayload
{
    public string Name { get; set; } = string.Empty;
}

public enum ShellExtensionMode
{
    Off,
    On
}
