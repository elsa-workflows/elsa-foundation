using System.Reflection;
using Elsa.Api.FastEndpoints.Constants;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class WorkflowInstanceListContractTests
{
    [Fact]
    public void Instance_list_exposes_cursor_filters_and_page_metadata()
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/instances");
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
    }

    private static void AssertProperties(Type type, params string[] properties) =>
        Assert.All(properties, property => Assert.NotNull(type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)));
}
