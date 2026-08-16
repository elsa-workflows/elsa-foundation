using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using CShells.FastEndpoints.Contracts;
using Elsa.Api.AspNetCore;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogsApiCoexistenceTests
{
    [Fact]
    public async Task Structured_logs_minimal_routes_and_a_secured_fastendpoints_route_share_foundation_authorization()
    {
        StructuredLogsCoexistenceHost.ResetPermissionEvaluatorObservations();
        await using var host = await StructuredLogsCoexistenceHost.StartAsync();

        var structuredRoutes = host.EndpointDataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/_elsa/studio/diagnostics/structured-logs/", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(3, structuredRoutes.Length);
        Assert.Equal(3, structuredRoutes.Select(endpoint => endpoint.RoutePattern.RawText).Distinct(StringComparer.Ordinal).Count());

        using (var anonymousStructured = await host.Client.GetAsync(StructuredLogsApiHostPath.Recent))
        using (var anonymousFastEndpoints = await host.Client.GetAsync(UnrelatedFastEndpointsEndpoint.Route))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousStructured.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousFastEndpoints.StatusCode);
        }

        foreach (var path in new[]
                 {
                     StructuredLogsApiHostPath.Recent,
                     StructuredLogsApiHostPath.Sources
                 })
        {
            using var request = Request(path, StructuredLogsApiHostPath.ExactIdentity);
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var streamRequest = Request(StructuredLogsApiHostPath.Stream, StructuredLogsApiHostPath.ExactIdentity))
        using (var streamResponse = await host.Client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead))
        {
            Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
            Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);
        }

        using (var fastEndpointsRequest = Request(
                   UnrelatedFastEndpointsEndpoint.Route,
                   StructuredLogsApiHostPath.ExactIdentity))
        using (var fastEndpointsResponse = await host.Client.SendAsync(fastEndpointsRequest))
        {
            Assert.Equal(HttpStatusCode.OK, fastEndpointsResponse.StatusCode);
            Assert.Equal(
                "unrelated-fastendpoints",
                (await fastEndpointsResponse.Content.ReadAsStringAsync()).Trim('"'));
        }

        using (var deniedStructured = await host.Client.SendAsync(
                   Request(StructuredLogsApiHostPath.Recent, StructuredLogsApiHostPath.AdjacentIdentity)))
        using (var deniedFastEndpoints = await host.Client.SendAsync(
                   Request(UnrelatedFastEndpointsEndpoint.Route, StructuredLogsApiHostPath.AdjacentIdentity)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedStructured.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deniedFastEndpoints.StatusCode);
        }

        Assert.True(StructuredLogsCoexistenceHost.PermissionEvaluatorCallsFor(StructuredLogsApiHostPath.Recent) >= 2);
        Assert.True(StructuredLogsCoexistenceHost.PermissionEvaluatorCallsFor(StructuredLogsApiHostPath.Sources) >= 1);
        Assert.True(StructuredLogsCoexistenceHost.PermissionEvaluatorCallsFor(StructuredLogsApiHostPath.Stream) >= 1);
        Assert.True(StructuredLogsCoexistenceHost.PermissionEvaluatorCallsFor(UnrelatedFastEndpointsEndpoint.Route) >= 2);
    }

    private static HttpRequestMessage Request(string path, string? identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StructuredLogsApiHostPath.IdentityHeader, identity);
        return request;
    }
}

internal static class StructuredLogsApiHostPath
{
    public const string IdentityHeader = "X-Structured-Logs-Coexistence-Identity";
    public const string ExactIdentity = "exact";
    public const string WildcardIdentity = "wildcard";
    public const string AdjacentIdentity = "adjacent";
    public const string Recent = "/_elsa/studio/diagnostics/structured-logs/recent";
    public const string Sources = "/_elsa/studio/diagnostics/structured-logs/sources";
    public const string Stream = "/_elsa/studio/diagnostics/structured-logs/stream";
}

internal sealed class StructuredLogsCoexistenceHost : IAsyncDisposable
{
    private const string SchemeName = "structured-logs-coexistence";
    private readonly IHost host;

    private StructuredLogsCoexistenceHost(IHost host, IReadOnlyList<EndpointDataSource> endpointDataSources)
    {
        this.host = host;
        Client = host.GetTestClient();
        EndpointDataSources = endpointDataSources;
    }

    public HttpClient Client { get; }

    public IReadOnlyList<EndpointDataSource> EndpointDataSources { get; }

    public static async Task<StructuredLogsCoexistenceHost> StartAsync()
    {
        IReadOnlyList<EndpointDataSource>? dataSources = null;
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
                    services.AddRouting();
                    services.AddAuthentication(SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, CoexistenceAuthenticationHandler>(SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddOpenApi();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { SchemeName });
                    services.ReplacePermissionEvaluator<RecordingPermissionEvaluator>();

                    new StructuredLogsFeature
                    {
                        ServiceName = "structured-logs-coexistence",
                        SourceDisplayName = "Structured Logs Coexistence",
                        BufferCapacity = 3
                    }.ConfigureServices(services);
                    services.Configure<StructuredLogsOptions>(options =>
                    {
                        options.RecentPath = StructuredLogsApiHostPath.Recent;
                        options.SourcesPath = StructuredLogsApiHostPath.Sources;
                        options.StreamPath = StructuredLogsApiHostPath.Stream;
                        options.MaxRecentQuerySize = 3;
                        options.TailPollInterval = TimeSpan.FromMilliseconds(10);
                    });
                    services.AddFastEndpoints(options => options.Assemblies = [typeof(UnrelatedFastEndpointsEndpoint).Assembly]);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        StructuredLogsApi.MapStructuredLogsApi(endpoints);
                        endpoints.MapFastEndpoints(config =>
                        {
                            using var scope = endpoints.ServiceProvider.CreateScope();
                            foreach (var configurator in scope.ServiceProvider.GetServices<IFastEndpointsConfigurator>())
                                configurator.Configure(config);
                        });
                        endpoints.MapOpenApi();
                        dataSources = endpoints.DataSources.ToArray();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new StructuredLogsCoexistenceHost(host, dataSources ?? []);
    }

    public static void ResetPermissionEvaluatorObservations() => RecordingPermissionEvaluator.Reset();

    public static int PermissionEvaluatorCallsFor(string path) => RecordingPermissionEvaluator.CallsFor(path);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    private sealed class RecordingPermissionEvaluator(IPermissionCatalog catalog) : IPermissionEvaluator
    {
        private static readonly ConcurrentDictionary<string, int> Calls = new(StringComparer.Ordinal);
        private readonly ClaimsPermissionEvaluator inner = new(catalog);

        public async ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Resource is HttpContext httpContext)
                Calls.AddOrUpdate(httpContext.Request.Path.Value ?? string.Empty, 1, static (_, value) => value + 1);
            return await inner.EvaluateAsync(context, cancellationToken);
        }

        public static void Reset() => Calls.Clear();

        public static int CallsFor(string path) => Calls.GetValueOrDefault(path);
    }

    private sealed class CoexistenceAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(StructuredLogsApiHostPath.IdentityHeader, out var values))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = values.ToString();
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.Normalized, "v1"),
                new(IdentityClaimTypes.Provider, "structured-logs-coexistence")
            };
            var permission = identity switch
            {
                StructuredLogsApiHostPath.ExactIdentity => StructuredLogsApiHostPathPermission,
                StructuredLogsApiHostPath.WildcardIdentity => PermissionKey.Wildcard,
                StructuredLogsApiHostPath.AdjacentIdentity => "Diagnostics:StructuredLog",
                _ => null
            };
            if (permission is not null)
                claims.Add(new Claim(IdentityClaimTypes.Permission, permission));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private const string StructuredLogsApiHostPathPermission = "Diagnostics:StructuredLogs";
}

internal sealed class UnrelatedFastEndpointsEndpoint : ElsaEndpointWithoutRequest<string>
{
    public const string Route = "/_canary/structured-logs-unrelated-fastendpoints";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions("Diagnostics:StructuredLogs");
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        Send.OkAsync("unrelated-fastendpoints", cancellationToken);
}
