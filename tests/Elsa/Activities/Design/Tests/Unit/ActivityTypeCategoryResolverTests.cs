using System.Reflection;
using System.Reflection.Emit;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Tests.ClrFixture;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ActivityTypeCategoryResolver"/>. The category is the last dot-separated
/// segment of the declaring assembly's simple name, humanized for display (issue #928), so activities
/// shipped together share one bucket.
/// </summary>
public sealed class ActivityTypeCategoryResolverTests
{
    private readonly ActivityTypeCategoryResolver _resolver = new();

    [Fact]
    public void Category_IsLastSegment_OfAssemblyName()
    {
        // The fixture assembly is named "Elsa.Activities.Design.Tests.ClrFixture"; the last segment
        // "ClrFixture" humanizes to "Clr Fixture".
        var assembly = typeof(UnannotatedFixtureActivity).Assembly;

        var result = _resolver.Resolve(typeof(UnannotatedFixtureActivity), assembly);

        Assert.Equal("Clr Fixture", result);
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

    [Theory]
    [InlineData("Elsa.Activities.ControlFlow.Runtime", "Control Flow")]
    [InlineData("Elsa.Activities.ControlFlow.Design", "Control Flow")]
    [InlineData("Elsa.Activities.Bpmn.Runtime", "BPMN")]
    [InlineData("Elsa.Activities.Graph.Runtime", "Graph")]
    [InlineData("Elsa.Activities.DispatchWorkflow.Runtime", "Dispatch Workflow")]
    public void PlaneSuffix_IsSkipped_SoBothHalvesShareTheDomainBucket(string assemblyName, string expected)
    {
        // spec 151 T128 split the composite activity packages into .Design/.Runtime halves. The suffix names
        // the composition plane, not the catalog bucket, so it must not become the category — otherwise every
        // composite activity in the designer palette collapses into one "Runtime" group.
        var assembly = EmitAssemblyNamed(assemblyName, out var type);

        var result = _resolver.Resolve(type, assembly);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void RuntimeActivityPackage_KeepsRuntime_BecauseTheSuffixIsItsDomain()
    {
        // The guard on the skip: Elsa.Activities.Runtime IS the runtime activity package, so stripping here
        // would fall back to the meaningless "Activities".
        var assembly = EmitAssemblyNamed("Elsa.Activities.Runtime", out var type);

        var result = _resolver.Resolve(type, assembly);

        Assert.Equal("Runtime", result);
    }

    [Fact]
    public void SingleSegmentAssemblyName_IsHumanized()
    {
        var assembly = EmitAssemblyNamed("MyActivities", out var type);

        var result = _resolver.Resolve(type, assembly);

        Assert.Equal("My Activities", result);
    }

    private static Assembly EmitAssemblyNamed(string simpleName, out Type type)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(simpleName), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("Main");
        type = module.DefineType("Emitted.MyActivity", TypeAttributes.Public | TypeAttributes.Class).CreateType();
        return builder;
    }
}
