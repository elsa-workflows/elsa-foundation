using System.Net;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.FastEndpoints.Features;
using Elsa.Api.FastEndpoints.Tests.Support;
using Elsa.Api.FastEndpoints.Tests.TestSupport;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Oidc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Api.FastEndpoints.Tests;

/// <summary>
/// The headline test for MS-12. Each case boots a real CShells + FastEndpoints host over Kestrel and
/// issues an unauthenticated HTTP call to the same secured-by-construction <see cref="PingEndpoint"/>.
/// The only thing that changes between a secure shell and an insecure shell is the presence of
/// <c>ApiSecurity.AllowAnonymous = true</c>; the authentication stack (Identity + OIDC JWT default
/// challenge scheme) is identical. A secure shell rejects the call with 401 while an insecure shell
/// serves 200 and logs the per-shell "security disabled" warning.
/// <para>
/// FastEndpoints keeps its endpoint configuration in <c>process-static</c> state, which is exactly
/// what the deleted process-global security flag abused. <see cref="Secure_shell_started_after_an_insecure_shell_still_rejects_401"/>
/// is the direct refutation: it relaxes the static configuration through an insecure shell and then
/// proves a subsequently composed secure shell re-secures itself rather than inheriting the leaked
/// relaxed state. Because two shells that share an endpoint assembly cannot coexist in a single host
/// (FastEndpoints requires globally unique endpoint names and discovers endpoints per whole
/// assembly), the isolation is demonstrated across sequential hosts in one process, which share the
/// same static configuration and therefore make any leak observable.
/// </para>
/// </summary>
public sealed class PerShellApiSecurityIntegrationTests
{
    private enum SecurityMode
    {
        Secure,
        InsecureAllowAnonymous
    }

    [Fact]
    public async Task Secure_shell_rejects_unauthenticated_call_with_401()
    {
        await using var host = await ShellHost.StartAsync(SecurityMode.Secure);

        var response = await host.Client.GetAsync($"/app/{PingEndpoint.Route}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Insecure_shell_serves_the_same_endpoint_anonymously_with_200()
    {
        await using var host = await ShellHost.StartAsync(SecurityMode.InsecureAllowAnonymous);

        var response = await host.Client.GetAsync($"/app/{PingEndpoint.Route}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", (await response.Content.ReadAsStringAsync()).Trim('"'));
    }

    [Fact]
    public async Task Insecure_shell_logs_the_per_shell_security_disabled_warning_naming_the_shell()
    {
        await using var host = await ShellHost.StartAsync(SecurityMode.InsecureAllowAnonymous);

        await host.Client.GetAsync($"/app/{PingEndpoint.Route}");

        var warning = host.Logs.Logs.FirstOrDefault(log =>
            log.Level == LogLevel.Warning &&
            log.Message.Contains("SECURITY IS DISABLED", StringComparison.OrdinalIgnoreCase) &&
            log.Message.Contains("app", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(warning);
    }

    /// <summary>
    /// MS-12 core: an insecure shell relaxes the process-static FastEndpoints configuration, then a
    /// secure shell is composed afterwards in the same process. If per-shell security leaked through
    /// the static state the secure shell would serve 200; instead it must re-secure itself and 401.
    /// </summary>
    [Fact]
    public async Task Secure_shell_started_after_an_insecure_shell_still_rejects_401()
    {
        await using (var insecure = await ShellHost.StartAsync(SecurityMode.InsecureAllowAnonymous))
        {
            var relaxed = await insecure.Client.GetAsync($"/app/{PingEndpoint.Route}");
            Assert.Equal(HttpStatusCode.OK, relaxed.StatusCode);
        }

        await using var secure = await ShellHost.StartAsync(SecurityMode.Secure);

        var response = await secure.Client.GetAsync($"/app/{PingEndpoint.Route}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class ShellHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ShellHost(WebApplication app, HttpClient client, RecordingLoggerProvider logs)
        {
            _app = app;
            Client = client;
            Logs = logs;
        }

        public HttpClient Client { get; }

        public RecordingLoggerProvider Logs { get; }

        public static async Task<ShellHost> StartAsync(SecurityMode mode)
        {
            var logs = new RecordingLoggerProvider();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            builder.Services.AddCShellsAspNetCore(shells =>
            {
                shells
                    .WithAuthenticationAndAuthorization()
                    .WithAssemblies(
                        typeof(TestFastEndpointsFeature).Assembly,
                        typeof(FastEndpointsFeature).Assembly,
                        typeof(ApiSecurityFeature).Assembly,
                        typeof(FoundationIdentityAbstractionsFeature).Assembly,
                        typeof(OidcAuthenticationFeature).Assembly)
                    .WithWebRouting(options => options.EnablePathRouting = true)
                    .AddShell("app", shell =>
                    {
                        // Both modes share an identical authentication stack (Identity + OIDC). The
                        // only differentiator is the ApiSecurity relax below, which is the whole point
                        // of the per-shell isolation proof. The OIDC feature is composed without a
                        // ClientId, which is exactly the secured-server default: no interactive
                        // OpenID Connect handler is registered, and the JWT bearer scheme challenges
                        // unauthenticated calls with 401.
                        shell
                            .WithPath("app")
                            .WithFeature<FastEndpointsFeature>(f => f.EndpointRoutePrefix = string.Empty)
                            .WithFeature<TestFastEndpointsFeature>()
                            .WithFeature<FoundationIdentityAbstractionsFeature>()
                            .WithFeature<OidcAuthenticationFeature>();

                        if (mode == SecurityMode.InsecureAllowAnonymous)
                            shell.WithFeature<ApiSecurityFeature>(f => f.AllowAnonymous = true);
                    });
            });

            builder.Services.AddAuthentication();
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.MapShells();
            app.UseAuthentication();
            app.UseAuthorization();

            await app.StartAsync();

            var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            return new ShellHost(app, client, logs);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
