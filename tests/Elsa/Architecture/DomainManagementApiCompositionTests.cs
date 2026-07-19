using System.Net;
using System.Net.Http.Json;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Elsa.Expressions;
using Elsa.Expressions.Api;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
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

        var expressions = await host.Client.GetFromJsonAsync<ExpressionDescriptorsResponse>("/expressions/descriptors");
        Assert.NotNull(expressions);
        Assert.Equal(new[] { "Input", "Literal", "Object", "Variable" }, expressions.Items.Select(x => x.Type));
        Assert.Equal(
            new[]
            {
                ExpressionEditingModeView.Reference,
                ExpressionEditingModeView.Literal,
                ExpressionEditingModeView.Structured,
                ExpressionEditingModeView.Reference
            },
            expressions.Items.Select(x => x.EditingMode));

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

    [Fact]
    public async Task Activity_availability_diagnostics_is_retrievable_without_request_input()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false);

        var diagnostics = await host.Client.GetFromJsonAsync<ActivityAvailabilityDiagnostics>(
            "/design/activities/availability/diagnostics");

        Assert.NotNull(diagnostics);
        Assert.Empty(diagnostics.Items);
        Assert.Empty(diagnostics.Sets);
    }

    [Fact]
    public async Task Workflow_definition_list_binds_paging_and_sort_query_parameters_at_the_public_host()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false);

        var response = await host.Client.GetFromJsonAsync<WorkflowDefinitionListView>(
            "/design/workflows/definitions?state=all&searchTerm=invoice&page=2&pageSize=3&sortBy=createdAt&sortDirection=desc");

        Assert.NotNull(response);
        Assert.Equal(2, response.Page);
        Assert.Equal(3, response.PageSize);
        Assert.Equal(0, response.TotalCount);
        Assert.Empty(response.Items);
        Assert.Equal(
            new ListDefinitions(null, null, "invoice", null, "all", Page: 2, PageSize: 3, SortBy: "createdAt", SortDirection: "desc"),
            host.RequestSender.LastWorkflowDefinitionRequest);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=2")]
    [InlineData("sortBy=priority")]
    [InlineData("sortDirection=sideways")]
    public async Task Workflow_definition_list_returns_bad_request_for_invalid_public_paging_and_sort_input(string query)
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false);

        var response = await host.Client.GetAsync($"/design/workflows/definitions?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class CustomManagementHost(WebApplication app, HttpClient client, JourneyRequestSender requestSender) : IAsyncDisposable
    {
        private static readonly string[] CommonEndpointTypes =
        [
            "Elsa.Api.Capabilities.Endpoints.GetCapabilities",
            "Elsa.Activities.Design.Api.Endpoints.Catalog.List",
            "Elsa.Activities.Design.Api.Endpoints.Availability.ListDiagnostics",
            "Elsa.Workflows.Design.Api.Endpoints.Definitions.List",
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishWorkflowEndpoint",
            "Elsa.Workflows.Runtime.Api.Endpoints.ListWorkflowExecutablesEndpoint"
        ];

        public HttpClient Client { get; } = client;
        public JourneyRequestSender RequestSender { get; } = requestSender;

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
                new ExpressionsFeature().ConfigureServices(builder.Services);
                new ExpressionsApiFeature().ConfigureServices(builder.Services);
                assemblies.Add(typeof(ExpressionsApiFeature).Assembly);
                endpointTypes.Add("Elsa.Expressions.Api.Endpoints.ListExpressionDescriptors");
            }

            builder.Services.AddSingleton<JourneyRequestSender>();
            builder.Services.AddSingleton<IRequestSender>(services => services.GetRequiredService<JourneyRequestSender>());
            builder.Services.AddFastEndpoints(options =>
            {
                options.Assemblies = assemblies.ToArray();
                options.Filter = type => type.FullName is not null && endpointTypes.Contains(type.FullName);
            });

            var app = builder.Build();
            app.UseFastEndpoints(options => options.Endpoints.Configurator = endpoint => endpoint.AllowAnonymous());
            await app.StartAsync();
            return new CustomManagementHost(
                app,
                app.GetTestClient(),
                app.Services.GetRequiredService<JourneyRequestSender>());
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

    private sealed class JourneyRequestSender(IServiceProvider services) : IRequestSender
    {
        public ListDefinitions? LastWorkflowDefinitionRequest { get; private set; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            if (request is ListExpressionDescriptors expressionDescriptors)
                return HandleExpressionDescriptors<T>(expressionDescriptors, cancellationToken);

            if (request is ListDefinitions workflowDefinitions)
            {
                ValidateWorkflowDefinitionList(workflowDefinitions);
                LastWorkflowDefinitionRequest = workflowDefinitions;
                return Task.FromResult((T)(object)new WorkflowDefinitionListView(
                    [],
                    workflowDefinitions.Page,
                    workflowDefinitions.PageSize,
                    0));
            }

            object response = typeof(T) switch
            {
                var type when type == typeof(ActivityAuthoringCatalogView) => new ActivityAuthoringCatalogView([]),
                var type when type == typeof(ActivityAvailabilityDiagnostics) => new ActivityAvailabilityDiagnostics([], []),
                var type when type == typeof(PublishedWorkflowView) => new PublishedWorkflowView(
                    "publication-1", "definition-1", "version-1", "version-1", "artifact-1", "default",
                    PublicationStatusView.Active, "reference-1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                    null, "1.0.0", "hash-1", "root-1", 1, true),
                var type when type == typeof(Elsa.Workflows.Runtime.Api.Models.WorkflowExecutablesListView) =>
                    new Elsa.Workflows.Runtime.Api.Models.WorkflowExecutablesListView([]),
                _ => throw new InvalidOperationException($"Unexpected representative journey response type '{typeof(T)}'.")
            };

            return Task.FromResult((T)response);
        }

        private static void ValidateWorkflowDefinitionList(ListDefinitions request)
        {
            var query = new WorkflowDefinitionListQuery(
                new WorkflowDefinitionFilter(),
                request.State?.ToLowerInvariant() == "all" ? WorkflowDefinitionLifecycleScope.All : WorkflowDefinitionLifecycleScope.Active,
                request.SortBy?.ToLowerInvariant() switch
                {
                    null or "" => WorkflowDefinitionSortBy.Name,
                    "name" => WorkflowDefinitionSortBy.Name,
                    "lastmodifiedat" => WorkflowDefinitionSortBy.LastModifiedAt,
                    "createdat" => WorkflowDefinitionSortBy.CreatedAt,
                    _ => throw new ArgumentException("Invalid sortBy.")
                },
                request.SortDirection?.ToLowerInvariant() switch
                {
                    null or "" or "asc" => WorkflowDefinitionSortDirection.Asc,
                    "desc" => WorkflowDefinitionSortDirection.Desc,
                    _ => throw new ArgumentException("Invalid sortDirection.")
                },
                request.Page,
                request.PageSize);
            query.Validate();
        }

        private async Task<T> HandleExpressionDescriptors<T>(
            ListExpressionDescriptors request,
            CancellationToken cancellationToken) where T : notnull
        {
            var handler = services.GetRequiredService<IRequestHandler<ListExpressionDescriptors, ExpressionDescriptorsResponse>>();
            return (T)(object)await handler.Handle(request, cancellationToken);
        }
    }
}
