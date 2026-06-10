using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeDependencyBoundaryTests
{
    [Fact]
    public void RuntimeCoreContracts_DoNotReferenceWorkflowDesignAssemblies()
    {
        var referencedAssemblies = typeof(WorkflowExecutable).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Elsa.Workflows.Design.Core", referencedAssemblies);
        Assert.DoesNotContain("Elsa.Workflows.Design.Api", referencedAssemblies);
    }

    [Fact]
    public void RuntimeCoreContractTypes_DoNotExposeWorkflowDesignNamespaces()
    {
        var designTypes = typeof(WorkflowExecutable).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("Elsa.Workflows.Runtime.Core", StringComparison.Ordinal) == true)
            .SelectMany(PublicSurfaceTypes)
            .Where(type => type.Namespace?.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal) == true)
            .Select(type => type.FullName)
            .Distinct()
            .ToList();

        Assert.True(designTypes.Count == 0, string.Join(Environment.NewLine, designTypes));
    }

    private static IEnumerable<Type> PublicSurfaceTypes(Type type)
    {
        yield return type;

        foreach (var property in type.GetProperties())
            yield return property.PropertyType;

        foreach (var constructor in type.GetConstructors())
        foreach (var parameter in constructor.GetParameters())
            yield return parameter.ParameterType;

        foreach (var method in type.GetMethods())
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }
    }
}
