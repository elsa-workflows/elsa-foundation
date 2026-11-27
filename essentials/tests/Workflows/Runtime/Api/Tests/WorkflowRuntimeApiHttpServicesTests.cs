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
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MinimalEndpoints;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class WorkflowRuntimeApiHttpServicesTests
{
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
        Assert.Single(runtimeServices.GetServices<IPermissionContributor>().OfType<WorkflowRuntimePermissionContributor>());
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
            services.AddWorkflowRuntimeApiHttpServices();
        }
    }
}
