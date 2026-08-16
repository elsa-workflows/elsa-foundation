using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>Guards the definition and version mutation/read permission split after the FastEndpoints retirement.</summary>
public sealed class DefinitionEndpointSecurityTests
{
    [Theory]
    [InlineData("GET", "design/workflows/definitions", "workflow-design.read")]
    [InlineData("POST", "design/workflows/definitions", "workflow-design.manage")]
    [InlineData("GET", "design/workflows/definitions/{definitionId}", "workflow-design.read")]
    [InlineData("PATCH", "design/workflows/definitions/{definitionId}", "workflow-design.manage")]
    [InlineData("DELETE", "design/workflows/definitions/{definitionId}", "workflow-design.manage")]
    [InlineData("POST", "design/workflows/versions/ingest", "workflow-design.manage")]
    [InlineData("GET", "design/workflows/versions/{versionId}", "workflow-design.read")]
    public void Definition_and_version_endpoints_require_the_expected_permission(string method, string route, string permission)
    {
        var endpoint = WorkflowDesignEndpointTestSupport.Find(WorkflowDesignEndpointTestSupport.MapEndpoints(), route, method);
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(security.Value!));
        Assert.Contains(PermissionKey.Normalize(permission), policy.Descriptor!.Permissions);
        Assert.Contains(PermissionKey.Normalize(PermissionKey.Wildcard), policy.Descriptor.Permissions);
        Assert.NotNull(endpoint.Metadata.GetMetadata<AuthorizeAttribute>());
    }
}
