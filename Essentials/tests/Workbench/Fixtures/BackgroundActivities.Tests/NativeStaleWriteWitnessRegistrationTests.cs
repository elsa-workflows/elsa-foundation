using Elsa.Workbench.Tests.BackgroundActivities;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench.Tests.BackgroundActivities.Tests;

public class NativeStaleWriteWitnessRegistrationTests
{
    [Theory]
    [InlineData("missing-backend", "requires a selected runtime checkpoint backend")]
    [InlineData("wrong-backend", "requires the EF runtime checkpoint backend")]
    [InlineData("missing-outer", "requires exactly one scoped IRuntimeCheckpointCommitStore registration")]
    [InlineData("singleton-outer", "requires exactly one scoped IRuntimeCheckpointCommitStore registration")]
    [InlineData("transient-outer", "requires exactly one scoped IRuntimeCheckpointCommitStore registration")]
    [InlineData("duplicate-outer", "requires exactly one scoped IRuntimeCheckpointCommitStore registration")]
    [InlineData("missing-holder", "requires exactly one scoped CoalescingInner`1 registration")]
    [InlineData("singleton-holder", "requires exactly one scoped CoalescingInner`1 registration")]
    [InlineData("transient-holder", "requires exactly one scoped CoalescingInner`1 registration")]
    [InlineData("duplicate-holder", "requires exactly one scoped CoalescingInner`1 registration")]
    public void Register_fails_fast_for_missing_or_incorrect_checkpoint_registration(
        string scenario,
        string expectedMessage)
    {
        var services = CreateServices();
        ApplyInvalidRegistration(services, scenario);

        var exception = Assert.Throws<InvalidOperationException>(() => NativeStaleWriteWitnessRegistration.Register(services));

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void Resolved_public_checkpoint_store_must_be_the_actual_API_coalescer()
    {
        var services = CreateServices();
        ReplacePublicCheckpointStore(services, _ => new StubCheckpointStore());
        NativeStaleWriteWitnessRegistration.Register(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>());

        Assert.Contains("requires the actual API CoalescingRuntimeCheckpointCommitStore", exception.Message);
    }

    [Fact]
    public void Resolved_coalescing_holder_must_retain_the_actual_EF_checkpoint_store()
    {
        var disposed = 0;
        var services = CreateServices(
            _ => new CountingCheckpointStore(() => Interlocked.Increment(ref disposed)));
        NativeStaleWriteWitnessRegistration.Register(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>());

        Assert.Contains("expected CoalescingInner to retain the real EF checkpoint store", exception.Message);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task Captured_sync_holder_is_disposed_exactly_once_by_the_store_witness()
    {
        var syncDisposals = 0;
        var asyncDisposals = 0;
        var services = CreateServices(_ => new CountingCheckpointStore(
            () => Interlocked.Increment(ref syncDisposals),
            () =>
            {
                Interlocked.Increment(ref asyncDisposals);
                return ValueTask.CompletedTask;
            }));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var holder = CreateOwnedHolderFromPersistenceRegistration(services, scope.ServiceProvider);
        var witness = new NativeStaleWriteEfCheckpointStoreWitness(holder, null!, null!);

        witness.Dispose();
        witness.Dispose();
        await witness.DisposeAsync();

        Assert.Equal(1, syncDisposals);
        Assert.Equal(0, asyncDisposals);
    }

    [Fact]
    public async Task Captured_async_holder_is_disposed_exactly_once_by_the_store_witness()
    {
        var syncDisposals = 0;
        var asyncDisposals = 0;
        var services = CreateServices(_ => new CountingCheckpointStore(
            () => Interlocked.Increment(ref syncDisposals),
            () =>
            {
                Interlocked.Increment(ref asyncDisposals);
                return ValueTask.CompletedTask;
            }));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var holder = CreateOwnedHolderFromPersistenceRegistration(services, scope.ServiceProvider);
        var witness = new NativeStaleWriteEfCheckpointStoreWitness(holder, null!, null!);

        await witness.DisposeAsync();
        await witness.DisposeAsync();
        witness.Dispose();

        Assert.Equal(0, syncDisposals);
        Assert.Equal(1, asyncDisposals);
    }

    private static IServiceCollection CreateServices(
        Func<IServiceProvider, IRuntimeCheckpointCommitStore>? checkpointStoreFactory = null)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddWorkflowRuntime();
        if (checkpointStoreFactory is not null)
        {
            var original = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
            services.Remove(original);
            services.AddScoped(checkpointStoreFactory);
        }

        services.AddPersistenceModes();
        SelectEfBackend(services);

        return services;
    }

    private static void SelectEfBackend(IServiceCollection services)
    {
        // The in-process composition gives these focused guard tests the selected-backend marker needed to reach
        // later checks. The fake inner store is never treated as EF: the exact-type test below requires it to fail.
        var checkpointStore = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        RuntimeCheckpointCommitStoreBackend.Register(services,
            new RuntimeCheckpointCommitStoreBackend(RuntimeCheckpointCommitStoreBackend.EntityFramework, checkpointStore, checkpointStore));
    }

    private static void ReplacePublicCheckpointStore(
        IServiceCollection services,
        Func<IServiceProvider, IRuntimeCheckpointCommitStore> factory)
    {
        services.Remove(services.Single(descriptor => descriptor.ImplementationInstance is RuntimeCheckpointCommitStoreBackend));
        var current = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        services.Remove(current);
        var replacement = ServiceDescriptor.Scoped<IRuntimeCheckpointCommitStore>(factory);
        services.Add(replacement);
        RuntimeCheckpointCommitStoreBackend.Register(services,
            new RuntimeCheckpointCommitStoreBackend(RuntimeCheckpointCommitStoreBackend.EntityFramework, replacement, replacement));
    }

    private static CoalescingInner<IRuntimeCheckpointCommitStore> CreateOwnedHolderFromPersistenceRegistration(
        IServiceCollection services,
        IServiceProvider provider)
    {
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(CoalescingInner<IRuntimeCheckpointCommitStore>));
        // Invoke the public persistence-mode descriptor factory directly so its ownership semantics are under test,
        // without also asking the scope to dispose the same holder after the witness does.
        var factory = Assert.IsType<Func<IServiceProvider, object>>(descriptor.ImplementationFactory);
        return Assert.IsType<CoalescingInner<IRuntimeCheckpointCommitStore>>(factory(provider));
    }

    private static void ApplyInvalidRegistration(IServiceCollection services, string scenario)
    {
        switch (scenario)
        {
            case "missing-backend":
                services.Remove(services.Single(descriptor => descriptor.ImplementationInstance is RuntimeCheckpointCommitStoreBackend));
                break;
            case "wrong-backend":
                services.Remove(services.Single(descriptor => descriptor.ImplementationInstance is RuntimeCheckpointCommitStoreBackend));
                var contract = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
                RuntimeCheckpointCommitStoreBackend.Register(services,
                    new RuntimeCheckpointCommitStoreBackend(RuntimeCheckpointCommitStoreBackend.InMemory, contract, contract));
                break;
            case "missing-outer":
                services.Remove(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore)));
                break;
            case "singleton-outer":
            case "transient-outer":
                ReplaceLifetime<IRuntimeCheckpointCommitStore>(services,
                    scenario == "singleton-outer" ? ServiceLifetime.Singleton : ServiceLifetime.Transient,
                    _ => new StubCheckpointStore());
                break;
            case "duplicate-outer":
                services.AddScoped<IRuntimeCheckpointCommitStore>(_ => new StubCheckpointStore());
                break;
            case "missing-holder":
                services.Remove(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CoalescingInner<IRuntimeCheckpointCommitStore>)));
                break;
            case "singleton-holder":
            case "transient-holder":
                ReplaceLifetime<CoalescingInner<IRuntimeCheckpointCommitStore>>(services,
                    scenario == "singleton-holder" ? ServiceLifetime.Singleton : ServiceLifetime.Transient,
                    _ => new CoalescingInner<IRuntimeCheckpointCommitStore>(new StubCheckpointStore()));
                break;
            case "duplicate-holder":
                services.AddScoped<CoalescingInner<IRuntimeCheckpointCommitStore>>(_ =>
                    new CoalescingInner<IRuntimeCheckpointCommitStore>(new StubCheckpointStore()));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown stale-witness registration test scenario.");
        }
    }

    private static void ReplaceLifetime<TService>(
        IServiceCollection services,
        ServiceLifetime lifetime,
        Func<IServiceProvider, TService> factory)
        where TService : class
    {
        services.Remove(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService)));
        services.Add(ServiceDescriptor.Describe(typeof(TService), factory, lifetime));
    }

    private class StubCheckpointStore : IRuntimeCheckpointCommitStore
    {
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The registration test does not invoke checkpoint commits.");
    }

    private sealed class CountingCheckpointStore(
        Action dispose,
        Func<ValueTask>? disposeAsync = null) : StubCheckpointStore(), IDisposable, IAsyncDisposable
    {
        public void Dispose() => dispose();

        public ValueTask DisposeAsync() => disposeAsync?.Invoke() ?? ValueTask.CompletedTask;
    }
}
