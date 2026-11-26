using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Studio.Api.Authorization;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests.Hosting;

public sealed class StudioSecurityOwnershipTests
{
    [Fact]
    public async Task Shell_authentication_borrows_root_cache_and_clock_across_reload()
    {
        await using var host = await SecurityHost.StartAsync();
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", "owner-test");
        using var first = await host.Client.GetAsync("/private");
        first.EnsureSuccessStatusCode();
        Assert.Equal(1, host.Backend.SessionCalls);
        var registry = host.App.Services.GetRequiredService<IShellRegistry>();
        await AuthenticateInShell(host);
        Assert.Equal(1, host.Backend.SessionCalls);
        await registry.ReloadAsync("Default");
        Assert.Equal(0, host.Clock.Disposals);
        await AuthenticateInShell(host);
        Assert.Equal(1, host.Backend.SessionCalls);
        await host.DisposeAsync();
        Assert.Equal(1, host.Clock.Disposals);
    }

    [Fact]
    public async Task Shell_authentication_uses_root_transport_while_module_factories_remain_shell_owned()
    {
        await using var host = await SecurityHost.StartAsync(cacheSeconds: 0);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", "owner-test");
        using var first = await host.Client.GetAsync("/private");
        first.EnsureSuccessStatusCode();
        var rootFactory = (TaggedFactory)host.App.Services.GetRequiredService<IHttpClientFactory>();
        await AuthenticateInShell(host);
        await host.App.Services.GetRequiredService<IShellRegistry>().ReloadAsync("Default");
        Assert.Equal(0, rootFactory.Disposals);
        await AuthenticateInShell(host);
        Assert.Equal(3, host.Backend.SessionCalls);
        Assert.All(host.Backend.FactoryIds, id => Assert.Equal(rootFactory.Id, id));
        await host.DisposeAsync();
        Assert.Equal(1, rootFactory.Disposals);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    [InlineData(false, HttpStatusCode.OK)]
    public async Task Installed_gate_and_runtime_posture_agree_after_configuration_reload(bool enabled, HttpStatusCode expected)
    {
        await using var host = await SecurityHost.StartAsync(authEnabled: enabled);
        using var before = await host.Client.GetAsync("/private");
        Assert.Equal(expected, before.StatusCode);
        host.App.Services.GetRequiredService<IConfiguration>()["Studio:Auth:Enabled"] = (!enabled).ToString();
        using var after = await host.Client.GetAsync("/private");
        Assert.Equal(expected, after.StatusCode);
        var script = await host.Client.GetStringAsync("/studio-runtime.js");
        using var json = JsonDocument.Parse(script[(script.IndexOf('=') + 1)..].Trim().TrimEnd(';'));
        Assert.Equal(enabled, json.RootElement.GetProperty("auth").GetProperty("enabled").GetBoolean());
        Assert.Equal(0, host.Backend.SessionCalls);
    }

    [Fact]
    public async Task No_query_prefix_is_trusted_by_default()
    {
        await using var host = await SecurityHost.StartAsync();
        using var response = await host.Client.GetAsync("/private?access_token=owner-test");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.Backend.SessionCalls);
    }

    private static async Task AuthenticateInShell(SecurityHost host)
    {
        var shell = await host.App.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync("Default");
        await using var scope = shell.BeginScope();
        Assert.Same(host.Clock, scope.ServiceProvider.GetRequiredService<TimeProvider>());
        // A shell may configure its own named module clients; the general factory must not be replaced with root's.
        Assert.NotSame(host.App.Services.GetRequiredService<IHttpClientFactory>(), scope.ServiceProvider.GetRequiredService<IHttpClientFactory>());
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = "Bearer owner-test";
        var result = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().AuthenticateAsync(context, StudioBridgeAuthorization.SchemeName);
        Assert.True(result.Succeeded);
        var authorization = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(result.Principal!, null, StudioBridgeAuthorization.ModuleManagementReadPolicyName);
        Assert.True(authorization.Succeeded);
    }

    private sealed class SecurityHost : IAsyncDisposable
    {
        private bool _disposed;
        private int _nextFactoryId;
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public OwnedClock Clock { get; } = new();
        public SessionBackend Backend { get; } = new();

        public static async Task<SecurityHost> StartAsync(int cacheSeconds = 30, bool authEnabled = true)
        {
            var host = new SecurityHost();
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:Default:Features:StudioApi"] = "true",
                ["Studio:Auth:Enabled"] = authEnabled.ToString(),
                ["Studio:Auth:SessionCacheSeconds"] = cacheSeconds.ToString(),
                ["Studio:BackendBaseUrl"] = "https://backend.example/",
                ["Studio:BackendModuleManagementApiKey"] = "ownership-secret-sentinel"
            });
            builder.Services.AddSingleton<TimeProvider>(_ => host.Clock);
            builder.Services.AddSingleton<IHttpClientFactory>(_ => new TaggedFactory(Interlocked.Increment(ref host._nextFactoryId), host.Backend));
            builder.AddElsaStudio(options => options.ShellsFile = null);
            host.App = builder.Build();
            host.App.UseElsaStudio();
            host.App.MapElsaStudio();
            host.App.MapGet("/private", () => Results.Ok()).RequireAuthorization(StudioBridgeAuthorization.PolicyName);
            await host.App.StartAsync();
            host.Client = host.App.GetTestClient();
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            Client.Dispose();
            await App.DisposeAsync();
            Backend.Dispose();
        }
    }

    private sealed class OwnedClock : TimeProvider, IDisposable
    {
        public int Disposals { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            ObjectDisposedException.ThrowIf(Disposals > 0, this);
            return DateTimeOffset.Parse("2026-10-26T00:00:00Z");
        }
        public void Dispose() => Disposals++;
    }

    private sealed class TaggedFactory(int id, HttpMessageHandler backend) : IHttpClientFactory, IDisposable
    {
        public int Id => id;
        public int Disposals { get; private set; }
        public HttpClient CreateClient(string name)
        {
            ObjectDisposedException.ThrowIf(Disposals > 0, this);
            var client = new HttpClient(backend, disposeHandler: false) { BaseAddress = new Uri("https://backend.example/") };
            client.DefaultRequestHeaders.Add("X-Test-Factory", id.ToString());
            return client;
        }
        public void Dispose() => Disposals++;
    }

    private sealed class SessionBackend : HttpMessageHandler
    {
        private int _calls;
        public int SessionCalls => _calls;
        public ConcurrentQueue<int> FactoryIds { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/_elsa/identity/session", request.RequestUri!.AbsolutePath);
            Assert.False(request.Headers.Contains(StudioBackendManagementOptions.ManagementApiKeyHeaderName));
            FactoryIds.Enqueue(int.Parse(request.Headers.GetValues("X-Test-Factory").Single()));
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"status":"authenticated","subject":"owner","permissions":["module-management:read"]}""", Encoding.UTF8, "application/json") });
        }
    }
}
