using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using CShells.Resolution;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CShells;
using CShells.AspNetCore.Features;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Studio.Core.Attributes;
using Elsa.Studio.Core.Events;
using Elsa.Studio.Hosting;
using Elsa.Studio.Api.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Tests.Hosting;

public sealed class StudioHostingTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("read", HttpStatusCode.Forbidden)]
    [InlineData("manage", HttpStatusCode.OK)]
    public async Task Cold_and_warm_contributed_endpoints_enforce_policy(string? permission, HttpStatusCode expected)
    {
        await using var host = await HostingTestHost.StartAsync();
        if (permission is not null) host.Client.DefaultRequestHeaders.Add("X-Probe-Permission", permission);
        for (var i = 0; i < 2; i++)
        {
            using var response = await host.Client.GetAsync("/_elsa/hosting-probe");
            Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.OK) Assert.Equal("Authoring", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(expected == HttpStatusCode.OK ? 2 : 0, host.Probe.HandlerCalls);
    }

    [Theory]
    [InlineData("/_elsa/studio/modules", false)]
    [InlineData("/_elsa/studio/module-registry", false)]
    [InlineData("/_elsa/studio/modules", true)]
    [InlineData("/_elsa/studio/module-registry", true)]
    public async Task Selected_shell_discovers_external_feature_and_configured_versions(string route, bool withDefaultDecoy)
    {
        await using var host = await HostingTestHost.StartAsync(withDefaultDecoy: withDefaultDecoy);
        using var response = await host.Client.GetAsync(route);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hosting consumer", json);
        Assert.Contains("9.8.7", json);
        Assert.Contains("6.5.4", json);
        Assert.False(File.Exists(Path.Combine(host.Root, "shells.json")));
        Assert.DoesNotContain(host.App.Services.GetRequiredService<IOptions<StudioHostingOptions>>().Value.ShellName, new[] { "Default" });
    }

    [Fact]
    public async Task Static_files_runtime_and_custom_fallback_preserve_browser_contracts()
    {
        await using var host = await HostingTestHost.StartAsync();
        foreach (var (path, content, mime) in new[] { ("/entry.js", "export const probe = true;", "javascript"), ("/entry.css", "body{}", "text/css"), ("/some/application/route", "<html>custom Studio</html>", "text/html") })
        {
            using var response = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(content, await response.Content.ReadAsStringAsync());
            Assert.Contains(mime, response.Content.Headers.ContentType!.MediaType);
            Assert.True(response.Headers.CacheControl!.NoCache);
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.IfModifiedSince = response.Content.Headers.LastModified;
            using var cached = await host.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        }
        using var missing = await host.Client.GetAsync("/_elsa/unknown");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var script = await host.Client.GetStringAsync("/studio-runtime.js");
        Assert.StartsWith("window.__ELSA_STUDIO_RUNTIME__ = ", script);
        Assert.DoesNotContain("private-key-sentinel", script);
        Assert.DoesNotContain("server-only-sentinel", script);
        Assert.DoesNotContain("unrelated-sentinel", script);
        using var runtime = JsonDocument.Parse(script[(script.IndexOf('=') + 1)..].Trim().TrimEnd(';'));
        Assert.Equal("https://browser.example/", runtime.RootElement.GetProperty("backendBaseUrl").GetString());
        Assert.False(runtime.RootElement.GetProperty("auth").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Failed_shell_activation_keeps_files_available_but_refuses_partial_runtime_configuration()
    {
        await using var host = await HostingTestHost.StartAsync(failActivation: true);
        Assert.Equal("export const probe = true;", await host.Client.GetStringAsync("/entry.js"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/studio-runtime.js")).StatusCode);
        Assert.Equal("<html>custom Studio</html>", await host.Client.GetStringAsync("/"));
        using var missing = await host.Client.GetAsync("/_elsa/unknown");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("/_elsa/studio/modules")]
    [InlineData("/_elsa/studio/module-registry")]
    public async Task Discovery_retains_its_scoped_services_across_generation_replacement(string route)
    {
        await using var host = await HostingTestHost.StartAsync();
        host.Probe.PauseNext = 1;
        var request = host.Client.GetAsync(route);
        var scope = await host.Probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var registry = host.App.Services.GetRequiredService<IShellRegistry>();
        var old = registry.GetActive("Authoring");
        Assert.NotNull(old);
        try
        {
            await registry.ReloadAsync("Authoring").WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotSame(old, registry.GetActive("Authoring"));
            Assert.False(scope.Disposed);
            using var current = await host.Client.GetAsync(route);
            current.EnsureSuccessStatusCode();
            Assert.False(scope.Disposed);
        }
        finally { host.Probe.Release.TrySetResult(); }
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));
        response.EnsureSuccessStatusCode();
        await scope.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(scope.Disposed);
    }

    [Fact]
    public async Task Canceled_discovery_releases_its_scope()
    {
        await using var host = await HostingTestHost.StartAsync();
        host.Probe.PauseNext = 1;
        using var cancellation = new CancellationTokenSource();
        var request = host.Client.GetAsync("/_elsa/studio/modules", cancellation.Token);
        var scope = await host.Probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
            await scope.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { host.Probe.Release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Root_manifest_consumers_retain_the_selected_generation_until_the_awaited_read_finishes(bool moduleRegistry)
    {
        await using var host = await HostingTestHost.StartAsync(withDefaultDecoy: true);
        await using var rootScope = host.App.Services.CreateAsyncScope();
        var manifests = rootScope.ServiceProvider.GetRequiredService<IStudioModuleManifestProvider>();
        host.Probe.PauseNext = 1;
        Task request = moduleRegistry ? manifests.GetModuleRegistry(CancellationToken.None) : manifests.GetModules(CancellationToken.None);
        var retained = await host.Probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var registry = host.App.Services.GetRequiredService<IShellRegistry>();
        var old = registry.GetActive("Authoring");
        try
        {
            await registry.ReloadAsync("Authoring").WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotSame(old, registry.GetActive("Authoring"));
            Assert.False(retained.Disposed);
            var current = await manifests.GetModules(CancellationToken.None);
            Assert.Contains(current.Modules, module => module.ShellFeatureName == "HostingConsumer");
        }
        finally { host.Probe.Release.TrySetResult(); }
        await request.WaitAsync(TimeSpan.FromSeconds(10));
        await retained.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Root_manifest_cancellation_releases_the_tracked_scope()
    {
        await using var host = await HostingTestHost.StartAsync();
        await using var rootScope = host.App.Services.CreateAsyncScope();
        host.Probe.PauseNext = 1;
        using var cancellation = new CancellationTokenSource();
        var pending = rootScope.ServiceProvider.GetRequiredService<IStudioModuleManifestProvider>().GetModuleRegistry(cancellation.Token);
        var retained = await host.Probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            await retained.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { host.Probe.Release.TrySetResult(); }
    }

    [Fact]
    public async Task Runtime_reports_the_installed_auth_posture_after_configuration_changes()
    {
        await using var host = await HostingTestHost.StartAsync();
        host.App.Services.GetRequiredService<IConfiguration>()["Studio:Auth:Enabled"] = "true";
        var script = await host.Client.GetStringAsync("/studio-runtime.js");
        using var json = JsonDocument.Parse(script[(script.IndexOf('=') + 1)..].Trim().TrimEnd(';'));
        Assert.False(json.RootElement.GetProperty("auth").GetProperty("enabled").GetBoolean());
        Assert.Equal(new[] { "activityDefinitions", "appearance", "attention", "auth", "backendBaseUrl", "backendId", "branding", "dashboard", "hostId", "workflows" }, json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    [Theory]
    [InlineData("shell")]
    [InlineData("shell-path")]
    [InlineData("file")]
    [InlineData("index")]
    [InlineData("root-prefix")]
    [InlineData("wildcard-prefix")]
    [InlineData("ambiguous-root-prefix")]
    public void Invalid_registration_options_fail_early(string kind)
    {
        var builder = WebApplication.CreateSlimBuilder();
        using var contentRoot = builder.Environment.ContentRootFileProvider as IDisposable;
        using var configuration = builder.Configuration;
        Assert.Throws<ArgumentException>(() => builder.AddElsaStudio(options =>
        {
            options.ShellsFile = null;
            switch (kind)
            {
                case "shell": options.ShellName = " "; break;
                case "shell-path": options.ShellName = "../Authoring"; break;
                case "file": options.ShellsFile = " "; break;
                case "index": options.IndexFile = "../index.html"; break;
                case "root-prefix": options.QueryTokenPathPrefixes = ["/"]; break;
                case "wildcard-prefix": options.QueryTokenPathPrefixes = ["/hubs/*"]; break;
                case "ambiguous-root-prefix": options.QueryTokenPathPrefixes = ["//"]; break;
            }
        }));
    }

    [Fact]
    public async Task Missing_mandatory_api_fails_startup_without_activating_features()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["CShells:Shells:Authoring:Features:HostingConsumer"] = "true" });
        builder.AddElsaStudio(options => { options.ShellName = "Authoring"; options.ShellsFile = null; })
            .ConfigureShells(shells => shells.WithAssemblies(typeof(HostingConsumerFeature).Assembly));
        await using var app = builder.Build();
        app.UseElsaStudio();
        app.MapElsaStudio();
        var error = await Assert.ThrowsAsync<StudioHostingException>(() => app.StartAsync());
        Assert.Contains("Authoring", error.Message);
        Assert.Contains("StudioApi", error.Message);
    }

    [Theory]
    [InlineData(1, HttpStatusCode.OK)]
    [InlineData(2, HttpStatusCode.ServiceUnavailable)]
    public async Task Scope_acquisition_retries_one_disposal_race_only(int failures, HttpStatusCode expected)
    {
        await using var host = await HostingTestHost.StartAsync(scopeFailures: failures);
        using var response = await host.Client.GetAsync("/_elsa/studio/modules");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(2, host.Probe.ScopeAcquisitions);
    }

    [Fact]
    public async Task Framework_fallback_resolves_the_selected_nondefault_shell()
    {
        await using var host = await HostingTestHost.StartAsync();
        var context = new ShellResolutionContext();
        context.Set(ShellResolutionContextKeys.Path, "/unowned");
        var selected = await host.App.Services.GetRequiredService<IShellResolver>().ResolveAsync(context);
        Assert.Equal("Authoring", selected?.Name);
    }

    [Fact]
    public async Task Relative_shell_file_uses_content_root_and_installed_options_are_frozen()
    {
        var root = Directory.CreateTempSubdirectory("studio-hosting-file-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "shells.json"), """{"CShells":{"Shells":{"Authoring":{"Features":{"StudioApi":{}}}}},"Studio":{"Auth":{"Enabled":true}}}""");
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, ContentRootPath = root });
            builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            StudioHostingOptions retained = null!;
            builder.AddElsaStudio(options => { retained = options; options.ShellName = "Authoring"; });
            retained.ShellName = "MutatedAfterRegistration";
            retained.IndexFile = "../invalid";
            await using var app = builder.Build();
            app.UseElsaStudio();
            app.MapElsaStudio();
            await app.StartAsync();
            using var client = app.GetTestClient();
            using var modules = await client.GetAsync("/_elsa/studio/modules");
            modules.EnsureSuccessStatusCode();
            var settings = app.Services.GetRequiredService<IOptions<StudioHostingOptions>>().Value;
            Assert.Equal("Authoring", settings.ShellName);
            Assert.Equal(Path.Combine(root, "shells.json"), settings.ShellsFile);
            Assert.Contains("\"enabled\":true", await client.GetStringAsync("/studio-runtime.js"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Host_roots_borrow_the_same_process_configuration_gate()
    {
        var first = HostingTestHost.CreateGate();
        var second = HostingTestHost.CreateGate();
        var lease = await first.AcquireAsync();
        try
        {
            var waiting = second.AcquireAsync().AsTask();
            Assert.False(waiting.IsCompleted);
            await lease.DisposeAsync();
            await using var borrowed = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await lease.DisposeAsync(); }
    }

    [Fact]
    public async Task Coordinator_is_shared_cancellable_and_lease_disposal_is_idempotent()
    {
        await using var host = await HostingTestHost.StartAsync();
        var gate = host.Studio.ConfigurationMutationGate;
        Assert.Same(gate, host.App.Services.GetRequiredService<StudioConfigurationMutationGate>());
        var lease = await gate.AcquireAsync();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await gate.AcquireAsync(canceled.Token));
        var pending = gate.AcquireAsync().AsTask();
        Assert.False(pending.IsCompleted);
        await lease.DisposeAsync();
        await using var next = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await lease.DisposeAsync();
        var stillBlocked = gate.AcquireAsync().AsTask();
        Assert.False(stillBlocked.IsCompleted);
        await next.DisposeAsync();
        await using var final = await stillBlocked.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

internal sealed class HostingTestHost : IAsyncDisposable
{
    private WebApplicationBuilder? _builder;
    public string Root { get; } = Directory.CreateTempSubdirectory("studio-hosting-").FullName;
    public HostingProbe Probe { get; } = new();
    public StudioBuilder Studio { get; private set; } = null!;
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public static StudioConfigurationMutationGate CreateGate()
    {
        var builder = WebApplication.CreateSlimBuilder();
        using var contentRoot = builder.Environment.ContentRootFileProvider as IDisposable;
        using var configuration = builder.Configuration;
        return builder.AddElsaStudio(options => options.ShellsFile = null).ConfigurationMutationGate;
    }

    public static async Task<HostingTestHost> StartAsync(bool failActivation = false, int scopeFailures = 0, bool withDefaultDecoy = false)
    {
        var host = new HostingTestHost();
        try
        {
            var webRoot = Directory.CreateDirectory(Path.Combine(host.Root, "wwwroot")).FullName;
            await File.WriteAllTextAsync(Path.Combine(webRoot, "entry.js"), "export const probe = true;");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "entry.css"), "body{}");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "custom.html"), "<html>custom Studio</html>");
            var builder = host._builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, ContentRootPath = host.Root, WebRootPath = webRoot });
            builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:Authoring:Features:StudioApi"] = "true",
                ["CShells:Shells:Authoring:Features:HostingConsumer"] = "true",
                ["Studio:Api:HostVersion"] = "9.8.7", ["Studio:Api:SdkVersion"] = "6.5.4",
                ["Studio:Hosting:ShellName"] = "WrongBoundName",
                ["Studio:BackendBaseUrl"] = "https://browser.example/",
                ["Studio:BackendServerBaseUrl"] = "http://server-only-sentinel/",
                ["Studio:BackendModuleManagementApiKey"] = "private-key-sentinel",
                ["Unrelated"] = "unrelated-sentinel"
            });
            if (withDefaultDecoy) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["CShells:Shells:Default:Features:StudioApi"] = "true" });
            if (failActivation) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["CShells:Shells:Authoring:Features:FailingStudioTestFeature"] = "true" });
            builder.Services.AddSingleton(host.Probe);
            host.Studio = builder.AddElsaStudio(options => { options.ShellName = "Authoring"; options.ShellsFile = null; options.IndexFile = "custom.html"; })
                .ConfigureShells(shells => shells.WithAssemblies(typeof(HostingConsumerFeature).Assembly).ShareSingletonWithShells<HostingProbe>());
            if (scopeFailures > 0)
            {
                host.Probe.ScopeFailures = scopeFailures;
                var descriptor = builder.Services.Last(item => item.ServiceType == typeof(IShellRegistry));
                builder.Services.Remove(descriptor);
                builder.Services.AddSingleton<IShellRegistry>(provider =>
                {
                    var original = (IShellRegistry)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
                    var proxy = DispatchProxy.Create<IShellRegistry, HostingRegistryProxy>();
                    ((HostingRegistryProxy)(object)proxy).Initialize(original, host.Probe);
                    return proxy;
                });
            }
            builder.Services.AddAuthentication("HostingProbe").AddScheme<AuthenticationSchemeOptions, HostingAuthentication>("HostingProbe", _ => { });
            builder.Services.AddAuthorization(options => options.AddPolicy("hosting-probe", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "manage")));
            host.App = builder.Build();
            host.App.UseElsaStudio();
            host.App.MapElsaStudio();
            await host.App.StartAsync();
            host.Client = host.App.GetTestClient();
            return host;
        }
        catch { await host.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null) await App.DisposeAsync();
        else if (_builder is not null)
        {
            _builder.Configuration.Dispose();
            (_builder.Environment.ContentRootFileProvider as IDisposable)?.Dispose();
        }
        Directory.Delete(Root, true);
    }
}

public sealed class HostingProbe
{
    public int HandlerCalls;
    public int PauseNext;
    public int ScopeFailures;
    public int ScopeAcquisitions;
    internal TaskCompletionSource<HostingRequestScope> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class HostingRequestScope : IDisposable
{
    public bool Disposed { get; private set; }
    public TaskCompletionSource DisposedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() { Disposed = true; DisposedSignal.TrySetResult(); }
}

internal sealed class HostingManifestPause(HostingProbe probe, HostingRequestScope scope) : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    public async Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref probe.PauseNext, 0) == 0) return;
        probe.Entered.TrySetResult(scope);
        await probe.Release.Task.WaitAsync(cancellationToken);
        if (scope.Disposed) throw new ObjectDisposedException(nameof(HostingRequestScope));
    }
}

[ShellFeature("HostingConsumer")]
[StudioModule("hosting-consumer", "Hosting consumer", HasStyles = false)]
public class HostingConsumerFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<HostingRequestScope>();
        services.AddScoped<IStudioEventHandler<OnStudioModuleManifestsCollecting>, HostingManifestPause>();
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        endpoints.MapGet("/_elsa/hosting-probe", (HostingProbe probe, ShellSettings shell) =>
        {
            Interlocked.Increment(ref probe.HandlerCalls);
            return Results.Text(shell.Id.Name);
        }).RequireAuthorization("hosting-probe");
}

internal sealed class HostingAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var permission = Request.Headers["X-Probe-Permission"].ToString();
        return Task.FromResult(string.IsNullOrEmpty(permission) ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", permission)], Scheme.Name)), Scheme.Name)));
    }
}

// Protocol stubs inject only the generation-disposal race; all other public framework calls delegate unchanged.
public class HostingRegistryProxy : DispatchProxy
{
    private IShellRegistry _inner = null!;
    private HostingProbe _probe = null!;
    public void Initialize(IShellRegistry inner, HostingProbe probe) { _inner = inner; _probe = probe; }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var result = HostingProxy.Invoke(_inner, method!, args);
        return method!.Name == nameof(IShellRegistry.GetOrActivateAsync) ? WrapAsync((Task<IShell>)result!) : result;
    }
    private async Task<IShell> WrapAsync(Task<IShell> task)
    {
        var proxy = DispatchProxy.Create<IShell, HostingShellProxy>();
        ((HostingShellProxy)(object)proxy).Initialize(await task, _probe);
        return proxy;
    }
}

public class HostingShellProxy : DispatchProxy
{
    private IShell _inner = null!;
    private HostingProbe _probe = null!;
    public void Initialize(IShell inner, HostingProbe probe) { _inner = inner; _probe = probe; }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IShell.BeginScope))
        {
            Interlocked.Increment(ref _probe.ScopeAcquisitions);
            if (Interlocked.Decrement(ref _probe.ScopeFailures) >= 0) throw new ObjectDisposedException("test generation");
        }
        return HostingProxy.Invoke(_inner, method, args);
    }
}

internal static class HostingProxy
{
    public static object? Invoke(object inner, MethodInfo method, object?[]? args)
    {
        try { return method.Invoke(inner, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
}
