using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Foundation.Host.ModuleManagement;
using Elsa.Persistence.Schema;
using Elsa.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Admin;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// <c>POST /_module-management/reload</c> on <c>Elsa.Foundation.Host</c>: CShells reports a shell it could not activate in its
/// <see cref="ReloadResult"/> and keeps the previous generation serving, so the endpoint has to read the results rather than
/// count them. The refusal here is a type this assembly's host never references, as an EF module package's is: it is
/// recognised by <see cref="IEfModuleRefusal"/> alone.
/// </summary>
public sealed class FoundationHostReloadEndpointTests : IAsyncLifetime
{
    private const string ApiKey = "expected-key";
    private readonly ScriptedShellRegistry _registry = new();
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IShellRegistry>(_registry);
        builder.Services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog());
        builder.Services.AddSingleton<INuplaneAdminOperations>(new FakeNuplaneAdminOperations());
        _app = builder.Build();
        _app.MapModuleManagementApi(new ModuleManagementOptions { Enabled = true, ApiKey = ApiKey });
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add(ModuleManagementOptions.ApiKeyHeader, ApiKey);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    private async Task<HttpResponseMessage> ReloadAsync() => await _client!.PostAsync("/_module-management/reload", content: null);

    [Fact]
    public async Task Answers_200_when_every_shell_reloaded()
    {
        _registry.Results = [new ReloadResult("default", null, null, null), new ReloadResult("tenant-a", null, null, null)];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("reloaded").GetInt32());
    }

    [Fact]
    public async Task Answers_a_409_naming_the_shell_the_module_the_migrations_and_the_command_when_a_module_refuses()
    {
        _registry.Results =
        [
            new ReloadResult("tenant-a", null, null, null),
            // Wrapped, as an initializer's failure can arrive out of a shell's activation.
            new ReloadResult("default", null, null, new InvalidOperationException("Shell 'default' failed to activate.", new PendingRefusal()))
        ];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        var detail = body.GetProperty("detail").GetString();
        Assert.Contains("Shell 'default'", detail, StringComparison.Ordinal);
        Assert.Contains(PendingRefusal.Text, detail, StringComparison.Ordinal);
        Assert.Equal(1, body.GetProperty("reloaded").GetInt32());
        var shell = Assert.Single(body.GetProperty("shells").EnumerateArray());
        Assert.Equal("default", shell.GetProperty("shell").GetString());
        Assert.Equal("Orders", shell.GetProperty("module").GetString());
        Assert.Equal(IEfModuleRefusal.PendingMigrationsCode, shell.GetProperty("code").GetString());
        Assert.Equal(["20260930_One", "20260930_Two"], shell.GetProperty("pendingMigrations").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(PendingRefusal.ApplyCommand, shell.GetProperty("command").GetString());
    }

    [Fact]
    public async Task Answers_a_409_for_any_other_failed_shell_without_carrying_its_message()
    {
        _registry.Results = [new ReloadResult("default", null, null, new InvalidOperationException("Host=db;Password=hunter2 refused"))];

        using var response = await ReloadAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(nameof(InvalidOperationException), text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        var shell = Assert.Single(JsonDocument.Parse(text).RootElement.GetProperty("shells").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, shell.GetProperty("code").ValueKind);
    }

    /// <summary>An EF module's refusal as this host meets it: a type of the module's own, known here only by the shared interface.</summary>
    private sealed class PendingRefusal() : InvalidOperationException(Text), IEfModuleRefusal
    {
        public const string ApplyCommand = "dotnet elsa persistence apply --host <path> --modules Orders --provider Sqlite --connection-env ELSA_EF_CONNECTION";
        public const string Text = "EF module 'Orders' has pending migrations: 20260930_One, 20260930_Two.";

        public string Module => "Orders";

        public string Code => IEfModuleRefusal.PendingMigrationsCode;

        public IReadOnlyList<string> PendingMigrations => ["20260930_One", "20260930_Two"];

        public string? Command => ApplyCommand;
    }

    /// <summary>Answers <see cref="ReloadActiveAsync"/> with what a test scripts; nothing else of the registry is reached.</summary>
    private sealed class ScriptedShellRegistry : IShellRegistry
    {
        public IReadOnlyList<ReloadResult> Results { get; set; } = [];

        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(Results);

        public Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IShell? GetActive(string name) => null;
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }
}
