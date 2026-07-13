using System.Reflection;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class WorkflowInstanceListContractTests
{
    [Fact]
    public void Legacy_instance_list_preserves_the_v1_array_contract()
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/instances");
        var (request, response) = RuntimeApiEndpointTestFactory.Contract(endpoint);

        AssertProperties(request,
            "Status", "DefinitionId", "CorrelationId", "Take", "Cursor",
            "WorkflowExecutionId", "ArtifactId", "From", "To", "RunKind");
        Assert.Equal(typeof(IReadOnlyCollection<WorkflowInstanceSummaryView>), response);
        Assert.Contains(PermissionNames.WorkflowRuntimeRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
    }

    [Fact]
    public void Paged_instance_list_uses_an_additive_rel_route_and_envelope()
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/instances/page");
        var (request, response) = RuntimeApiEndpointTestFactory.Contract(endpoint);

        AssertProperties(request,
            "Status", "DefinitionId", "CorrelationId", "Take", "Cursor",
            "WorkflowExecutionId", "ArtifactId", "From", "To", "RunKind");
        AssertProperties(response,
            "Items", "PreviousCursor", "NextCursor", "HasPrevious", "HasNext", "Count", "TotalCount");
        var item = response.GetProperty("Items")!.PropertyType.GetGenericArguments().Single();
        AssertProperties(item, "RunKind");
        Assert.Contains(PermissionNames.WorkflowRuntimeRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);

        var links = RuntimeApiCapabilities.StaticDeclaration.Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);
        Assert.Equal("runtime/workflows/instances", links["workflow-instances"].Href);
        Assert.Equal("runtime/workflows/instances/page", links["workflow-instances-page"].Href);
        Assert.Equal(1, RuntimeApiCapabilities.StaticDeclaration.ContractMajorVersion);
    }

    private static void AssertProperties(Type type, params string[] properties) =>
        Assert.All(properties, property => Assert.NotNull(type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)));
}
