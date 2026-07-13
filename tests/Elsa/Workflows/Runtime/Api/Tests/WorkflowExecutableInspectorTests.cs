using System.Reflection;
using Elsa.Api.FastEndpoints.Constants;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>RED ownership and HTTP contract for moving executable inspection out of Publishing.</summary>
public sealed class WorkflowExecutableInspectorTests
{
    [Fact]
    public void Runtime_owns_the_self_contained_executable_inspector()
    {
        var inspector = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Services.WorkflowExecutableInspector");
        Assert.NotNull(inspector);
        var dependencies = inspector!.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray();

        Assert.Contains("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableStore", dependencies);
        Assert.Contains("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableSourceReferenceStore", dependencies);
        Assert.Contains("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionStateStore", dependencies);
        Assert.DoesNotContain(dependencies, dependency => dependency?.Contains("Design", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("runtime/workflows/executables")]
    [InlineData("runtime/workflows/executables/{artifactId}")]
    [InlineData("runtime/workflows/executables/{artifactId}/provenance")]
    public void Runtime_owns_each_canonical_executable_read_route(string route)
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute(route);

        Assert.Contains(PermissionNames.WorkflowRuntimeRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Executable_list_exposes_retention_counts_without_definition_reads()
    {
        var response = RuntimeApiEndpointTestFactory.Contract(
            RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/executables")).Response;
        var items = response.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(items);
        var row = ElementType(items!.PropertyType);

        AssertProperties(row, "ArtifactId", "CreatedAt", "LiveSourceReferenceCount", "RetainedExecutionCount");
    }

    [Fact]
    public void Provenance_is_read_only_and_reports_collection_protection()
    {
        var response = RuntimeApiEndpointTestFactory.Contract(
            RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/executables/{artifactId}/provenance")).Response;

        AssertProperties(response, "ArtifactId", "SourceReferences", "RetainedExecutionCount", "ProtectedFromCollection");
    }

    private static Type ElementType(Type collectionType) => collectionType.GetInterfaces().Append(collectionType)
        .First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];

    private static void AssertProperties(Type type, params string[] properties) =>
        Assert.All(properties, property => Assert.NotNull(type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)));
}
