using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>Spec 266 (FR-008): forward steps run in order; handlers run afterwards, newest first.</summary>
public sealed class CompensationScopeChildTimeRelationTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public CompensationScopeChildTimeRelationTests()
    {
        var services = new ServiceCollection();
        new ActivitiesCompensationFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>());
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData("step-1", "step-2", ActivityChildTimeRelation.FirstBeforeSecond)]
    [InlineData("step-2", "undo-1", ActivityChildTimeRelation.FirstBeforeSecond)]
    [InlineData("undo-2", "undo-1", ActivityChildTimeRelation.FirstBeforeSecond)]
    [InlineData("undo-1", "step-1", ActivityChildTimeRelation.SecondBeforeFirst)]
    [InlineData("step-1", "missing", ActivityChildTimeRelation.Unspecified)]
    public void Steps_then_handlers_in_reverse_form_one_order(string first, string second, ActivityChildTimeRelation expected)
    {
        var structure = new CompensationScopeAuthoredStructure(
        [
            new CompensationScopeAuthoredStep("one", Leaf("step-1"), Leaf("undo-1")),
            new CompensationScopeAuthoredStep("two", Leaf("step-2"), Leaf("undo-2"))
        ]);
        var scope = new ActivityNode("scope", "scope@1", [], [],
            new ActivityNodeStructure(CompensationScope.StructureKind, CompensationScope.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        Assert.Equal(expected, _handler.RelateChildren(scope, first, second));
        Assert.True(_handler.CompletesAfterItsChildren(scope));
    }

    private static ActivityNode Leaf(string nodeId) => new(nodeId, $"activity-version-{nodeId}", [], []);
}
