using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Workflows.Dashboard.Tests;

public sealed class WorkflowDashboardEndpointSecurityTests
{
    [Fact]
    public void RunHealthEndpointRequiresExplicitDashboardReadPermissionAndIsNotAnonymous()
    {
        var endpoint = ConfiguredEndpoint("Elsa.Workflows.Dashboard.GetWorkflowRunHealth", new StubService());

        Assert.Contains(WorkflowsDashboardPermissions.Read, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void PortfolioEndpointRequiresExplicitDashboardReadPermissionAndIsNotAnonymous()
    {
        var endpoint = ConfiguredEndpoint("Elsa.Workflows.Dashboard.GetWorkflowPortfolio", new StubPortfolioService());

        Assert.Contains(WorkflowsDashboardPermissions.Read, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    private static BaseEndpoint ConfiguredEndpoint(string typeName, object dependency)
    {
        var endpointType = typeof(WorkflowsDashboardFeature).Assembly.GetType(typeName, throwOnError: true)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters() is [var first, var rest]
                              && first.ParameterType == typeof(Action<DefaultHttpContext>)
                              && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        var endpoint = (BaseEndpoint)create.Invoke(null,
            [(Action<DefaultHttpContext>)(_ => { }), new[] { dependency }])!;

        endpoint.Configure();
        return endpoint;
    }

    private sealed class StubService : IWorkflowRunHealthService
    {
        public ValueTask<WorkflowRunHealthSnapshot> QueryAsync(
            WorkflowRunHealthQuery query,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Configuration-only endpoint test.");
    }

    private sealed class StubPortfolioService : IWorkflowPortfolioService
    {
        public ValueTask<WorkflowPortfolioSnapshot> QueryAsync(string tenantId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Configuration-only endpoint test.");
    }
}
