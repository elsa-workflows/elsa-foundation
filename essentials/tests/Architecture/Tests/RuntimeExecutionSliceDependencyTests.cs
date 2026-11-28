using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.KernelHosting;
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
    public void PublishingEngine_DoesNotReferenceClassicRuntimeOrItsApi()
    {
        var engine = typeof(Elsa.Workflows.Publishing.WorkflowsPublishingFeature).Assembly;
        AssertNoReferences(engine, "Elsa.Workflows.Runtime.Api");
        Assert.DoesNotContain(engine.GetReferencedAssemblies(), reference => reference.Name == "Elsa.Workflows.Runtime");
        Assert.DoesNotContain(typeof(Elsa.Workflows.Runtime.Extensions.WorkflowPublicationDefaults).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "Elsa.Workflows.Runtime");
    }

    [Fact]
    public void KernelApi_DoesNotReferenceBroadRuntimeApi()
    {
        Assert.DoesNotContain(typeof(WorkflowsRuntimeKernelRetainedApiFeature).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "Elsa.Workflows.Runtime.Api");
    }

    [Fact]
    public void KernelHosting_DoesNotReferenceBroadRuntimeApi()
    {
        Assert.DoesNotContain(typeof(WorkflowsRuntimeKernelHostingFeature).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "Elsa.Workflows.Runtime.Api");
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
