using Elsa.Api.FastEndpoints.Constants;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class RuntimeExecutionEndpointTests
{
    [Fact]
    public void Execute_uses_the_executable_owned_route_and_execute_permission()
    {
        var type = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Endpoints.Execute");
        Assert.NotNull(type);
        var endpoint = RuntimeApiEndpointTestFactory.Create(type!);

        Assert.Contains("runtime/workflows/executables/{artifactId}/execute", endpoint.Definition.Routes);
        Assert.Contains(PermissionNames.WorkflowRuntimeExecute, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }
}
