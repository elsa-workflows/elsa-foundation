using System.Net;
using CShells.Lifecycle;
using Elsa.Server.Readiness;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ServerReadinessTests
{
    [Fact]
    public async Task ListeningProcessReportsLiveWhileDefaultShellReadinessIsImmediateUnavailable()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync();
        var defaultGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        await fixture.WaitForDefaultRouteInitializationAsync();

        using var live = await fixture.Client.GetAsync(ServerReadinessFixture.LivePath);
        var ready = await fixture.ReadReadyAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("live", (await fixture.ReadLiveAsync()).Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("starting", ready.Body.Status);
        Assert.Equal("shell_activation_pending", ready.Body.Code);
        Assert.Null(fixture.Registry.GetActive(ServerReadinessFixture.DefaultShellName));
        Assert.Equal(1, defaultGate.Attempts);
    }

    [Fact]
    public async Task ReadinessSucceedsOnlyAfterRouteInitializationAndActiveGenerationPublication()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync();
        var defaultGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        await fixture.WaitForDefaultRouteInitializationAsync();
        Assert.False(defaultGate.Initialized);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await fixture.ReadReadyAsync()).StatusCode);

        defaultGate.Release();
        await fixture.WaitUntilReadyAsync();

        var active = fixture.Registry.GetActive(ServerReadinessFixture.DefaultShellName);
        var ready = await fixture.ReadReadyAsync();
        Assert.NotNull(active);
        Assert.Equal(ShellLifecycleState.Active, active!.State);
        Assert.True(defaultGate.Initialized);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("ready", ready.Body.Status);
        Assert.Equal(ServerReadinessFixture.DefaultShellName, ready.Body.Shell);
        Assert.Equal(active.Descriptor.Generation, ready.Body.Generation);
        Assert.NotNull(ready.Body.DurationMs);
        Assert.Equal(ShellReadinessStatus.Ready, fixture.ReadinessState.Snapshot.Status);
    }

    [Fact]
    public async Task ActivationFailureLeavesReadinessUnavailableAndLivenessHealthy()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync();
        var defaultGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        defaultGate.Failure = new InvalidOperationException("sensitive connection string must not escape");
        await fixture.WaitForDefaultRouteInitializationAsync();
        defaultGate.Release();
        await fixture.WaitForStatusAsync(ShellReadinessStatus.Failed);

        using var live = await fixture.Client.GetAsync(ServerReadinessFixture.LivePath);
        var ready = await fixture.ReadReadyAsync();

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("failed", ready.Body.Status);
        Assert.Equal("shell_activation_failed", ready.Body.Code);
        Assert.DoesNotContain("connection", ready.Body.Code, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Registry.GetActive(ServerReadinessFixture.DefaultShellName));
    }

    [Fact]
    public async Task ConcurrentReadinessProbesDoNotCreateAnActivationStampede()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync();
        var defaultGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        await fixture.WaitForDefaultRouteInitializationAsync();

        var probes = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => fixture.ReadReadyAsync()));

        Assert.All(probes, probe => Assert.Equal(HttpStatusCode.ServiceUnavailable, probe.StatusCode));
        Assert.All(probes, probe => Assert.Equal("shell_activation_pending", probe.Body.Code));
        Assert.Equal(1, defaultGate.Attempts);

        defaultGate.Release();
        await fixture.WaitUntilReadyAsync();
        Assert.Equal(1, defaultGate.Attempts);
    }

    [Fact]
    public async Task DefaultShellWarmupDoesNotActivateOrShareStateWithAnotherShell()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync();
        var defaultGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        var otherGate = fixture.RouteInitialization.For(ServerReadinessFixture.OtherShellName);
        await fixture.WaitForDefaultRouteInitializationAsync();
        defaultGate.Release();
        await fixture.WaitUntilReadyAsync();

        Assert.Equal(1, defaultGate.Attempts);
        Assert.True(defaultGate.Initialized);
        Assert.Equal(0, otherGate.Attempts);
        Assert.False(otherGate.Initialized);
        Assert.Null(fixture.Registry.GetActive(ServerReadinessFixture.OtherShellName));
        Assert.Equal(ServerReadinessFixture.DefaultShellName, fixture.ReadinessState.Snapshot.ShellName);
    }

    [Fact]
    public async Task DisabledWarmupRemainsObservationalAndRecognizesExternalActivation()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync(warmDefaultShell: false);
        var defaultGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        var unavailable = await fixture.ReadReadyAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("disabled", unavailable.Body.Status);
        Assert.Equal(0, defaultGate.Attempts);

        var activation = fixture.Registry.GetOrActivateAsync(ServerReadinessFixture.DefaultShellName);
        await defaultGate.WaitUntilEnteredAsync();
        defaultGate.Release();
        var shell = await activation;

        var ready = await fixture.ReadReadyAsync();
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(shell.Descriptor.Generation, ready.Body.Generation);
        Assert.Equal(ShellReadinessStatus.Disabled, fixture.ReadinessState.Snapshot.Status);
    }

    [Fact]
    public async Task ReadinessKeepsServingTheActiveGenerationUntilReloadPromotion()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync();
        var initialGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        await fixture.WaitForDefaultRouteInitializationAsync();
        initialGate.Release();
        await fixture.WaitUntilReadyAsync();
        var initialGeneration = fixture.Registry.GetActive(ServerReadinessFixture.DefaultShellName)!.Descriptor.Generation;

        var reloadGate = fixture.RouteInitialization.PrepareNext(ServerReadinessFixture.DefaultShellName);
        var reload = fixture.Registry.ReloadAsync(ServerReadinessFixture.DefaultShellName);
        await reloadGate.WaitUntilEnteredAsync();

        var duringReload = await fixture.ReadReadyAsync();
        Assert.Equal(HttpStatusCode.OK, duringReload.StatusCode);
        Assert.Equal(initialGeneration, duringReload.Body.Generation);

        reloadGate.Release();
        await reload;
        var afterReload = await fixture.ReadReadyAsync();
        Assert.Equal(HttpStatusCode.OK, afterReload.StatusCode);
        Assert.True(afterReload.Body.Generation > initialGeneration);
        Assert.Null(afterReload.Body.DurationMs);
    }
}
