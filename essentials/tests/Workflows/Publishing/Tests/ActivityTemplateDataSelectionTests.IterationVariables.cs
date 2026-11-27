using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

/// <summary>
/// #5697: a loop's iteration values (For's <c>index</c>, ForEach's <c>currentItem</c>) are variables the loop node
/// declares for its body (spec 1222 FR-004a). Publication pins a body script's <c>variables.index</c> to the loop node,
/// so the read admission facts declare those values on the loop node too, visible to its body only.
/// </summary>
public sealed partial class ActivityTemplateDataSelectionTests
{
    [Fact]
    public void A_body_read_of_a_loop_iteration_value_is_admitted()
    {
        var (compiled, authored, structures) = Loop(Node("reader", Expression(new VariableExpressionParameterBinding("loop", "index"))));

        var facts = ActivityTemplatePlacer.DescribeDataSelectionFacts(compiled, [],
            new Dictionary<string, IReadOnlyCollection<string>> { [VariableReference.WorkflowScopeId] = [] }, authored, structures);

        ActivityTemplateDataSelectionGuard.Validate(compiled, true, facts);
        Assert.Equal(new[] { "index" }, facts.VariableDeclarations["loop"]);
    }

    [Fact]
    public void An_iteration_value_is_not_visible_outside_its_loop_body()
    {
        var (compiled, authored, structures) = Loop(Node("reader", Expression(new VariableExpressionParameterBinding("loop", "index"))),
            sibling: Node("outside", Expression(new VariableExpressionParameterBinding("loop", "index"))));

        var facts = ActivityTemplatePlacer.DescribeDataSelectionFacts(compiled, [],
            new Dictionary<string, IReadOnlyCollection<string>> { [VariableReference.WorkflowScopeId] = [] }, authored, structures);

        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(compiled, true, facts));
        var finding = Assert.Single(rejected.Findings);
        Assert.Equal(("outside/inputs/value", "ActivityDataSelection/VariableScopeInvisible"), (finding.Path, finding.Type));
    }

    [Fact]
    public void A_body_read_of_a_value_the_loop_does_not_declare_is_still_refused()
    {
        var (compiled, authored, structures) = Loop(Node("reader", Expression(new VariableExpressionParameterBinding("loop", "currentItem"))));

        var facts = ActivityTemplatePlacer.DescribeDataSelectionFacts(compiled, [],
            new Dictionary<string, IReadOnlyCollection<string>> { [VariableReference.WorkflowScopeId] = [] }, authored, structures);

        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(compiled, true, facts));
        Assert.Equal("ActivityDataSelection/VariableUnknown", Assert.Single(rejected.Findings).Type);
    }

    /// <summary>A root container holding a loop (whose body is <paramref name="body"/>) and an optional sibling after it.</summary>
    private static (ExecutableNode Compiled, ActivityNode Authored, IActivityStructureService Structures) Loop(ExecutableNode body, ExecutableNode? sibling = null)
    {
        var loop = Parent("loop", body);
        var compiled = sibling is null ? Parent("root", loop) : Parent("root", loop, sibling);
        var authoredBody = new ActivityNode(body.ExecutableNodeId, "v", [], []);
        var authoredLoop = new ActivityNode("loop", "v", [], []);
        var authoredRoot = new ActivityNode("root", "v", [], []);
        var children = new Dictionary<string, ActivityNode[]>(StringComparer.Ordinal)
        {
            ["root"] = sibling is null ? [authoredLoop] : [authoredLoop, new ActivityNode(sibling.ExecutableNodeId, "v", [], [])],
            ["loop"] = [authoredBody]
        };
        return (compiled, authoredRoot, new IterationStructures(children));
    }

    private static ExecutableNode Parent(string id, params ExecutableNode[] children) =>
        new(id, id, "native", "1", new("native", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
            childSlots: [new("body", children)]);

    /// <summary>Projects the authored tree; the node <c>loop</c> declares the iteration value <c>index</c> and no scoped variables.</summary>
    private sealed class IterationStructures(IReadOnlyDictionary<string, ActivityNode[]> children) : IActivityStructureService
    {
        public bool SupportsScopedVariables(ActivityNode activity) => false;
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            children.TryGetValue(activity.NodeId, out var slot) ? [new("body", slot)] : [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> projections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public IReadOnlyCollection<VariableDefinition> ProjectIterationVariables(ActivityNode activity) =>
            activity.NodeId == "loop" ? [new("index", "index", new("Any"), null, null)] : [];
    }
}
