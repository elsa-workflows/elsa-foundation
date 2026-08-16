using Elsa.Api.FastEndpoints.Constants;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>RED route/security contract replacing the legacy workflow-management diagnostics path.</summary>
public sealed class RuntimeDiagnosticsEndpointTests
{
    [Fact]
    public void Diagnostics_settings_get_uses_the_canonical_runtime_route_and_read_permission()
    {
        var type = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Endpoints.RuntimeDiagnostics.GetSettings");
        Assert.NotNull(type);
        var endpoint = RuntimeApiEndpointTestFactory.Create(type!);

        Assert.Contains("runtime/workflows/diagnostics/settings", endpoint.Definition.Routes);
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, PermissionNames.WorkflowRuntimeRead);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Diagnostics_settings_put_uses_the_canonical_runtime_route_and_manage_permission()
    {
        var type = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Endpoints.RuntimeDiagnostics.SaveSettings");
        Assert.NotNull(type);
        var endpoint = RuntimeApiEndpointTestFactory.Create(type!);

        Assert.Contains("runtime/workflows/diagnostics/settings", endpoint.Definition.Routes);
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, PermissionNames.WorkflowRuntimeManage);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

}
