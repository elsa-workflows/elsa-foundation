using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Workbench;
using Elsa.Workbench.Composition;
using Elsa.Workbench.Readiness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class WorkbenchSettledReadinessEndpointTests
{
    [Fact]
    public async Task Active_but_unsettled_shell_is_not_reported_ready()
    {
        var oldShell = new StubShell("default", 1);
        var candidate = new StubShell("default", 2);
        var registry = new SettledShellRegistry(candidate, null);
        var state = ReadyState(oldShell.Descriptor.Generation);
        await using var app = await StartAsync(registry, state);
        using var client = app.GetTestClient();

        using var readiness = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        using var readinessBody = await readiness.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("ready", readinessBody!.RootElement.GetProperty("status").GetString());

        using var observation = await ObserveAsync(client);
        Assert.Equal(JsonValueKind.Null, observation.RootElement.GetProperty("activeGeneration").ValueKind);
        Assert.False(observation.RootElement.GetProperty("ready").GetBoolean());
    }

    [Fact]
    public async Task Registry_without_settled_observation_fails_closed()
    {
        var shell = new StubShell("default", 1);
        var registry = new RawShellRegistry(shell);
        await using var app = await StartAsync(registry, ReadyState(shell.Descriptor.Generation));
        using var client = app.GetTestClient();

        using var readiness = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);

        using var observation = await ObserveAsync(client);
        Assert.Equal(JsonValueKind.Null, observation.RootElement.GetProperty("activeGeneration").ValueKind);
        Assert.False(observation.RootElement.GetProperty("ready").GetBoolean());
    }

    [Fact]
    public async Task Readiness_tracks_settled_generation_through_candidate_rollback_and_promotion()
    {
        var previous = new StubShell("default", 1);
        var candidate = new StubShell("default", 2);
        var registry = new SettledShellRegistry(previous, previous);
        await using var app = await StartAsync(registry, ReadyState(previous.Descriptor.Generation));
        using var client = app.GetTestClient();

        registry.SetCurrent(candidate, previous);
        await AssertReadyAsync(client, previous.Descriptor.Generation);

        registry.SetCurrent(previous, previous);
        await AssertReadyAsync(client, previous.Descriptor.Generation);

        registry.SetCurrent(candidate, candidate);
        await AssertReadyAsync(client, candidate.Descriptor.Generation);
    }

    private static ShellReadinessState ReadyState(int generation)
    {
        var state = new ShellReadinessState(TimeProvider.System);
        Assert.True(state.TryBegin("default"));
        state.MarkReady(generation);
        return state;
    }

    private static async Task<WebApplication> StartAsync(IShellRegistry registry, ShellReadinessState state)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration[ManagementApiKeyAuthentication.ConfigurationKey] = "readiness-test-key";
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton<IOptions<ShellReadinessOptions>>(Options.Create(new ShellReadinessOptions
        {
            DefaultShellName = "default"
        }));

        var app = builder.Build();
        app.MapShellReadiness();
        app.MapCompositionActivationObservation();
        await app.StartAsync();
        return app;
    }

    private static async Task AssertReadyAsync(HttpClient client, int generation)
    {
        using var readiness = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        var ready = await readiness.Content.ReadFromJsonAsync<ShellReadinessReadyResponse>();
        Assert.Equal(generation, ready!.Generation);

        using var observation = await ObserveAsync(client);
        Assert.Equal(generation, observation.RootElement.GetProperty("activeGeneration").GetInt32());
        Assert.True(observation.RootElement.GetProperty("ready").GetBoolean());
    }

    private static async Task<JsonDocument> ObserveAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_admin/composition/default-shell/observation");
        request.Headers.Add(ManagementApiKeyAuthentication.HeaderName, "readiness-test-key");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
    }

    private class RawShellRegistry(IShell shell) : IShellRegistry
    {
        public virtual IShell? GetActive(string name) => string.Equals(name, shell.Descriptor.Name, StringComparison.OrdinalIgnoreCase) ? shell : null;
        public IReadOnlyCollection<IShell> GetAll(string name) => GetActive(name) is { } active ? [active] : [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [shell];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
        public Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell active, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SettledShellRegistry(IShell? active, IShell? settled) : RawShellRegistry(active ?? settled ?? new StubShell("default", 1)), ISettledShellRegistry
    {
        private IShell? _active = active;
        private IShell? _settled = settled;

        public void SetCurrent(IShell? current, IShell? settled)
        {
            _active = current;
            _settled = settled;
        }

        public override IShell? GetActive(string name) => string.Equals(name, _active?.Descriptor.Name, StringComparison.OrdinalIgnoreCase) ? _active : null;
        public IShell? GetSettledActive(string name) => string.Equals(name, _settled?.Descriptor.Name, StringComparison.OrdinalIgnoreCase) ? _settled : null;
    }

    private sealed class StubShell(string name, int generation) : IShell
    {
        public ShellDescriptor Descriptor { get; } = ShellDescriptor.Create(name, generation);
        public ShellLifecycleState State => ShellLifecycleState.Active;
        public IServiceProvider ServiceProvider => throw new NotSupportedException();
        public IShellScope BeginScope() => throw new NotSupportedException();
        public IDrainOperation? Drain => null;
    }
}