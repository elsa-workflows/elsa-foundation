using System.Net;
using System.Net.Http.Json;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Elsa.Expressions.Api;
using Elsa.Expressions.Api.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Api;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class DomainManagementApiCompositionTests
{
    private static readonly string[] ExpectedCapabilities =
    [
        "elsa.api.activity-design",
        "elsa.api.expressions",
        "elsa.api.publishing",
        "elsa.api.runtime",
        "elsa.api.workflow-design"
    ];

    [Fact]
    public async Task Custom_host_exposes_representative_domain_journeys_without_Elsa_Server()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: true);

        Assert.DoesNotContain(
            typeof(DomainManagementApiCompositionTests).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Elsa.Server", StringComparison.Ordinal));

        await host.AssertJourneyAsync(HttpMethod.Get, "/design/workflows/definitions");
        await host.AssertJourneyAsync(HttpMethod.Get, "/design/activities/catalog");
        await host.AssertJourneyAsync(HttpMethod.Get, "/expressions/descriptors");
        await host.AssertJourneyAsync(HttpMethod.Post, "/publishing/workflows/version-1/publish", new { });
        await host.AssertJourneyAsync(HttpMethod.Get, "/runtime/workflows/executables");

        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        Assert.NotNull(capabilities);
        Assert.Equal(ExpectedCapabilities, capabilities.Capabilities.Select(x => x.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Omitted_domain_has_neither_routes_nor_capability_while_installed_domains_remain_available()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false);

        await host.AssertJourneyAsync(HttpMethod.Get, "/design/workflows/definitions");
        await host.AssertJourneyAsync(HttpMethod.Get, "/design/activities/catalog");
        await host.AssertJourneyAsync(HttpMethod.Post, "/publishing/workflows/version-1/publish", new { });
        await host.AssertJourneyAsync(HttpMethod.Get, "/runtime/workflows/executables");

        var omittedRoute = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/expressions/descriptors"));
        Assert.Equal(HttpStatusCode.NotFound, omittedRoute.StatusCode);

        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        Assert.NotNull(capabilities);
        Assert.Equal(
            ExpectedCapabilities.Where(x => x != "elsa.api.expressions"),
            capabilities.Capabilities.Select(x => x.Id).Order(StringComparer.Ordinal));
    }

    private sealed class CustomManagementHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        private static readonly string[] CommonEndpointTypes =
        [
            "Elsa.Api.Capabilities.Endpoints.GetCapabilities",
            "Elsa.Activities.Design.Api.Endpoints.Catalog.List",
            "Elsa.Workflows.Design.Api.Endpoints.Definitions.List",
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishWorkflowEndpoint",
            "Elsa.Workflows.Runtime.Api.Endpoints.ListWorkflowExecutablesEndpoint"
        ];

        public HttpClient Client { get; } = client;

        public static async Task<CustomManagementHost> StartAsync(bool includeExpressions)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            new ApiCapabilitiesFeature().ConfigureServices(builder.Services);
            new ActivitiesDesignApiFeature().ConfigureServices(builder.Services);
            new WorkflowsDesignApiFeature().ConfigureServices(builder.Services);
            new WorkflowsPublishingApiFeature().ConfigureServices(builder.Services);
            new WorkflowsRuntimeApiFeature().ConfigureServices(builder.Services);

            var endpointTypes = CommonEndpointTypes.ToHashSet(StringComparer.Ordinal);
            var assemblies = new List<System.Reflection.Assembly>
            {
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(ActivitiesDesignApiFeature).Assembly,
                typeof(WorkflowsDesignApiFeature).Assembly,
                typeof(WorkflowsPublishingApiFeature).Assembly,
                typeof(WorkflowsRuntimeApiFeature).Assembly
            };

            if (includeExpressions)
            {
                new ExpressionsApiFeature().ConfigureServices(builder.Services);
                assemblies.Add(typeof(ExpressionsApiFeature).Assembly);
                endpointTypes.Add("Elsa.Expressions.Api.Endpoints.ListExpressionDescriptors");
            }

            builder.Services.AddSingleton<IRequestSender, JourneyRequestSender>();
            builder.Services.AddFastEndpoints(options =>
            {
                options.Assemblies = assemblies.ToArray();
                options.Filter = type => type.FullName is not null && endpointTypes.Contains(type.FullName);
            });

            var app = builder.Build();
            app.UseFastEndpoints(options => options.Endpoints.Configurator = endpoint => endpoint.AllowAnonymous());
            await app.StartAsync();
            return new CustomManagementHost(app, app.GetTestClient());
        }

        public async Task AssertJourneyAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            using var response = await Client.SendAsync(request);

            Assert.True(
                response.IsSuccessStatusCode,
                $"Representative {method} {path} journey returned {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class JourneyRequestSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            object response = typeof(T) switch
            {
                var type when type == typeof(WorkflowDefinitionListView) => new WorkflowDefinitionListView([]),
                var type when type == typeof(ActivityAuthoringCatalogView) => new ActivityAuthoringCatalogView([]),
                var type when type == typeof(ExpressionDescriptorsResponse) => new ExpressionDescriptorsResponse([]),
                var type when type == typeof(PublishedWorkflowView) => new PublishedWorkflowView(
                    "publication-1", "definition-1", "version-1", "version-1", "artifact-1", "default",
                    PublicationStatus.Active, "reference-1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                    null, "1.0.0", "hash-1", "root-1", 1, true),
                var type when type == typeof(Elsa.Workflows.Runtime.Api.Models.WorkflowExecutablesListView) =>
                    new Elsa.Workflows.Runtime.Api.Models.WorkflowExecutablesListView([]),
                _ => throw new InvalidOperationException($"Unexpected representative journey response type '{typeof(T)}'.")
            };

            return Task.FromResult((T)response);
        }
    }
}
