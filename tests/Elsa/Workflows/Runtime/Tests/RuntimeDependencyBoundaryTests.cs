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
            .OfType<string>()
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

    [Fact]
    public void PublicSurfaceTypes_UnwrapsGenericTypeArguments()
    {
        var surfaceTypes = PublicSurfaceTypes(typeof(GenericSurfaceProbe)).ToList();

        Assert.Contains(typeof(Elsa.Workflows.Design.TestDoubles.DesignLeakProbe), surfaceTypes);
    }

    private static IEnumerable<Type> PublicSurfaceTypes(Type type)
    {
        foreach (var surfaceType in Expand(type))
            yield return surfaceType;

        foreach (var property in type.GetProperties())
        foreach (var surfaceType in Expand(property.PropertyType))
            yield return surfaceType;

        foreach (var constructor in type.GetConstructors())
        foreach (var parameter in constructor.GetParameters())
        foreach (var surfaceType in Expand(parameter.ParameterType))
            yield return surfaceType;

        foreach (var method in type.GetMethods())
        {
            foreach (var surfaceType in Expand(method.ReturnType))
                yield return surfaceType;

            foreach (var parameter in method.GetParameters())
            foreach (var surfaceType in Expand(parameter.ParameterType))
                yield return surfaceType;
        }
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;

        var elementType = type.GetElementType();
        if (elementType is not null)
        {
            foreach (var surfaceType in Expand(elementType))
                yield return surfaceType;
        }

        foreach (var argument in type.GetGenericArguments())
        foreach (var surfaceType in Expand(argument))
            yield return surfaceType;
    }

    private sealed class GenericSurfaceProbe
    {
        public IReadOnlyCollection<Elsa.Workflows.Design.TestDoubles.DesignLeakProbe> Items { get; } = [];
    }
}
