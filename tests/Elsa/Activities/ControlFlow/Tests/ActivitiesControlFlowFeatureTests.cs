using Elsa.Activities.ControlFlow;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using IfActivity = Elsa.Activities.If.Activities.If;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.ControlFlow.Tests;

public sealed class ActivitiesControlFlowFeatureTests
{
    private readonly IReadOnlyList<IActivityStructureHandler> _handlers;

    public ActivitiesControlFlowFeatureTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        _handlers = services.BuildServiceProvider().GetServices<IActivityStructureHandler>().ToList();
    }

    [Fact]
    public void ConfigureServices_RegistersAllSevenControlFlowStructureHandlers()
    {
        Assert.Equal(7, _handlers.Count);
    }

    [Theory]
    [InlineData(IfActivity.StructureKind, IfActivity.StructureSchemaVersion)]
    [InlineData(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion)]
    [InlineData(ForEachActivity.StructureKind, ForEachActivity.StructureSchemaVersion)]
    [InlineData(ForActivity.StructureKind, ForActivity.StructureSchemaVersion)]
    [InlineData(WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion)]
    [InlineData(DoActivity.StructureKind, DoActivity.StructureSchemaVersion)]
    [InlineData(ParallelActivity.StructureKind, ParallelActivity.StructureSchemaVersion)]
    public void ConfigureServices_RegistersHandlerForEachControlFlowKind(string kind, string schemaVersion)
    {
        var handler = Assert.Single(_handlers, h => h.Kind == kind);
        Assert.Equal(schemaVersion, handler.SchemaVersion);
    }
}
