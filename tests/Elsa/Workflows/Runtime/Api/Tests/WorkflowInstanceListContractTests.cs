using System.Reflection;
using Elsa.Api.FastEndpoints.Constants;
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
            "Items", "NextCursor", "HasNext", "Count", "TotalCount");
        var item = response.GetProperty("Items")!.PropertyType.GetGenericArguments().Single();
        AssertProperties(item, "RunKind");
        Assert.Contains(PermissionNames.WorkflowRuntimeRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);

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
        var sender = new CapturingRequestSender();
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute(route);
        endpoint = RuntimeApiEndpointTestFactory.Create(endpoint.GetType(), sender);
        var handle = endpoint.GetType().GetMethod("HandleAsync", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(handle);
        await Assert.IsAssignableFrom<Task>(handle!.Invoke(
            endpoint,
            [new ListWorkflowInstances(null, null, null, Take: null), CancellationToken.None]));

        var contract = typeof(ListWorkflowInstances).GetProperty("PagingContract", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(contract);
        Assert.Equal(expectedContract, contract!.GetValue(sender.Request)!.ToString());
    }

    private static void AssertProperties(Type type, params string[] properties) =>
        Assert.All(properties, property => Assert.NotNull(type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)));

    private sealed class CapturingRequestSender : IRequestSender
    {
        public ListWorkflowInstances? Request { get; private set; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            Request = Assert.IsType<ListWorkflowInstances>(request);
            return Task.FromResult((T)(object)new WorkflowInstanceListView([], null, false, 0, 0));
        }
    }
}
