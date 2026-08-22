using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Tests.Support;
using Microsoft.AspNetCore.Http.Metadata;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>Preserves the authoring route, binding, and permission contract during the mapper migration.</summary>
public sealed class AuthoringEndpointContractTests
{
    [Theory]
    [InlineData("design/workflows/scoped-variables/analyze", "POST")]
    [InlineData("design/workflows/activities/{activityVersionId}/inputs/{inputName}/options", "POST")]
    public void Workflow_design_owns_the_canonical_authoring_route(string route, string method)
    {
        var endpoint = WorkflowDesignEndpointTestSupport.Find(WorkflowDesignEndpointTestSupport.MapEndpoints(), route, method);
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(security.Value!));
        Assert.Contains(PermissionKey.Normalize(WorkflowDesignPermissions.Read), policy.Descriptor!.Permissions);
        Assert.NotNull(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
    }

    [Fact]
    public void Scoped_variable_analysis_request_and_response_match_the_management_contract()
    {
        AssertProperties(typeof(AnalyzeScopedVariablesRequest), "State", "NodeId");
        AssertProperties(typeof(ScopedVariableAnalysisResponse), "VisibleVariables", "ShadowingWarnings");
    }

    [Fact]
    public void Contextual_input_options_bind_route_and_workflow_context_without_cacheable_output()
    {
        var endpoint = WorkflowDesignEndpointTestSupport.Find(
            WorkflowDesignEndpointTestSupport.MapEndpoints(),
            "design/workflows/activities/{activityVersionId}/inputs/{inputName}/options", "POST");
        var accepts = Assert.IsAssignableFrom<IAcceptsMetadata>(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
        Assert.Equal(typeof(ActivityInputOptionsRequest), accepts.RequestType);
        AssertProperties(typeof(ActivityInputOptionsRequest), "ActivityVersionId", "InputName", "NodeId", "WorkflowState");
        AssertProperties(typeof(ActivityInputOptionsResponse), "Options");
    }

    private static void AssertProperties(Type type, params string[] names) =>
        Assert.All(names, name => Assert.NotNull(type.GetProperty(name)));
}
