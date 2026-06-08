using System.Text.Json;
using Elsa.Activities.Composition.Runtime.Activities;
using Elsa.Activities.Composition.Runtime.Constructors;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Expressions.Models;
using Elsa.Workflows.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Composition.Tests;

public class WorkflowActivityConstructorTests
{
    private static (ActivityFactory factory, string descriptorType) BuildFactory()
    {
        var registry = new ActivityConstructorRegistry();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        registry.Add(new WorkflowActivityConstructor(serviceProvider));
        return (new ActivityFactory(registry), typeof(WorkflowIdentity).FullName!);
    }

    [Fact] // US2 — Workflow round-trip: identity applied as typed state, author args in the bag
    public async Task Create_WorkflowDescriptor_ConfiguresBackingActivity()
    {
        var (factory, descriptorType) = BuildFactory();
        var payload = JsonSerializer.SerializeToElement(new WorkflowIdentity("def-1", "ver-1", "1.0.0"));
        var inputs = new Dictionary<string, InputArgument>
        {
            ["amount"] = new InputArgument<int>(new MemoryBlockReference())
        };

        var activity = await factory.Create(descriptorType, payload, inputs, null);

        var workflowActivity = Assert.IsType<WorkflowDefinitionActivity>(activity);
        Assert.Equal("def-1", workflowActivity.WorkflowIdentity!.DefinitionId);
        Assert.Equal("ver-1", workflowActivity.WorkflowIdentity.VersionId);
        Assert.Equal("1.0.0", workflowActivity.WorkflowIdentity.Version);
        Assert.True(workflowActivity.SyntheticProperties.ContainsKey("amount"));
    }

    [Fact] // US2 AS3 — one backing type, many workflows: distinct instances differ only by identity
    public async Task Create_TwoIdentities_ProduceDistinctInstances()
    {
        var (factory, descriptorType) = BuildFactory();

        var a = await factory.Create(descriptorType, JsonSerializer.SerializeToElement(new WorkflowIdentity("d", "v1", "1.0.0")), null, null);
        var b = await factory.Create(descriptorType, JsonSerializer.SerializeToElement(new WorkflowIdentity("d", "v2", "2.0.0")), null, null);

        Assert.NotSame(a, b);
        Assert.Equal("v1", Assert.IsType<WorkflowDefinitionActivity>(a).WorkflowIdentity!.VersionId);
        Assert.Equal("v2", Assert.IsType<WorkflowDefinitionActivity>(b).WorkflowIdentity!.VersionId);
    }

    [Fact] // descriptor type derived from typeof(WorkflowIdentity)
    public void DescriptorType_IsWorkflowIdentityFullName()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var constructor = new WorkflowActivityConstructor(serviceProvider);

        Assert.Equal("Elsa.Workflows.Primitives.WorkflowIdentity", constructor.DescriptorType);
    }
}
