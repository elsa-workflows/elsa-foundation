using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

/// <summary>
/// The spec 266 rules over declarations alone: a test structure service states every relation, so no built-in
/// structure is involved.
/// </summary>
public sealed class UnorderedVariableWriteAnalysisTests
{
    [Fact]
    public void A_child_that_overlaps_itself_makes_its_single_writer_unordered()
    {
        var root = Container("multi", Capture("body", "total"));
        var structures = new DeclaredStructures { SelfOverlapping = { "body" } };

        var finding = Assert.Single(Analyze(root, structures).UnorderedWrites);

        Assert.Equal(["body"], finding.WriterNodeIds);
        Assert.Equal(["body"], finding.SelfOverlappingNodeIds);
        Assert.Contains("Node 'body' can run more than once at the same time.", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordered_writes_inside_a_self_overlapping_child_are_unordered_across_its_runs()
    {
        var root = Container("multi", Container("inner", Capture("first", "total"), Capture("second", "total")));
        var structures = new DeclaredStructures { SelfOverlapping = { "inner" }, Ordered = { "inner" } };

        var finding = Assert.Single(Analyze(root, structures).UnorderedWrites);

        Assert.Equal(["first", "second"], finding.WriterNodeIds);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(null, true)]
    public void An_earlier_child_orders_its_descendants_only_when_it_completes_after_its_children(bool? completes, bool warns)
    {
        // sequence(container(write-early), write-late)
        var root = Container("sequence", Container("container", Capture("write-early", "total")), Capture("write-late", "total"));
        var structures = new DeclaredStructures { Ordered = { "sequence" }, Completes = { ["container"] = completes } };

        var findings = Analyze(root, structures).UnorderedWrites;

        Assert.Equal(warns, findings.Count == 1);
        if (completes is null)
            Assert.Contains("Structure 'test.container' (node 'container') does not declare whether it completes only after its children", Assert.Single(findings).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_containers_own_capture_follows_its_descendants_only_when_it_completes_after_them(bool completes, bool warns)
    {
        var root = Container("container", Capture("inner", "total")) with { Outputs = [VariableOutput("result", "total")] };
        var structures = new DeclaredStructures { Completes = { ["container"] = completes } };

        Assert.Equal(warns, Analyze(root, structures).UnorderedWrites.Count == 1);
    }

    [Fact]
    public void Different_variables_and_different_scopes_never_collide()
    {
        var root = Container("parallel",
            Capture("a", "total"),
            Capture("b", "other"),
            new ActivityNode("c", "test@1", [], [VariableOutput("result", "total", "container-scope")]));

        Assert.Empty(Analyze(root, new DeclaredStructures()).UnorderedWrites);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("$workflow")]
    [InlineData("activity-graph")]
    public void Root_scope_spellings_are_one_variable(string? scope)
    {
        var root = Container("parallel", Capture("a", "total"), new ActivityNode("b", "test@1", [], [VariableOutput("result", "total", scope)]));

        var finding = Assert.Single(Analyze(root, new DeclaredStructures()).UnorderedWrites);

        Assert.Equal(VariableReference.WorkflowScopeId, finding.ScopeId);
    }

    // Children that alternate around a cycle need both sides done: an early-releasing later child still overlaps the
    // earlier child's next run.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Alternating_children_need_both_sides_done(bool laterCompletesAfterChildren, bool warns)
    {
        var root = Container("loop", Capture("a", "total"), Container("b", Capture("b-inner", "total")));
        var structures = new DeclaredStructures { Alternating = { "loop" }, Completes = { ["b"] = laterCompletesAfterChildren } };

        Assert.Equal(warns, Analyze(root, structures).UnorderedWrites.Count == 1);
    }

    [Fact]
    public void Two_children_with_one_node_id_are_never_claimed_ordered()
    {
        var root = Container("sequence", Capture("same", "total"), Capture("same", "total"));
        var structures = new DeclaredStructures { Ordered = { "sequence" } };

        Assert.Single(Analyze(root, structures).UnorderedWrites);
    }

    private static UnorderedVariableWriteAnalysis Analyze(ActivityNode root, DeclaredStructures structures) =>
        WriteOrderAnalyzer().Analyze(root, structures, includeIntrinsics: true);

    private static Elsa.Workflows.Design.Validations.Core.Contracts.IUnorderedVariableWriteAnalyzer WriteOrderAnalyzer()
    {
        var services = new ServiceCollection();
        new Elsa.Workflows.Design.Validations.WorkflowDesignValidationsFeature().ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<Elsa.Workflows.Design.Validations.Core.Contracts.IUnorderedVariableWriteAnalyzer>();
    }


    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ActivityNode Container(string nodeId, params ActivityNode[] children) =>
        new(nodeId, "test@1", [], [], new ActivityNodeStructure("test.container", "1", JsonSerializer.SerializeToElement(children, Json)));

    private static ActivityNode Capture(string nodeId, string variableKey) =>
        new(nodeId, "test@1", [], [VariableOutput("result", variableKey)]);

    private static ArgumentState VariableOutput(string outputKey, string variableKey, string? scope = "workflow") =>
        new(outputKey,
            new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = variableKey, declaringScopeId = scope }), "Variable"),
            null, null, null, null);

    // Containers are unordered (Parallel-like) unless listed as ordered (by child position) or self-overlapping.
    private sealed class DeclaredStructures : IActivityStructureService
    {
        public HashSet<string> Ordered { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Alternating { get; } = new(StringComparer.Ordinal);
        public HashSet<string> SelfOverlapping { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, bool?> Completes { get; } = new(StringComparer.Ordinal);

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            activity.Structure is null ? [] : [new ActivityChildProjection("children", Children(activity))];

        public ActivityChildTimeRelation RelateChildren(ActivityNode activity, string first, string second)
        {
            if (first == second)
                return SelfOverlapping.Contains(first) ? ActivityChildTimeRelation.Unordered : ActivityChildTimeRelation.FirstBeforeSecond;
            if (Alternating.Contains(activity.NodeId))
                return ActivityChildTimeRelation.FirstBeforeSecond;
            if (!Ordered.Contains(activity.NodeId))
                return ActivityChildTimeRelation.Unordered;
            var ids = Children(activity).Select(child => child.NodeId).ToList();
            return ids.IndexOf(first) < ids.IndexOf(second) ? ActivityChildTimeRelation.FirstBeforeSecond : ActivityChildTimeRelation.SecondBeforeFirst;
        }

        public bool? CompletesAfterItsChildren(ActivityNode activity) => Completes.TryGetValue(activity.NodeId, out var value) ? value : true;

        private static ActivityNode[] Children(ActivityNode activity) =>
            activity.Structure!.Payload.Deserialize<ActivityNode[]>(Json) ?? [];

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> children) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
}
