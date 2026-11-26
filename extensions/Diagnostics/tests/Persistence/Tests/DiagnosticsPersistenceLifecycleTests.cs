using CShells.Lifecycle;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsPersistenceLifecycleTests
{
    [Fact]
    public async Task Registration_uses_one_hosted_coordinator_and_the_selected_singleton_drain_instances()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FirstDrain>();
        services.AddSingleton<SecondDrain>();
        services.AddDiagnosticsPersistenceLifecycle<FirstDrain>();
        services.AddDiagnosticsPersistenceLifecycle<FirstDrain>();
        services.AddDiagnosticsPersistenceLifecycle<SecondDrain>();

        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        Assert.Same(hosted, Assert.Single(provider.GetServices<IShellInitializer>()));
        Assert.Same(hosted, Assert.Single(provider.GetServices<IShellTerminator>()));
        var first = provider.GetRequiredService<FirstDrain>();
        var second = provider.GetRequiredService<SecondDrain>();
        Assert.Same(first, Assert.IsType<FirstDrain>(
            Assert.Single(provider.GetServices<IDiagnosticsPersistenceDrain>(), x => x is FirstDrain)));
        Assert.Same(second, Assert.IsType<SecondDrain>(
            Assert.Single(provider.GetServices<IDiagnosticsPersistenceDrain>(), x => x is SecondDrain)));
        Assert.Equal((0, 0), (first.StartCount, second.StartCount));

        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal((1, 1), (first.StartCount, second.StartCount));
        Assert.Equal((1, 1), (first.StopCount, second.StopCount));
    }

    [Fact]
    public async Task Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FirstDrain>();
        services.AddDiagnosticsPersistenceLifecycle<FirstDrain>();

        var initializerRegistration = Assert.Single(services
            .Where(descriptor => descriptor.ServiceType == typeof(ShellInitializerRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ShellInitializerRegistration>());
        var terminatorRegistration = Assert.Single(services
            .Where(descriptor => descriptor.ServiceType == typeof(ShellTerminatorRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ShellTerminatorRegistration>());
        Assert.Equal(LifecyclePhase.Default, initializerRegistration.Phase);
        Assert.Equal(LifecyclePhase.Default, terminatorRegistration.Phase);
        Assert.Equal(initializerRegistration.InitializerType, terminatorRegistration.TerminatorType);

        await using var provider = services.BuildServiceProvider();
        var initializer = Assert.Single(provider.GetServices<IShellInitializer>());
        var terminator = Assert.Single(provider.GetServices<IShellTerminator>());
        var drain = provider.GetRequiredService<FirstDrain>();

        await initializer.InitializeAsync(CancellationToken.None);
        await terminator.TerminateAsync(CancellationToken.None);

        Assert.Equal(1, drain.StartCount);
        Assert.Equal(1, drain.StopCount);
    }

    [Fact]
    public void Registration_rejects_missing_or_non_singleton_concrete_drains()
    {
        var missing = new ServiceCollection();
        var scoped = new ServiceCollection();
        scoped.AddScoped<FirstDrain>();

        Assert.Throws<InvalidOperationException>(
            () => missing.AddDiagnosticsPersistenceLifecycle<FirstDrain>());
        Assert.Throws<InvalidOperationException>(
            () => scoped.AddDiagnosticsPersistenceLifecycle<FirstDrain>());
    }

    [Fact]
    public async Task Host_stop_awaits_every_drain_and_does_not_abandon_them_when_host_cancellation_fires()
    {
        var services = new ServiceCollection();
        services.AddSingleton<BlockingDrain>();
        services.AddDiagnosticsPersistenceLifecycle<BlockingDrain>();
        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var drain = provider.GetRequiredService<BlockingDrain>();
        await hosted.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var stop = hosted.StopAsync(cancellation.Token);
        await drain.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(drain.StopToken.CanBeCanceled);
        Assert.False(stop.IsCompleted);

        drain.ReleaseStop();
        await stop;
        Assert.Equal(1, drain.StopCount);
    }

    [Fact]
    public async Task Coordinator_stop_completes_before_provider_owner_disposal()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ProviderOwner>();
        services.AddSingleton<OwnerBackedDrain>();
        services.AddDiagnosticsPersistenceLifecycle<OwnerBackedDrain>();
        var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var owner = provider.GetRequiredService<ProviderOwner>();
        await hosted.StartAsync(CancellationToken.None);

        await hosted.StopAsync(CancellationToken.None);
        await provider.DisposeAsync();

        Assert.True(owner.Disposed);
        Assert.True(owner.DrainStoppedBeforeDisposal);
    }

    [Fact]
    public async Task Startup_resources_are_acquired_before_drains_and_released_after_every_drain_stops()
    {
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(events);
        services.AddSingleton<ResourceBackedDrain>();
        services.AddSingleton<SecondRecordingDrain>();
        services.AddDiagnosticsPersistenceLifecycle<ResourceBackedDrain>();
        services.AddDiagnosticsPersistenceLifecycle<SecondRecordingDrain>();
        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());

        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(
            ["resource.acquire", "resource.start", "second.start", "second.stop", "resource.stop", "resource.release"],
            events);
    }

    [Fact]
    public async Task Startup_failure_releases_prior_leases_preserves_the_exact_failure_and_starts_no_drains()
    {
        var failure = new InvalidOperationException("schema drift");
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(events);
        services.AddSingleton<SuccessfulResourceDrain>();
        services.AddSingleton(new FailingResourceDrain(events, failure));
        services.AddDiagnosticsPersistenceLifecycle<SuccessfulResourceDrain>();
        services.AddDiagnosticsPersistenceLifecycle<FailingResourceDrain>();
        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => hosted.StartAsync(CancellationToken.None));

        Assert.Same(failure, actual);
        Assert.Equal(["successful.acquire", "failing.acquire", "successful.release"], events);
        Assert.Equal(0, provider.GetRequiredService<SuccessfulResourceDrain>().StartCount);
        Assert.Equal(0, provider.GetRequiredService<FailingResourceDrain>().StartCount);

        var repeatedHostFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => hosted.StartAsync(CancellationToken.None));
        var initializer = Assert.Single(provider.GetServices<IShellInitializer>());
        var repeatedShellFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.InitializeAsync(CancellationToken.None));

        Assert.Same(failure, repeatedHostFailure);
        Assert.Same(failure, repeatedShellFailure);
        Assert.Equal(["successful.acquire", "failing.acquire", "successful.release"], events);
        await hosted.StopAsync(CancellationToken.None);
    }

    private class FirstDrain : RecordingDrain;
    private sealed class SecondDrain : RecordingDrain;

    private class RecordingDrain : IDiagnosticsPersistenceDrain
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public void Start() => StartCount++;

        public virtual Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDrain : RecordingDrain
    {
        private readonly TaskCompletionSource _releaseStop =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken StopToken { get; private set; }

        public override async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopToken = cancellationToken;
            StopEntered.TrySetResult();
            await _releaseStop.Task;
            await base.StopAsync(cancellationToken);
        }

        public void ReleaseStop() => _releaseStop.TrySetResult();
    }

    private sealed class OwnerBackedDrain(ProviderOwner owner) : RecordingDrain
    {
        public override async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await base.StopAsync(cancellationToken);
            owner.MarkDrainStopped();
        }
    }

    private sealed class ResourceBackedDrain(List<string> events) :
        IDiagnosticsPersistenceDrain,
        IDiagnosticsPersistenceStartupResource
    {
        public ValueTask<IDiagnosticsPersistenceResourceLease> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("resource.acquire");
            return ValueTask.FromResult<IDiagnosticsPersistenceResourceLease>(
                new RecordingLease(events, "resource.release"));
        }

        public void Start() => events.Add("resource.start");

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            events.Add("resource.stop");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondRecordingDrain(List<string> events) : IDiagnosticsPersistenceDrain
    {
        public void Start() => events.Add("second.start");

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            events.Add("second.stop");
            return Task.CompletedTask;
        }
    }

    private sealed class SuccessfulResourceDrain(List<string> events) :
        RecordingDrain,
        IDiagnosticsPersistenceStartupResource
    {
        public ValueTask<IDiagnosticsPersistenceResourceLease> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            events.Add("successful.acquire");
            return ValueTask.FromResult<IDiagnosticsPersistenceResourceLease>(
                new RecordingLease(events, "successful.release"));
        }
    }

    private sealed class FailingResourceDrain(List<string> events, Exception failure) :
        RecordingDrain,
        IDiagnosticsPersistenceStartupResource
    {
        public ValueTask<IDiagnosticsPersistenceResourceLease> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            events.Add("failing.acquire");
            return ValueTask.FromException<IDiagnosticsPersistenceResourceLease>(failure);
        }
    }

    private sealed class RecordingLease(List<string> events, string releaseEvent) :
        IDiagnosticsPersistenceResourceLease
    {
        public ValueTask DisposeAsync()
        {
            events.Add(releaseEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProviderOwner : IAsyncDisposable
    {
        private bool _drainStopped;

        public bool Disposed { get; private set; }
        public bool DrainStoppedBeforeDisposal { get; private set; }

        public void MarkDrainStopped() => _drainStopped = true;

        public ValueTask DisposeAsync()
        {
            DrainStoppedBeforeDisposal = _drainStopped;
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
