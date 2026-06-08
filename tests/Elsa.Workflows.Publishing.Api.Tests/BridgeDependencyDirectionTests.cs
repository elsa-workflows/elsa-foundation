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
    [InlineData("Elsa.Activities.Primitives")]    // a Runtime kind feature
    public void BridgeDoesNotReference(string forbiddenAssembly)
    {
        var referenced = typeof(WorkflowsPublishingApiFeature).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name);

        Assert.DoesNotContain(forbiddenAssembly, referenced);
    }
}
