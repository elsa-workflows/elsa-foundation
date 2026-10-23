using System.Text.Json;
using Elsa.Events;
using Elsa.Expressions.JavaScript.Rendering;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Testing;
using Elsa.Workflows.Design.JavaScript;
using Elsa.Workflows.Design.JavaScript.Contributors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class JavaScriptDeclarationSurfaceTests
{
    [Fact]
    public async Task Workflow_design_renders_only_the_readonly_args_global_for_binding_pure_expressions()
    {
        var document = await RenderDeclarationsAsync();

        Assert.Equal("declare const args: Readonly<Record<string, any>>;\n", document.ReplaceLineEndings("\n"));
    }

    // Kept beside the composition test: asserts what the feature must not register (removed ambient contributors).
    [Fact]
    public void Workflow_design_does_not_register_removed_ambient_declaration_contributors()
    {
        var services = new ServiceCollection();
        new JavaScriptWorkflowsDesignFeature().ConfigureServices(services);

        var implementations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IJavaScriptDeclarationContributor))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        Assert.Equal([typeof(BindingPureArgsDeclarationContributor)], implementations);
    }

    [Fact]
    public void JavaScript_rendering_has_no_default_host_function_or_ambient_variable_declarations()
    {
        var feature = new JavaScriptRenderingFeature();

        Assert.Empty(feature.FunctionDeclarations);
        Assert.Empty(feature.VariableDeclarations);
    }

    /// <summary>
    /// The declarations document as the public rendering endpoint serves it when the workflow design feature is its only
    /// contributor: the rendering feature's own contributors are removed before the workflow design feature is added. The
    /// document factory and renderer behind the endpoint are internal to the rendering feature.
    /// </summary>
    private static async Task<string> RenderDeclarationsAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        new EventsFeature().ConfigureServices(services);
        new JavaScriptRenderingFeature().ConfigureServices(services);
        new JavaScriptRenderingEndpointsFeature().ConfigureServices(services);
        services.RemoveAll<IJavaScriptDeclarationContributor>();
        new JavaScriptWorkflowsDesignFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();

        var routes = new TestEndpointRouteBuilder(provider);
        new JavaScriptRenderingEndpointsFeature().MapEndpoints(routes, environment: null);
        var endpoint = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "javascript/documents/render");

        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, Response = { Body = new MemoryStream() } };
        await endpoint.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean(), response.RootElement.ToString());
        return response.RootElement.GetProperty("document").GetString()!;
    }
}
