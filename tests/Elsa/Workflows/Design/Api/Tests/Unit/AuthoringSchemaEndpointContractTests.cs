using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>Preserves the read-only authoring schema and structure registry surface.</summary>
public sealed class AuthoringSchemaEndpointContractTests
{
    [Theory]
    [InlineData("design/workflows/definitions/submit/schema")]
    [InlineData("design/workflows/structures")]
    public void Authoring_schema_route_is_a_read_only_design_endpoint(string route)
    {
        var endpoint = WorkflowDesignEndpointTestSupport.Find(WorkflowDesignEndpointTestSupport.MapEndpoints(), route, "GET");
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(security.Value!));
        Assert.Contains(PermissionKey.Normalize(WorkflowDesignPermissions.Read), policy.Descriptor!.Permissions);
        Assert.NotNull(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IProducesResponseTypeMetadata>());
    }
}
