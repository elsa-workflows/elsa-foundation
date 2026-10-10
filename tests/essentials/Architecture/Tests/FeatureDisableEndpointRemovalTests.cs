using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using NativeEndpoints;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Disabling a feature must retire its endpoints from the live host without a restart: the routes stop
/// resolving and the OpenAPI document stops advertising them.
/// </summary>
/// <remarks>
/// This is deliberately separate from the collectibility suites, because it is a different guarantee with a
/// different mechanism. Those suites prove an <c>AssemblyLoadContext</c> can be collected, which is what the
/// stable <c>*.Api.Core</c> contract split exists to enable. This proves *document and routing correctness*
/// after removal, which depends only on the endpoint data source changing and
/// <see cref="NativeEndpointsServiceCollectionExtensions.AddDynamicEndpointApiExplorerRefresh"/> projecting
/// that change into API Explorer's invalidation seam. Nothing here requires the owning assembly to unload,
/// so a module whose endpoints can be disabled at runtime does not, on this evidence alone, need its
/// contracts split into a separate stable assembly.
/// </remarks>
public sealed class FeatureDisableEndpointRemovalTests
{
    [Fact]
    public async Task Disabling_a_feature_removes_its_routes_and_openapi_operations_without_a_restart()
    {
        var source = new MutableEndpointDataSource([Endpoint("/feature/enabled", "FeatureEnabled")]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddSingleton<EndpointDataSource>(source);
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddOpenApi();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var documents = provider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");

        var enabled = await documents.GetOpenApiDocumentAsync(CancellationToken.None);
        Assert.True(enabled.Paths?.ContainsKey("/feature/enabled"),
            "The endpoint should be advertised while its feature is enabled.");
        Assert.Contains(source.Endpoints.OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == "/feature/enabled");

        // Disable the feature: its generation contributes no endpoints. The assembly stays loaded.
        source.Replace([]);

        var disabled = await documents.GetOpenApiDocumentAsync(CancellationToken.None);
        Assert.True(disabled.Paths is null || !disabled.Paths.ContainsKey("/feature/enabled"),
            "The OpenAPI document still advertises a route whose feature was disabled.");
        Assert.Empty(source.Endpoints);
    }

    [Fact]
    public async Task Re_enabling_a_feature_restores_its_openapi_operations()
    {
        var source = new MutableEndpointDataSource([Endpoint("/feature/toggled", "FeatureToggled")]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddSingleton<EndpointDataSource>(source);
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddOpenApi();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var documents = provider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");

        _ = await documents.GetOpenApiDocumentAsync(CancellationToken.None);
        source.Replace([]);
        var disabled = await documents.GetOpenApiDocumentAsync(CancellationToken.None);
        source.Replace([Endpoint("/feature/toggled", "FeatureToggled")]);
        var reEnabled = await documents.GetOpenApiDocumentAsync(CancellationToken.None);

        Assert.True(disabled.Paths is null || !disabled.Paths.ContainsKey("/feature/toggled"));
        Assert.True(reEnabled.Paths?.ContainsKey("/feature/toggled"),
            "Re-enabling a feature should re-advertise its endpoint without a restart.");
    }

    private static RouteEndpoint Endpoint(string route, string name)
    {
        RequestDelegate handler = context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        };
        var builder = new RouteEndpointBuilder(handler, RoutePatternFactory.Parse(route), order: 0)
        {
            DisplayName = name
        };
        builder.Metadata.Add(new HttpMethodMetadata([HttpMethods.Get]));
        // API Explorer needs a MethodInfo to derive an ApiDescription; without it the endpoint never
        // reaches the OpenAPI document and this test would pass vacuously.
        builder.Metadata.Add(typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))!);
        builder.Metadata.Add(new EndpointNameMetadata(name));
        return (RouteEndpoint)builder.Build();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = typeof(FeatureDisableEndpointRemovalTests).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class MutableEndpointDataSource(IReadOnlyList<Endpoint> endpoints) : EndpointDataSource
    {
        private IReadOnlyList<Endpoint> _endpoints = endpoints;
        private CancellationTokenSource _changeTokenSource = new();

        public override IReadOnlyList<Endpoint> Endpoints => Volatile.Read(ref _endpoints);

        public override IChangeToken GetChangeToken() =>
            new CancellationChangeToken(Volatile.Read(ref _changeTokenSource).Token);

        public void Replace(IReadOnlyList<Endpoint> endpoints)
        {
            Volatile.Write(ref _endpoints, endpoints);
            var previous = Interlocked.Exchange(ref _changeTokenSource, new CancellationTokenSource());
            try
            {
                previous.Cancel();
            }
            finally
            {
                previous.Dispose();
            }
        }
    }
}
