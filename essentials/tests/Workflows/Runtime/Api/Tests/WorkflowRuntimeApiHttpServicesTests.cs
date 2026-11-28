using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Inspection.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MinimalEndpoints;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class WorkflowRuntimeApiHttpServicesTests
{
    [Fact]
    public async Task Retained_mapping_preserves_the_live_runtime_route_contracts()
    {
        await using var broad = BuildApp(includeLegacyUserTasks: true);
        await using var retained = BuildApp(includeLegacyUserTasks: false);
        Assert.Contains(broad.Services.GetServices<IPermissionContributor>().SelectMany(contributor => contributor.Contribute()),
            permission => permission.Category == "User Tasks");
        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(broad);
        WorkflowsRuntimeApi.MapRetainedEndpoints(retained, includeExecute: true, includeDelete: true, includeInputSources: true);

        var broadRoutes = Routes(broad).ToDictionary(route => route.Key, route => route.Endpoint);
        var retainedRoutes = Routes(retained).ToArray();
        Assert.Equal(15, retainedRoutes.Length);
        Assert.Contains(retainedRoutes, route => route.Key == ("POST", "runtime/workflows/executables/{artifactId}/execute"));
        Assert.Contains(retainedRoutes, route => route.Key == ("DELETE", "runtime/workflows/instances/{workflowExecutionId}"));
        Assert.DoesNotContain(retainedRoutes, route => route.Key.Route.Contains("/stimuli", StringComparison.Ordinal) ||
            route.Key.Route.Contains("/dispatches", StringComparison.Ordinal) || route.Key.Route.Contains("/alteration-plans", StringComparison.Ordinal) ||
            route.Key.Route.Contains("/diagnostics", StringComparison.Ordinal));
        foreach (var (key, endpoint) in retainedRoutes)
        {
            var original = broadRoutes[key];
            Assert.Equal(original.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
            Assert.Equal(original.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy),
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy));
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Equal(original.Metadata.GetMetadata<EndpointGroupMetadata>(), endpoint.Metadata.GetMetadata<EndpointGroupMetadata>());
            Assert.Equal(RuntimeFault(original), RuntimeFault(endpoint));
            Assert.Equal(ActivityFault(original), ActivityFault(endpoint));
        }

        static WebApplication BuildApp(bool includeLegacyUserTasks)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddWorkflowRuntimeApiHttpServices(includeLegacyUserTasks);
            return builder.Build();
        }

        static IEnumerable<((string Verb, string Route) Key, RouteEndpoint Endpoint)> Routes(IEndpointRouteBuilder group) =>
            group.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Select(endpoint =>
            {
                var verb = Assert.Single(endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods);
                var route = endpoint.RoutePattern.RawText!.TrimStart('/');
                return ((verb, route), endpoint);
            });

        static (string Operation, bool NotFound, bool Executable, string? Argument)? RuntimeFault(RouteEndpoint endpoint) =>
            endpoint.Metadata.GetMetadata<RuntimeProblemShapeMetadata>() is { } shape
                ? (shape.Operation, shape.NotFoundArms, shape.ExecutableArms, shape.ArgumentDetail) : null;

        static (string Operation, bool History)? ActivityFault(RouteEndpoint endpoint) =>
            endpoint.Metadata.GetMetadata<ActivityInspectionProblemShapeMetadata>() is { } shape ? (shape.Operation, shape.History) : null;
    }

    [Fact]
    public async Task Shell_composes_runtime_endpoint_authority_without_legacy_execution_services()
    {
        const string shellName = "runtime-api-http-test";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"CShells:Shells:{shellName}:Features:{RuntimeApiHttpTestFeature.Name}"] = null
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCShells(shells => shells
            .WithAssemblies(typeof(RuntimeApiHttpTestFeature).Assembly)
            .WithConfigurationProvider(configuration));

        await using var host = services.BuildServiceProvider(validateScopes: true);
        var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(shellName);
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var runtimeServices = scope.ServiceProvider;

        Assert.IsType<HttpContextActivityExecutionInspectionAuthorizationContext>(runtimeServices.GetRequiredService<IActivityInspectionContextAsync>());
        var operatorAccess = Assert.IsType<HttpContextWorkflowRuntimeOperatorAccess>(runtimeServices.GetRequiredService<IWorkflowRuntimeOperatorAccess>());
        Assert.False(await operatorAccess.CanManageRuntimeAsync());
        Assert.Single(runtimeServices.GetServices<IPermissionContributor>().OfType<RetainedWorkflowRuntimePermissionContributor>());
        Assert.DoesNotContain(runtimeServices.GetRequiredService<IPermissionCatalog>().List(), permission => permission.Category == "User Tasks");
        Assert.IsType<WorkflowsRuntimeProblemWriter>(runtimeServices.GetRequiredKeyedService<IEndpointProblemWriter>(WorkflowsRuntimeApi.OwnerId));
        Assert.IsType<WorkflowsRuntimeFaultRenderer>(runtimeServices.GetRequiredKeyedService<IEndpointFaultRenderer>(WorkflowsRuntimeApi.OwnerId));
        Assert.Single(runtimeServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions.TypeInfoResolverChain
            .OfType<WorkflowsRuntimeJsonTypeInfoResolver>());

        Assert.Null(runtimeServices.GetService<IWorkflowSchedulerDrainer>());
        Assert.Null(runtimeServices.GetService<IWorkflowExecutionActorProvider>());
        Assert.Null(runtimeServices.GetService<IWorkflowExecutionStateStore>());
    }

    [ShellFeature(name: Name)]
    public sealed class RuntimeApiHttpTestFeature : IShellFeature
    {
        internal const string Name = "RuntimeApiHttpTest";

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddFoundationIdentityAbstractions();
            services.AddWorkflowRuntimeApiHttpServices(includeLegacyUserTasks: false);
        }
    }
}
