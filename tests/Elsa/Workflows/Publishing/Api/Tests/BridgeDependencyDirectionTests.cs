using Elsa.Workflows.Publishing.Api;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// The bridge must ride on the two seams' <c>.Core</c> contracts only. If it ever referenced the
/// Runtime implementation or a Design <c>.Api</c> feature, it would couple the sub-domains it is meant
/// to keep apart — and (via the Runtime impl) risk dragging a Design dependency into Runtime, breaking
/// §E2.2. This locks the dependency surface.
/// </summary>
public sealed class BridgeDependencyDirectionTests
{
    [Theory]
    [InlineData("Elsa.Activities.Runtime")]      // the Runtime IMPLEMENTATION (contracts live in .Runtime.Core)
    [InlineData("Elsa.Activities.Design.Api")]    // a peer Design feature
    [InlineData("Elsa.Workflows.Design.Api")]     // Studio-facing DTOs must not become the bridge contract
    [InlineData("Elsa.Activities.Primitives")]    // a Runtime kind feature
    public void BridgeDoesNotReference(string forbiddenAssembly)
    {
        // spec 145: the bridge is now split into the endpoint-free ENGINE assembly
        // (Elsa.Workflows.Publishing) and its transport sibling (…Publishing.Api). Both must ride on
        // the two seams' .Core contracts only, so the forbidden-reference list binds each assembly.
        AssertDoesNotReference(typeof(WorkflowsPublishingApiFeature).Assembly, forbiddenAssembly);
        AssertDoesNotReference(typeof(Elsa.Workflows.Publishing.WorkflowsPublishingFeature).Assembly, forbiddenAssembly);
    }

    private static void AssertDoesNotReference(System.Reflection.Assembly assembly, string forbiddenAssembly)
    {
        var referenced = assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name);

        Assert.DoesNotContain(forbiddenAssembly, referenced);
    }
}
