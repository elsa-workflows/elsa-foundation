using System.Reflection;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
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
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, WorkflowRuntimePermissions.WorkflowRuntimeRead);
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
            "Items", "NextCursor", "HasNext", "Count", "TotalCount");
        var item = response.GetProperty("Items")!.PropertyType.GetGenericArguments().Single();
        AssertProperties(item, "RunKind");
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, WorkflowRuntimePermissions.WorkflowRuntimeRead);

        var links = RuntimeApiCapabilities.StaticDeclaration.Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);
        Assert.Equal("runtime/workflows/instances", links["workflow-instances"].Href);
        Assert.Equal("runtime/workflows/instances/page", links["workflow-instances-page"].Href);
        Assert.Equal(1, RuntimeApiCapabilities.StaticDeclaration.ContractMajorVersion);
    }

    [Fact]
    public void Runtime_capability_advertises_every_reusable_boundary_inspection_relation()
    {
        var links = RuntimeApiCapabilities.StaticDeclaration.Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);

        string[] relations =
        [
            "activity-execution-boundary-detail",
            "activity-execution-descendants",
            "activity-execution-layout",
            "activity-execution-attempt-lineage",
            "activity-execution-bookmarks",
            "activity-execution-incidents",
            "activity-execution-value-evidence",
            "activity-execution-value-payload"
        ];
        Assert.All(relations, relation => Assert.True(links[relation].Templated));
    }

    [Theory]
    [InlineData("runtime/workflows/instances", "LegacyArray")]
    [InlineData("runtime/workflows/instances/page", "Paged")]
    public async Task Each_route_selects_its_own_page_size_contract(string route, string expectedContract)
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute(route);
        Assert.Equal(typeof(ListWorkflowInstances), endpoint.Request);
        var request = new ListWorkflowInstances(null, null, null, Take: null);
        var normalized = expectedContract == "LegacyArray" ? request.ForLegacyArray() : request;
        var contract = typeof(ListWorkflowInstances).GetProperty("PagingContract", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(contract);
        Assert.Equal(expectedContract, contract!.GetValue(normalized)!.ToString());
    }

    private static void AssertProperties(Type type, params string[] properties) =>
        Assert.All(properties, property => Assert.NotNull(type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)));

}
