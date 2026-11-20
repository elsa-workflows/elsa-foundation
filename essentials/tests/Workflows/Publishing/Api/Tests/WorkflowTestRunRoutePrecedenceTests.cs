using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Demonstrates the root cause of the draft/versioned test-run route collision and proves the fix.
/// <para>
/// ASP.NET endpoint routing compares candidate endpoints by <c>Order</c> <b>before</b> route
/// precedence. So a literal segment (<c>drafts</c>) is only guaranteed to beat a parameter
/// (<c>{versionId}</c>) when both endpoints share the same order. When the host assigns the versioned
/// endpoint a lower order than the draft endpoint — which can happen depending on endpoint discovery
/// order and varies between process starts — the parameter route wins and binds
/// <c>versionId = "drafts"</c>. Constraining <c>{versionId}</c> to exclude the reserved literal removes
/// the overlap entirely, so the draft path matches exactly one endpoint regardless of order.
/// </para>
/// <para>The templates mirror the production routes in <c>RouteConstants</c> (internal).</para>
/// </summary>
public sealed class WorkflowTestRunRoutePrecedenceTests
{
    private const string LiteralDraftTemplate = "publishing/workflows/drafts/test-runs";
    private const string UnconstrainedVersionedTemplate = "publishing/workflows/{versionId}/test-runs";
    private const string ConstrainedVersionedTemplate = "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/test-runs";

    private const string DraftPath = "/publishing/workflows/drafts/test-runs";

    [Fact]
    public async Task UnconstrainedVersionedRouteHijacksDraftPathWhenGivenLowerOrder()
    {
        // Control: reproduces the bug. Lower order on the parameter route makes it win over the literal.
        await using var host = await RouteHost.StartAsync(UnconstrainedVersionedTemplate, versionedOrder: -1);

        var body = await host.PostAsync(DraftPath);

        Assert.Equal("versioned:drafts", body);
    }

    [Fact]
    public async Task ConstrainedVersionedRouteNeverMatchesDraftPathEvenWithLowerOrder()
    {
        // Fix: the constraint excludes "drafts", so the literal draft route is the only match.
        await using var host = await RouteHost.StartAsync(ConstrainedVersionedTemplate, versionedOrder: -1);

        Assert.Equal("draft", await host.PostAsync(DraftPath));
        Assert.Equal("versioned:version-1", await host.PostAsync("/publishing/workflows/version-1/test-runs"));
    }

    private sealed class RouteHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private RouteHost(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
        }

        public static async Task<RouteHost> StartAsync(string versionedTemplate, int versionedOrder)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            var app = builder.Build();

            // Draft (literal) endpoint at the default order (0).
            app.MapPost(LiteralDraftTemplate, () => Results.Text("draft"));

            // Versioned (parameter) endpoint, deliberately given a winning (lower) order to expose the
            // order-before-precedence comparison that makes the collision nondeterministic.
            app.MapPost(versionedTemplate, (string versionId) => Results.Text($"versioned:{versionId}"))
                .Add(b => ((RouteEndpointBuilder)b).Order = versionedOrder);

            await app.StartAsync();
            var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            return new RouteHost(app, client);
        }

        public async Task<string> PostAsync(string path)
        {
            var response = await _client.PostAsync(path, content: null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
