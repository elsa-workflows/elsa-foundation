using System.Net;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.FastEndpoints.Features;
using Elsa.Api.FastEndpoints.Tests.TestSupport;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Oidc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Api.FastEndpoints.Tests;

/// <summary>
/// Verifies the MS-14 error contract at the shared endpoint-base level. The global
/// <c>ProblemDetailsFastEndpointConfigurator</c> switches every Elsa endpoint to RFC 7807 ProblemDetails,
/// and the handler base classes map <see cref="Primitives.Exceptions.EntityNotFoundException"/> to
/// <c>404 Not Found</c>. Together they resolve issue #393, where a not-found lookup surfaced as a generic 500.
/// The test boots a real CShells + FastEndpoints host and calls <see cref="NotFoundEndpoint"/>, whose request
/// sender throws <c>EntityNotFoundException</c>.
/// </summary>
public sealed class ErrorContractIntegrationTests
{
    [Fact]
    public async Task Not_found_lookup_is_mapped_to_404_problem_details()
    {
        await using var host = await StartHostAsync();

        var response = await host.Client.GetAsync($"/app/{NotFoundEndpoint.Route}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<Host> StartHostAsync()
    {
        // This host reaches the endpoint anonymously via ApiSecurity.AllowAnonymous, which is honored only
        // in Development (locked product decision — see ApiSecurityFastEndpointsConfigurator).
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
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
                    shell
                        .WithPath("app")
                        .WithFeature<FastEndpointsFeature>(f => f.EndpointRoutePrefix = string.Empty)
                        .WithFeature<TestFastEndpointsFeature>()
                        .WithFeature<FoundationIdentityAbstractionsFeature>()
                        .WithFeature<OidcAuthenticationFeature>()
                        .WithFeature<ApiSecurityFeature>(f => f.AllowAnonymous = true);
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
        return new Host(app, client);
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
