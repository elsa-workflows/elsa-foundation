using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests;

public sealed class WorkflowsDesignApiContractTests
{
    [Fact]
    public void Mapper_registers_exactly_the_27_owned_operations_with_stable_metadata()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);

        Elsa.Workflows.Design.Api.WorkflowsDesignApi.MapWorkflowsDesignApi(routes);

        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(27, endpoints.Length);
        Assert.Equal(27, endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId).Distinct().Count() == 1 ? endpoints.Length : 0);

        foreach (var endpoint in endpoints)
        {
            Assert.Equal("Elsa.Workflows.Design.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
            Assert.NotNull(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName));
        }
    }

    [Fact]
    public void Permission_metadata_contains_the_catalog_action_and_evaluator_wildcard()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        Elsa.Workflows.Design.Api.WorkflowsDesignApi.MapWorkflowsDesignApi(routes);

        var endpoint = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "design/workflows/definitions/{definitionId}" &&
                                 candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);
        var disposition = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(disposition.Value!));
        Assert.Contains(PermissionKey.Normalize(WorkflowDesignPermissions.Read), policy.Descriptor!.Permissions);
        Assert.Contains(PermissionKey.Normalize(PermissionKey.Wildcard), policy.Descriptor.Permissions);
        Assert.Contains("*/*", Assert.IsAssignableFrom<IAcceptsMetadata>(endpoint.Metadata.GetMetadata<IAcceptsMetadata>()).ContentTypes);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("untrusted", HttpStatusCode.Forbidden)]
    [InlineData("trusted-read", HttpStatusCode.OK)]
    [InlineData("trusted", HttpStatusCode.OK)]
    public async Task Requests_use_the_shared_normalized_permission_evaluator(string? identity, HttpStatusCode expected)
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/design/workflows/definitions");
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, identity);

        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class AuthorizationHost(IHost host) : IAsyncDisposable
    {
        public const string IdentityHeader = "X-Workflow-Design-Identity";
        public HttpClient Client { get; } = host.GetTestClient();

        public static async Task<AuthorizationHost> StartAsync()
        {
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddAuthentication(AuthenticationHandler.SchemeName)
                            .AddScheme<AuthenticationSchemeOptions, AuthenticationHandler>(AuthenticationHandler.SchemeName, _ => { });
                        services.AddAuthorization();
                        services.AddFoundationIdentityAbstractions(options =>
                            options.NormalizedAuthenticationTypes = new HashSet<string>([AuthenticationHandler.SchemeName], StringComparer.Ordinal));
                        new WorkflowsDesignApiFeature().ConfigureServices(services);
                        services.AddSingleton<IRequestSender, DefinitionsRequestSender>();
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => WorkflowsDesignApi.MapWorkflowsDesignApi(endpoints));
                    });
                })
                .Build();
            await host.StartAsync();
            return new AuthorizationHost(host);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class AuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : Microsoft.AspNetCore.Authentication.AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "WorkflowDesignContract";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers[AuthorizationHost.IdentityHeader].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>();
            if (identity is "trusted" or "trusted-read")
            {
                claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));
                claims.Add(new Claim(IdentityClaimTypes.Permission,
                    identity == "trusted" ? PermissionKey.Wildcard : WorkflowDesignPermissions.Read));
                claims.Add(new Claim(IdentityClaimTypes.TenantId, "tenant-design"));
            }
            else
                claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }

    private sealed class DefinitionsRequestSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            if (request is ListDefinitions)
                return Task.FromResult((T)(object)new WorkflowDefinitionListView([]));

            throw new InvalidOperationException($"Unexpected request '{request.GetType().FullName}'.");
        }
    }
}
