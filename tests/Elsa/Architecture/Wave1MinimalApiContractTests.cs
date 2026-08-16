using System.Text;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Attention.Api;
using Elsa.Expressions.Api;
using Elsa.Expressions.JavaScript.Rendering;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Runtime.JavaScript;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class Wave1MinimalApiContractTests
{
    [Fact]
    public void All_wave_one_owners_publish_explicit_owned_minimal_api_contracts()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        ApiCapabilitiesApi.MapApiCapabilitiesApi(routes);
        AttentionApi.MapAttentionApi(routes);
        ExpressionsApi.MapExpressionsApi(routes);
        JavaScriptRenderingApi.MapJavaScriptRenderingApi(routes);
        JavaScriptExecutionApi.MapJavaScriptExecutionApi(routes);
        WorkflowsDashboardApi.MapWorkflowsDashboardApi(routes);

        var manifest = EndpointManifestBuilder.Capture(routes.DataSources);

        Assert.Equal(8, manifest.Entries.Count);
        Assert.Equal(
            [
                "GET /_elsa/attention/items",
                "GET /_elsa/workflows/dashboard/definitions",
                "GET /_elsa/workflows/dashboard/runs",
                "GET /capabilities",
                "GET /expressions/descriptors",
                "GET /expressions/variable-types",
                "GET /javascript/documents/render",
                "POST /javascript/execute"
            ],
            manifest.Entries.SelectMany(entry => entry.Identities)
                .Select(identity =>
                {
                    var value = identity.ToString();
                    var separator = value.IndexOf(' ');
                    return value[..(separator + 1)] + "/" + value[(separator + 1)..].TrimStart('/');
                })
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Elsa.Api.Capabilities"] = 1,
                ["Elsa.Attention.Api"] = 1,
                ["Elsa.Expressions.Api"] = 2,
                ["Elsa.Expressions.JavaScript.Rendering"] = 1,
                ["Elsa.Workflows.Runtime.JavaScript"] = 1,
                ["Elsa.Workflows.Dashboard"] = 2
            },
            manifest.Entries.GroupBy(entry => entry.Owner, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
        Assert.All(manifest.Entries, entry =>
        {
            Assert.Equal(EndpointAuthoringModels.MinimalApi, entry.AuthoringModel);
            Assert.Equal(EndpointSecurityDispositionKind.Permission, entry.SecurityDisposition!.Kind);
            var policy = new PermissionPolicyCodec().Parse(entry.SecurityDisposition.Value!);
            Assert.Equal(PermissionRequirementMode.Any, policy.Descriptor!.Mode);
            Assert.Contains(PermissionKey.Wildcard, policy.Descriptor.Permissions);
            Assert.Contains(entry.Responses, response => response.StatusCode == StatusCodes.Status401Unauthorized);
            Assert.Contains(entry.Responses, response => response.StatusCode == StatusCodes.Status403Forbidden);
        });
        var expectedPermissions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/_elsa/attention/items"] = AttentionPermissions.Read,
            ["/_elsa/workflows/dashboard/definitions"] = WorkflowsDashboardPermissions.Read,
            ["/_elsa/workflows/dashboard/runs"] = WorkflowsDashboardPermissions.Read,
            ["/capabilities"] = Elsa.Api.Capabilities.Authorization.ApiCapabilitiesPermissions.Read,
            ["/expressions/descriptors"] = Elsa.Expressions.Api.Authorization.ExpressionsPermissions.Read,
            ["/expressions/variable-types"] = Elsa.Expressions.Api.Authorization.ExpressionsPermissions.Read,
            ["/javascript/documents/render"] = JavaScriptRenderingPermissions.Render,
            ["/javascript/execute"] = JavaScriptExecutionPermissions.Execute
        };
        foreach (var entry in manifest.Entries)
        {
            var policy = new PermissionPolicyCodec().Parse(entry.SecurityDisposition!.Value!);
            Assert.Equal(
                [PermissionKey.Wildcard, PermissionKey.Normalize(expectedPermissions["/" + entry.Route.Value.TrimStart('/')])],
                policy.Descriptor!.Permissions);
        }

        var execute = Assert.Single(manifest.Entries, entry => entry.Route.Value.TrimStart('/') == "javascript/execute");
        var executeEndpoint = Assert.Single(
            routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText?.TrimStart('/') == "javascript/execute");
        var accepts = Assert.IsAssignableFrom<IAcceptsMetadata>(executeEndpoint.Metadata.GetMetadata<IAcceptsMetadata>());
        Assert.Equal(["application/json"], accepts.ContentTypes);
        Assert.Contains(execute.Responses, response => response.StatusCode == StatusCodes.Status400BadRequest);
        Assert.Contains(execute.Responses, response => response.StatusCode == StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void Wave_one_permissions_have_one_module_catalog_owner_each()
    {
        var contributors = new IPermissionContributor[]
        {
            new Elsa.Api.Capabilities.Authorization.ApiCapabilitiesPermissionContributor(),
            new AttentionPermissionContributor(),
            new Elsa.Expressions.Api.Authorization.ExpressionsPermissionContributor(),
            new JavaScriptRenderingPermissionContributor(),
            new JavaScriptExecutionPermissionContributor(),
            new WorkflowsDashboardPermissionContributor()
        };
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Elsa.Api.Capabilities.Authorization.ApiCapabilitiesPermissions.Read] = "Elsa.Api.Capabilities",
            [AttentionPermissions.Read] = "Elsa.Attention.Api",
            [Elsa.Expressions.Api.Authorization.ExpressionsPermissions.Read] = "Elsa.Expressions.Api",
            [JavaScriptRenderingPermissions.Render] = "Elsa.Expressions.JavaScript.Rendering",
            [JavaScriptExecutionPermissions.Execute] = "Elsa.Workflows.Runtime.JavaScript",
            [WorkflowsDashboardPermissions.Read] = "Elsa.Workflows.Dashboard"
        };

        foreach (var contributor in contributors)
        {
            var definitions = contributor.Contribute().ToArray();
            Assert.Single(definitions);
            var definition = definitions[0];
            var permission = expected.Single(pair =>
                PermissionKey.Normalize(pair.Key) == PermissionKey.Normalize(definition.Key));
            Assert.Equal(permission.Value, contributor.OwnerId);
            Assert.NotEqual(PermissionKey.Wildcard, PermissionKey.Normalize(definition.Key));
        }
    }

    [Fact]
    public async Task Runtime_javascript_invalid_json_preserves_the_request_error_contract()
    {
        var endpoint = GetEndpoint("javascript/execute");
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{invalid"));
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType?.Split(';')[0]);
    }

    [Fact]
    public async Task Rendering_failure_is_a_json_server_error_and_not_a_framework_exception()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IJavaScriptDeclarationsDocumentFactory>(new ThrowingDocumentFactory())
            .AddSingleton<IJavaScriptDeclarationsDocumentRenderer>(new NoopDocumentRenderer())
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        await GetEndpoint("javascript/documents/render").RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType?.Split(';')[0]);
    }

    [Fact]
    public async Task Dashboard_without_tenant_scope_is_a_request_error()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        await GetEndpoint("/_elsa/workflows/dashboard/definitions").RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("text/plain", context.Response.ContentType?.Split(';')[0]);
    }

    [Fact]
    public async Task OpenApi_preserves_all_wave_one_operation_contracts()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.ApplicationName = "testhost";
        builder.Services.AddOpenApi();
        await using var app = builder.Build();
        ApiCapabilitiesApi.MapApiCapabilitiesApi(app);
        AttentionApi.MapAttentionApi(app);
        ExpressionsApi.MapExpressionsApi(app);
        JavaScriptRenderingApi.MapJavaScriptRenderingApi(app);
        JavaScriptExecutionApi.MapJavaScriptExecutionApi(app);
        WorkflowsDashboardApi.MapWorkflowsDashboardApi(app);
        app.MapOpenApi();
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var expectations = new[]
        {
            new OpenApiExpectation(
                "GET", "/capabilities", "ElsaApiCapabilitiesEndpointsGetCapabilities",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["200"] = ["application/json"], ["401"] = [], ["403"] = []
                }),
            new OpenApiExpectation(
                "GET", "/_elsa/attention/items", "ElsaAttentionApiEndpointsGetAttentionItems",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["200"] = ["application/json"], ["400"] = ["text/plain"], ["401"] = [], ["403"] = []
                }),
            new OpenApiExpectation(
                "GET", "/expressions/descriptors", "ElsaExpressionsApiEndpointsListExpressionDescriptors",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["200"] = ["application/json"], ["401"] = [], ["403"] = []
                }),
            new OpenApiExpectation(
                "GET", "/expressions/variable-types", "ElsaExpressionsApiEndpointsListVariableTypeDescriptors",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["200"] = ["application/json"], ["401"] = [], ["403"] = []
                }),
            new OpenApiExpectation(
                "GET", "/javascript/documents/render", "ElsaExpressionsJavaScriptRenderingEndpointsRenderEndpoint",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["200"] = ["application/json"], ["401"] = [], ["403"] = [], ["500"] = ["application/json"]
                }),
            new OpenApiExpectation(
                "POST", "/javascript/execute", "ElsaWorkflowsRuntimeJavaScriptActivitiesRunJavaScriptEndpoint",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["200"] = ["application/json"], ["400"] = ["application/json"], ["401"] = [], ["403"] = [], ["500"] = ["application/json"]
                },
                RequestContentType: "application/json", RequestSchemaName: "RequestModel"),
            new OpenApiExpectation(
                "GET", "/_elsa/workflows/dashboard/definitions", "ElsaWorkflowsDashboardGetWorkflowPortfolio",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["200"] = ["application/json"], ["400"] = ["text/plain"], ["401"] = [], ["403"] = []
                }),
            new OpenApiExpectation(
                "GET", "/_elsa/workflows/dashboard/runs", "ElsaWorkflowsDashboardGetWorkflowRunHealth",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["200"] = ["application/json"], ["400"] = ["text/plain"], ["401"] = [], ["403"] = []
                })
        };

        foreach (var expected in expectations)
        {
            var operation = paths.GetProperty(expected.Path).GetProperty(expected.Method.ToLowerInvariant());
            Assert.Equal(expected.OperationId, operation.GetProperty("operationId").GetString());
            Assert.Equal(["testhost"], operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()!).ToArray());

            var responses = operation.GetProperty("responses");
            Assert.Equal(expected.Responses.Keys.Order(StringComparer.Ordinal), responses.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            foreach (var expectedResponse in expected.Responses)
            {
                var responseMetadata = responses.GetProperty(expectedResponse.Key);
                if (expectedResponse.Value.Length == 0)
                {
                    Assert.False(responseMetadata.TryGetProperty("content", out _));
                    continue;
                }

                var content = responseMetadata.GetProperty("content");
                Assert.Equal(expectedResponse.Value.Order(StringComparer.Ordinal), content.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
                Assert.All(expectedResponse.Value, contentType => Assert.True(content.GetProperty(contentType).TryGetProperty("schema", out _)));
            }

            if (expected.RequestContentType is null)
            {
                Assert.False(operation.TryGetProperty("requestBody", out _));
                continue;
            }

            var requestBody = operation.GetProperty("requestBody").GetProperty("content").GetProperty(expected.RequestContentType);
            Assert.True(requestBody.GetProperty("schema").GetProperty("$ref").GetString()?.Contains(expected.RequestSchemaName!, StringComparison.Ordinal) ?? false);
        }
    }

    private sealed record OpenApiExpectation(
        string Method,
        string Path,
        string OperationId,
        IReadOnlyDictionary<string, string[]> Responses,
        string? RequestContentType = null,
        string? RequestSchemaName = null);

    private static RouteEndpoint GetEndpoint(string route)
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        ApiCapabilitiesApi.MapApiCapabilitiesApi(routes);
        AttentionApi.MapAttentionApi(routes);
        ExpressionsApi.MapExpressionsApi(routes);
        JavaScriptRenderingApi.MapJavaScriptRenderingApi(routes);
        JavaScriptExecutionApi.MapJavaScriptExecutionApi(routes);
        WorkflowsDashboardApi.MapWorkflowsDashboardApi(routes);
        var endpoint = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => candidate.RoutePattern.RawText == route);
        Assert.NotNull(endpoint);
        return endpoint!;
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class ThrowingDocumentFactory : IJavaScriptDeclarationsDocumentFactory
    {
        public ValueTask<JavaScriptDeclarationsDocument> Create(CancellationToken cancellationToken = default) =>
            ValueTask.FromException<JavaScriptDeclarationsDocument>(new InvalidOperationException("render unavailable"));
    }

    private sealed class NoopDocumentRenderer : IJavaScriptDeclarationsDocumentRenderer
    {
        public string Render(JavaScriptDeclarationsDocument typeDocument) => "{}";
    }
}
