using System.Text.Json;
using Elsa.Activities.Parallel.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.ControlFlow.Tests;

/// <summary>Spec 266 (FR-008): every control-flow structure declares how its children relate in time.</summary>
public sealed class ChildTimeRelationTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler[] _handlers;

    public ChildTimeRelationTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handlers = _provider.GetServices<IActivityStructureHandler>().ToArray();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Every_control_flow_structure_declares_its_children_and_completes_after_them_by_default()
    {
        var node = new ActivityNode("container", "container@1", [], [], null);
        foreach (var handler in _handlers)
        {
            var pair = handler.RelateChildren(node, "first", "second");
            var expected = handler.Kind switch
            {
                ParallelActivity.StructureKind => ActivityChildTimeRelation.Unordered,
                "elsa.if.structure" or "elsa.switch.structure" => ActivityChildTimeRelation.MutuallyExclusive,
                _ => ActivityChildTimeRelation.FirstBeforeSecond // the loops: one body, passes in turn
            };
            Assert.Equal(expected, pair);
            // No child of these structures runs twice at once within one run.
            Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, handler.RelateChildren(node, "body", "body"));
            Assert.True(handler.CompletesAfterItsChildren(node));
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    [InlineData(1, false)]
    public void A_parallel_threshold_below_its_runnable_branches_releases_the_join_early(int? threshold, bool completesAfterChildren)
    {
        var parallel = Assert.Single(_handlers, handler => handler.Kind == ParallelActivity.StructureKind);
        var structure = new ParallelAuthoredStructure(
            [
                new ParallelAuthoredBranch("a", new ActivityNode("a", "test@1", [], [])),
                new ParallelAuthoredBranch("b", new ActivityNode("b", "test@1", [], [])),
                new ParallelAuthoredBranch("empty")
            ],
            threshold);
        var node = new ActivityNode("parallel", "parallel@1", [], [],
            new ActivityNodeStructure(ParallelActivity.StructureKind, ParallelActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)));

        Assert.Equal(completesAfterChildren, parallel.CompletesAfterItsChildren(node));
    }
}
