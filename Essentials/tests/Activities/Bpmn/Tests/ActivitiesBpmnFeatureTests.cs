using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class ActivitiesBpmnFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersEngineBehaviorsAndStructureHandler()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<BpmnExecutionEngine>());
        Assert.NotNull(provider.GetRequiredService<BpmnTokenCoordinator>());
        Assert.NotNull(provider.GetRequiredService<BpmnStatePersister>());

        var structureHandler = Assert.Single(provider.GetServices<IActivityStructureHandler>());
        Assert.Equal(Activities.BpmnProcess.StructureKind, structureHandler.Kind);
        Assert.True(structureHandler.SupportsScopedVariables);
        Assert.Equal(typeof(Models.BpmnAuthoredStructure), structureHandler.AuthoredPayloadType);

        var registry = provider.GetRequiredService<IBpmnBehaviorRegistry>();
        foreach (var family in new[]
                 {
                     BpmnElementFamilies.StartEventNone,
                     BpmnElementFamilies.EndEventNone,
                     BpmnElementFamilies.EndEventTerminate,
                     BpmnElementFamilies.Task,
                     BpmnElementFamilies.SubProcess,
                     BpmnElementFamilies.ExclusiveGateway,
                     BpmnElementFamilies.ParallelGateway,
                     BpmnElementFamilies.InclusiveGateway
                 })
        {
            Assert.True(registry.TryGet(family, out _), $"Behavior for family '{family}' is not registered.");
        }
    }
}
