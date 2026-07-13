using Elsa.Api.FastEndpoints.Constants;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>RED route/security contract replacing the legacy workflow-management diagnostics path.</summary>
public sealed class RuntimeDiagnosticsEndpointTests
{
    [Fact]
    public void Diagnostics_settings_get_uses_the_canonical_runtime_route_and_read_permission()
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/diagnostics/settings");

        Assert.Contains(PermissionNames.WorkflowRuntimeRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Diagnostics_settings_put_uses_the_canonical_runtime_route_and_manage_permission()
    {
        var endpoints = typeof(WorkflowsRuntimeApiFeature).Assembly.GetTypes()
            .Where(type => type.FullName == "Elsa.Workflows.Runtime.Api.Endpoints.RuntimeDiagnostics.SaveSettings")
            .ToArray();
        var type = Assert.Single(endpoints);
        var endpoint = CreateByType(type);

        Assert.Contains("runtime/workflows/diagnostics/settings", endpoint.Definition.Routes);
        Assert.Contains(PermissionNames.WorkflowRuntimeManage, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    private static FastEndpoints.BaseEndpoint CreateByType(Type type)
    {
        // Route lookup cannot distinguish GET from PUT, so reuse the factory's configured assembly endpoint by
        // selecting the concrete endpoint type through the same reflection path.
        var method = typeof(RuntimeApiEndpointTestFactory).GetMethod("Create", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (FastEndpoints.BaseEndpoint)method.Invoke(null, [type])!;
    }
}
