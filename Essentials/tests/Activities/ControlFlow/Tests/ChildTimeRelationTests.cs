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

    [Theory]
    [InlineData(null, ActivityChildTimeRelation.MutuallyExclusive)]
    [InlineData("MatchAny", ActivityChildTimeRelation.Unordered)]
    public void Switch_mode_controls_case_overlap_but_Default_remains_exclusive(string? mode, ActivityChildTimeRelation expected)
    {
        var handler = _handlers.Single(item => item.Kind == "elsa.switch.structure" && item.SchemaVersion == "2.0.0");
        var payload = new Elsa.Activities.Switch.Models.SwitchAuthoredStructure(
            [new("A", new ActivityNode("a", "test", [], [])) { CaseId = "a" },
             new("B", new ActivityNode("b", "test", [], [])) { CaseId = "b" }],
            new ActivityNode("default", "test", [], []), mode);
        var node = new ActivityNode("switch", "test", [], [], new("elsa.switch.structure", "2.0.0", JsonSerializer.SerializeToElement(payload, Web)));
        Assert.Equal(expected, handler.RelateChildren(node, "a", "b"));
        Assert.Equal(ActivityChildTimeRelation.MutuallyExclusive, handler.RelateChildren(node, "a", "default"));
        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, handler.RelateChildren(node, "a", "a"));
        Assert.True(handler.CompletesAfterItsChildren(node));
    }

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
    [InlineData(null, false, false)]
    [InlineData("MatchAny", false, true)]
    [InlineData("MatchAny", true, false)]
    public void Real_Switch_facts_warn_only_for_overlapping_case_writers(string? mode, bool secondIsDefault, bool warns)
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        new Elsa.Workflows.Design.Validations.WorkflowDesignValidationsFeature().ConfigureServices(services);
        services.AddScoped<Elsa.Workflows.Design.Core.Contracts.IActivityStructureService, Elsa.Workflows.Design.Core.Services.DefaultActivityStructureService>();
        using var provider = services.BuildServiceProvider();
        ActivityNode Writer(string id) => new(id, "test", [], [new("result",
            new(JsonSerializer.SerializeToElement(new { referenceKey = "total", declaringScopeId = "workflow" }), "Variable"), null, null, null, null)]);
        var cases = new List<Elsa.Activities.Switch.Models.SwitchAuthoredCase> { new("A", Writer("a")) { CaseId = "a" } };
        if (!secondIsDefault) cases.Add(new("B", Writer("b")) { CaseId = "b" });
        var root = new ActivityNode("switch", "test", [], [], new("elsa.switch.structure", "2.0.0",
            JsonSerializer.SerializeToElement(new Elsa.Activities.Switch.Models.SwitchAuthoredStructure(cases, secondIsDefault ? Writer("b") : null, mode), Web)));
        var result = provider.GetRequiredService<Elsa.Workflows.Design.Validations.Core.Contracts.IUnorderedVariableWriteAnalyzer>()
            .Analyze(root, provider.GetRequiredService<Elsa.Workflows.Design.Core.Contracts.IActivityStructureService>(), includeIntrinsics: true);
        Assert.Equal(warns, result.UnorderedWrites.Count == 1);
        if (warns) Assert.Equal(new[] { "a", "b" }, Assert.Single(result.UnorderedWrites).WriterNodeIds);
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
