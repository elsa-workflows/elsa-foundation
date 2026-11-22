using Elsa.Workflows.Runtime.Api.Authorization;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>RED route/security contract replacing the legacy workflow-management diagnostics path.</summary>
public sealed class RuntimeDiagnosticsEndpointTests
{
    [Fact]
    public void Diagnostics_settings_get_uses_the_canonical_runtime_route_and_read_permission()
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/diagnostics/settings");

        Assert.Contains("runtime/workflows/diagnostics/settings", endpoint.Definition.Routes);
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, WorkflowRuntimePermissions.WorkflowRuntimeRead);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Diagnostics_settings_put_uses_the_canonical_runtime_route_and_manage_permission()
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/diagnostics/settings", "PUT");

        Assert.Contains("runtime/workflows/diagnostics/settings", endpoint.Definition.Routes);
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, WorkflowRuntimePermissions.WorkflowRuntimeManage);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

}
