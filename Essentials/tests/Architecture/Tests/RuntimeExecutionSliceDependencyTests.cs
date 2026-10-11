using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class RuntimeExecutionSliceDependencyTests
{
    [Fact]
    public void RuntimeCore_DoesNotReferenceWorkflowDesignOrPublishing()
    {
        AssertNoReferences(
            typeof(WorkflowExecutable).Assembly,
            "Elsa.Workflows.Design.",
            "Elsa.Workflows.Publishing.");
    }

    [Fact]
    public void RuntimeApi_DoesNotReferenceWorkflowDesignOrPublishing()
    {
        AssertNoReferences(
            typeof(WorkflowsRuntimeApiFeature).Assembly,
            "Elsa.Workflows.Design.",
            "Elsa.Workflows.Publishing.");
    }

    [Fact]
    public void PublishingApi_DoesNotReferenceRuntimeApi()
    {
        AssertNoReferences(
            typeof(WorkflowsPublishingApiFeature).Assembly,
            "Elsa.Workflows.Runtime.Api");
    }

    [Fact]
    public void PublishingEngine_DoesNotReferenceRuntimeApi()
    {
        // spec 145: the endpoint-free publish engine composes the Runtime triggers feature via DependsOn
        // (a shell-composition edge), not an assembly reference — so it must not pull in Runtime.Api.
        AssertNoReferences(
            typeof(Elsa.Workflows.Publishing.WorkflowsPublishingFeature).Assembly,
            "Elsa.Workflows.Runtime.Api");
    }

    private static void AssertNoReferences(System.Reflection.Assembly assembly, params string[] forbiddenPrefixes)
    {
        var forbidden = assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .OfType<string>()
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(forbidden.Length == 0, string.Join(Environment.NewLine, forbidden));
    }
}
