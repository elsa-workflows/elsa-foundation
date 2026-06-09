using System.Reflection;
using System.Reflection.Emit;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Tests.ClrFixture;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ActivityTypeCategoryResolver"/>. The category is the last dot-separated
/// segment of the declaring assembly's simple name, so activities shipped together share one bucket.
/// </summary>
public sealed class ActivityTypeCategoryResolverTests
{
    private readonly ActivityTypeCategoryResolver _resolver = new();

    [Fact]
    public void Category_IsLastSegment_OfAssemblyName()
    {
        // The fixture assembly is named "Elsa.Activities.Design.Tests.ClrFixture".
        var assembly = typeof(UnannotatedFixtureActivity).Assembly;

        var result = _resolver.Resolve(typeof(UnannotatedFixtureActivity), assembly);

        Assert.Equal("ClrFixture", result);
    }

    [Fact]
    public void PrimitivesAssembly_IsCatalogued_AsPrimitives()
    {
        // The motivating case: an assembly named Elsa.Runtime.Activities.Primitives groups all of
        // its activities under the "Primitives" category.
        var assembly = EmitAssemblyNamed("Elsa.Runtime.Activities.Primitives", out var type);

        var result = _resolver.Resolve(type, assembly);

        Assert.Equal("Primitives", result);
    }

    [Fact]
    public void SingleSegmentAssemblyName_IsUsedVerbatim()
    {
        var assembly = EmitAssemblyNamed("MyActivities", out var type);

        var result = _resolver.Resolve(type, assembly);

        Assert.Equal("MyActivities", result);
    }

    private static Assembly EmitAssemblyNamed(string simpleName, out Type type)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(simpleName), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("Main");
        type = module.DefineType("Emitted.MyActivity", TypeAttributes.Public | TypeAttributes.Class).CreateType();
        return builder;
    }
}
