using System.Text.Json;
using System.Net.Http.Json;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Server;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Extensions;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class WorkflowManagementScopedVariableApiTests
{
    [Fact]
    public void Route_registration_includes_capabilities_and_scoped_variable_analysis()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<IShellRegistry>(_ => null!);
        var app = builder.Build();

        app.MapElsaWorkflowManagementApi();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(x => (Pattern: x.RoutePattern.RawText, Methods: x.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods))
            .ToArray();

        Assert.Contains(routes, x => x.Pattern == "/_elsa/workflow-management/capabilities" && x.Methods?.SequenceEqual(["GET"]) == true);
        Assert.Contains(routes, x => x.Pattern == "/_elsa/workflow-management/design/scoped-variables/analyze" && x.Methods?.SequenceEqual(["POST"]) == true);
    }

    [Fact]
    public async Task Analysis_returns_camel_case_contract_with_empty_visibility_for_null_selection()
    {
        var services = BuildServices();
        var state = new WorkflowDefinitionStateView(
            Variables: [new VariableDefinition("var-wf", "Counter", new TypeReference("String"), null, null)],
            RootActivity: new ActivityNode("root", "activity-v1", [], [], null));

        var result = ElsaWorkflowManagementApi.AnalyzeScopedVariables(
            services,
            new AnalyzeScopedVariablesRequest(state, null));
        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("visibleVariables").ValueKind);
        Assert.Empty(document.RootElement.GetProperty("visibleVariables").EnumerateArray());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("shadowingWarnings").ValueKind);
    }

    [Fact]
    public async Task Mapped_analysis_post_binds_json_and_resolves_the_active_shell()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(AnalysisProbeFeature).Assembly)
            .AddShell("default", shell => shell.WithFeature<AnalysisProbeFeature>()));

        await using var app = builder.Build();
        app.MapShells();
        app.MapElsaWorkflowManagementApi();
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var state = new WorkflowDefinitionStateView(
            Variables: [new VariableDefinition("var-wf", "Counter", new TypeReference("String"), null, null)],
            RootActivity: new ActivityNode("root", "activity-v1", [], [], null));

        var response = await client.PostAsJsonAsync(
            "/_elsa/workflow-management/design/scoped-variables/analyze",
            new AnalyzeScopedVariablesRequest(state, null));

        Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.GetProperty("visibleVariables").EnumerateArray());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("shadowingWarnings").ValueKind);
    }

    [Fact]
    public async Task Analysis_rejects_a_missing_state_with_a_stable_error_contract()
    {
        var result = ElsaWorkflowManagementApi.AnalyzeScopedVariables(
            BuildServices(),
            new AnalyzeScopedVariablesRequest(null, "root"));

        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("A workflow definition state is required.", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Analysis_rejects_a_whitespace_node_id_but_allows_null_for_no_selection()
    {
        var result = ElsaWorkflowManagementApi.AnalyzeScopedVariables(
            BuildServices(),
            new AnalyzeScopedVariablesRequest(new WorkflowDefinitionStateView(), "  "));

        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("The selected activity node id cannot be empty.", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Capabilities_reflect_whether_the_active_shell_registered_analysis()
    {
        var supported = await ExecuteResultAsync(ElsaWorkflowManagementApi.GetCapabilities(BuildServices()));
        var unsupported = await ExecuteResultAsync(ElsaWorkflowManagementApi.GetCapabilities(new ServiceCollection().BuildServiceProvider()));

        using var supportedJson = JsonDocument.Parse(supported.Body);
        using var unsupportedJson = JsonDocument.Parse(unsupported.Body);
        Assert.True(supportedJson.RootElement.GetProperty("scopedVariableAnalysis").GetBoolean());
        Assert.False(unsupportedJson.RootElement.GetProperty("scopedVariableAnalysis").GetBoolean());
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddScopedVariableAuthoring();
        return services.BuildServiceProvider();
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    public sealed class AnalysisProbeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) => services.AddScopedVariableAuthoring();
    }
}
