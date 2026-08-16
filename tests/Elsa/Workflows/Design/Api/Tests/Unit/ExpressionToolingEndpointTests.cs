using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>Guards the expression tooling operation names/routes and shared read permission.</summary>
public sealed class ExpressionToolingEndpointTests
{
    public static TheoryData<string, string> ToolingEndpoints => new()
    {
        { "AuthoringResolveExpressionToolingContext", "design/workflows/expression-tooling/context" },
        { "AuthoringSearchExpressionToolingSymbols", "design/workflows/expression-tooling/symbols" },
        { "AuthoringCompleteExpressionTooling", "design/workflows/expression-tooling/completions" },
        { "AuthoringHoverExpressionTooling", "design/workflows/expression-tooling/hover" },
        { "AuthoringValidateExpressionTooling", "design/workflows/expression-tooling/validate" },
        { "AuthoringDescribeExpressionTooling", "design/workflows/expression-tooling/descriptors" }
    };

    [Theory]
    [MemberData(nameof(ToolingEndpoints))]
    public void Tooling_endpoints_require_design_read_permission_before_the_handler_runs(string operation, string route)
    {
        var endpoint = WorkflowDesignEndpointTestSupport.MapEndpoints().Single(candidate =>
            candidate.RoutePattern.RawText == route && candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == $"ElsaWorkflowsDesignApiEndpoints{operation}");
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(security.Value!));
        Assert.Contains(PermissionKey.Normalize(WorkflowDesignPermissions.Read), policy.Descriptor!.Permissions);
        Assert.NotNull(endpoint.Metadata.GetMetadata<AuthorizeAttribute>());
    }
}
