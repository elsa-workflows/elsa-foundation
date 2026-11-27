using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

/// <summary>
/// #5849: a graph-local Set Output inside a reusable body's loop reads the loop's iteration value. Before placement the
/// intrinsic sits beside the boundary, so its loop ancestor's iteration values come from the authored ancestry checked
/// against the loop occurrence's compiled structure.
/// </summary>
public sealed partial class ActivityTemplateDataSelectionTests
{
    [Fact]
    public void A_graph_local_intrinsic_in_a_loop_reads_the_loop_iteration_value_before_placement()
    {
        var (root, source, owner, overlays) = OriginalLoopScope(["index"]);

        var facts = ActivityTemplatePlacer.DescribeTemplateDataSelectionFacts(root, [], source, owner, overlays);

        ActivityTemplateDataSelectionGuard.Validate(root, true, facts);
        Assert.Equal(new[] { "index" }, facts.VariableDeclarations["loop"]);
    }

    [Fact]
    public void A_loop_occurrence_that_does_not_compile_its_iteration_values_cannot_authorize_the_read()
    {
        var (root, source, owner, overlays) = OriginalLoopScope([]);

        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplatePlacer.DescribeTemplateDataSelectionFacts(root, [], source, owner, overlays));
        Assert.Equal("ActivityDataSelection/VariableDeclarationMismatch", Assert.Single(rejected.Findings).Type);
    }

    [Theory]
    [InlineData("missing-overlay")]
    [InlineData("malformed-values")]
    public void A_loop_ancestor_without_one_well_formed_compiled_occurrence_cannot_authorize_the_read(string change)
    {
        var (root, source, owner, overlays) = OriginalLoopScope(change == "malformed-values" ? [""] : ["index"]);

        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplatePlacer.DescribeTemplateDataSelectionFacts(root, [], source, owner, change == "missing-overlay" ? [] : overlays));
        Assert.Equal("ActivityDataSelection/VariableDeclarationUnavailable", Assert.Single(rejected.Findings).Type);
    }

    private static (ExecutableNode Root, ActivityDataSelectionDocument Source, IActivityStructureService Owner, ActivityTemplateOccurrenceCompilation[] Overlays)
        OriginalLoopScope(string[] compiledIterationValues)
    {
        var sourceRead = new ArgumentState("value", new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "index", declaringScopeId = "loop" }), "Variable"), null, null, null, null);
        var authoredSetter = new ActivityNode("setter", "elsa.intrinsic.set-output@1", [sourceRead], [])
        { Intrinsic = new(AuthoredWorkflowIntrinsicKind.SetOutput, new("String")) };
        var authoredLoop = new ActivityNode("loop", "for", [], [], new("loop", "1", JsonSerializer.SerializeToElement(new { })));
        var structures = new IterationStructures(new Dictionary<string, ActivityNode[]>(StringComparer.Ordinal) { ["loop"] = [authoredSetter] });
        var source = new ActivityDataSelectionDocument("ActivityDefinition", "draft", "revision", authoredLoop,
            [new InputDefinition("body-key", "BodyKey", new("String"), null, "Body key", null, true)], [], []);
        var read = Expression(new VariableExpressionParameterBinding("loop", "index"));
        var setter = new ExecutableNode("set-output:setter", "setter", "elsa.intrinsic.setoutput", "1",
            "intrinsic", JsonSerializer.SerializeToElement(new { parentOccurrenceId = "loop", childSlotName = "body", childIndex = 0 }),
            new Dictionary<string, RuntimeInputBinding> { [read.InputKey] = read, ["name"] = new("name", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new("String"), JsonSerializer.SerializeToElement("Answer"), ValueProtectionPolicy.InstanceInline)) }, new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.SetOutput);
        var boundary = Closed("boundary", "body-key", [], null, setter);
        var root = new ExecutableNode(boundary.ExecutableNodeId, boundary.AuthoredActivityId, boundary.ActivityType, boundary.ActivityTypeVersion,
            boundary.Descriptor, boundary.InputBindings, boundary.OutputCaptures, new Dictionary<string, string>(),
            childSlots: [new("graph-local-nodes", [setter])], activityContract: boundary.ActivityContract);
        var overlay = new ActivityTemplateOccurrenceCompilation("loop", JsonSerializer.SerializeToElement(Array.Empty<object>()),
            new("loop", "1", CompiledLoop("loop", compiledIterationValues).Structure!.Payload));
        return (root, source, structures, [overlay]);
    }
}
