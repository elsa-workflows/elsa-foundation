using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CShells.Lifecycle;
using Elsa.Workbench;
using Elsa.Workbench.Readiness;
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
        // The route remains blocked; allow a busy CI runner time for the HTTP round trip.
        var ready = await fixture.ReadReadyAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("live", (await fixture.ReadLiveAsync()).Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("starting", ready.Body.Status);
        Assert.Equal("shell_activation_pending", ready.Body.Code);
        Assert.Null(fixture.Registry.GetActive(ServerReadinessFixture.DefaultShellName));
        Assert.Equal(1, defaultGate.Attempts);

        // Finish the deliberately blocked activation before fixture shutdown drains the shell.
        defaultGate.Release();
        await fixture.WaitUntilReadyAsync();
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
    public async Task ActiveGenerationRemainsUnavailableUntilTheWarmupSnapshotIsReady()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync(startReadinessWarmup: false);
        var defaultGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        Assert.True(fixture.ReadinessState.TryBegin(ServerReadinessFixture.DefaultShellName));

        var activation = fixture.Registry.GetOrActivateAsync(ServerReadinessFixture.DefaultShellName);
        await defaultGate.WaitUntilEnteredAsync();
        defaultGate.Release();
        var shell = await activation;

        var starting = await fixture.ReadReadyAsync();
        Assert.Equal(ShellLifecycleState.Active, shell.State);
        Assert.Equal(ShellReadinessStatus.Starting, fixture.ReadinessState.Snapshot.Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, starting.StatusCode);
        Assert.Equal("starting", starting.Body.Status);
        Assert.Null(starting.Body.DurationMs);

        fixture.ReadinessState.MarkReady(shell.Descriptor.Generation);
        var ready = await fixture.ReadReadyAsync();
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(shell.Descriptor.Generation, ready.Body.Generation);
        Assert.NotNull(ready.Body.DurationMs);
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

    [Fact]
    public async Task ReadbackAfterObserverTimeoutCannotDetermineWhetherReloadWillPromote()
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

        // Only the observer stops waiting; the host operation continues. A timeout is not a failed promotion.
        await Assert.ThrowsAsync<TimeoutException>(() => reload.WaitAsync(TimeSpan.Zero));
        var beforePromotion = await fixture.ReadReadyAsync();
        Assert.Equal(initialGeneration, beforePromotion.Body.Generation);

        reloadGate.Release();
        await reload;
        var afterPromotion = await fixture.ReadReadyAsync();
        Assert.True(afterPromotion.Body.Generation > initialGeneration);
    }

    [Fact]
    public async Task FailedReloadKeepsThePreviousGenerationReady()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync();
        var initialGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        await fixture.WaitForDefaultRouteInitializationAsync();
        initialGate.Release();
        await fixture.WaitUntilReadyAsync();
        var initial = fixture.Registry.GetActive(ServerReadinessFixture.DefaultShellName)!;

        var reloadGate = fixture.RouteInitialization.PrepareNext(ServerReadinessFixture.DefaultShellName);
        reloadGate.Failure = new InvalidOperationException("sensitive candidate detail");
        var reload = fixture.Registry.ReloadAsync(ServerReadinessFixture.DefaultShellName);
        await reloadGate.WaitUntilEnteredAsync();
        reloadGate.Release();
        await reload;

        var active = fixture.Registry.GetActive(ServerReadinessFixture.DefaultShellName);
        var ready = await fixture.ReadReadyAsync();
        Assert.Same(initial, active);
        Assert.Equal(ShellLifecycleState.Active, active!.State);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(initial.Descriptor.Generation, ready.Body.Generation);
        Assert.Equal(ShellReadinessStatus.Ready, fixture.ReadinessState.Snapshot.Status);
    }

    [Fact]
    public async Task Host_observer_reports_no_prior_active_generation_and_sanitized_initializer_failure()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync();
        var initialGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        await fixture.WaitForDefaultRouteInitializationAsync();
        var pending = await SendCompositionAsync(fixture, HttpMethod.Get, "observation");
        Assert.Null((int?)pending["activeGeneration"]);
        Assert.False((bool?)pending["ready"]);

        initialGate.Release();
        await fixture.WaitUntilReadyAsync();
        var initialGeneration = fixture.Registry.GetActive(ServerReadinessFixture.DefaultShellName)!.Descriptor.Generation;
        var reloadGate = fixture.RouteInitialization.PrepareNext(ServerReadinessFixture.DefaultShellName);
        reloadGate.Failure = new InvalidOperationException("sensitive candidate detail");
        var reload = SendCompositionAsync(fixture, HttpMethod.Post, "reload");
        await reloadGate.WaitUntilEnteredAsync();
        reloadGate.Release();
        var outcome = await reload;

        Assert.Equal("reload-failed", (string?)outcome["outcome"]);
        Assert.Equal(initialGeneration, (int?)outcome["previousGeneration"]);
        Assert.Null((int?)outcome["reportedGeneration"]);
        Assert.Equal(initialGeneration, (int?)outcome["activeGeneration"]);
        Assert.True((bool?)outcome["ready"]);
        Assert.Equal("unverified", (string?)outcome["candidateMatch"]);
        Assert.DoesNotContain("sensitive candidate detail", outcome.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_observer_timeout_readback_stays_uncertain_until_promotion()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync();
        var initialGate = fixture.RouteInitialization.For(ServerReadinessFixture.DefaultShellName);
        await fixture.WaitForDefaultRouteInitializationAsync();
        initialGate.Release();
        await fixture.WaitUntilReadyAsync();
        var initialGeneration = fixture.Registry.GetActive(ServerReadinessFixture.DefaultShellName)!.Descriptor.Generation;

        var reloadGate = fixture.RouteInitialization.PrepareNext(ServerReadinessFixture.DefaultShellName);
        var reload = SendCompositionAsync(fixture, HttpMethod.Post, "reload");
        await reloadGate.WaitUntilEnteredAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => reload.WaitAsync(TimeSpan.Zero));
        var during = await SendCompositionAsync(fixture, HttpMethod.Get, "observation");
        Assert.Equal(initialGeneration, (int?)during["activeGeneration"]);
        Assert.True((bool?)during["ready"]);
        Assert.Equal("unverified", (string?)during["candidateMatch"]);

        reloadGate.Release();
        var outcome = await reload;
        Assert.Equal("ready", (string?)outcome["outcome"]);
        Assert.True((int?)outcome["activeGeneration"] > initialGeneration);
    }

    [Fact]
    public async Task Host_observer_sanitizes_a_reload_exception_as_uncertain()
    {
        await using var fixture = await ServerReadinessFixture.StartAsync(defaultShellName: "absent");
        await fixture.WaitForStatusAsync(ShellReadinessStatus.Failed);

        var outcome = await SendCompositionAsync(fixture, HttpMethod.Post, "reload");
        Assert.Equal("reload-uncertain", (string?)outcome["outcome"]);
        Assert.Null((int?)outcome["previousGeneration"]);
        Assert.Null((int?)outcome["reportedGeneration"]);
        Assert.Null((int?)outcome["activeGeneration"]);
        Assert.False((bool?)outcome["ready"]);
        Assert.Equal("unverified", (string?)outcome["candidateMatch"]);
        Assert.DoesNotContain("Exception", outcome.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonNode> SendCompositionAsync(ServerReadinessFixture fixture, HttpMethod method, string action)
    {
        using var request = new HttpRequestMessage(method, $"/_admin/composition/default-shell/{action}");
        request.Headers.Add(ManagementApiKeyAuthentication.HeaderName, ServerReadinessFixture.ManagementKey);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }
}
