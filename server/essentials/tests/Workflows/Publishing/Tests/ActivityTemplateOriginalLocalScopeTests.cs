using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

public sealed partial class ActivityTemplateDataSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Original_graph_local_intrinsics_retain_their_authored_native_scope_before_placement(bool portable)
    {
        var (root, source, owner, overlays) = OriginalLocalScope(portable);
        var facts = ActivityTemplatePlacer.DescribeTemplateDataSelectionFacts(root, [], source, owner, overlays);
        ActivityTemplateDataSelectionGuard.Validate(root, true, facts);
    }

    [Theory]
    [InlineData("missing-overlay")]
    [InlineData("changed-declaration")]
    [InlineData("foreign-scope")]
    [InlineData("missing-key")]
    public void Original_local_scope_facts_cannot_authorize_missing_or_changed_owner_material(string change)
    {
        var (root, source, owner, overlays) = OriginalLocalScope(false, change);
        Assert.Throws<ActivityDataSelectionRejectedException>(() =>
        {
            var facts = ActivityTemplatePlacer.DescribeTemplateDataSelectionFacts(root, [], source, owner, overlays);
            ActivityTemplateDataSelectionGuard.Validate(root, true, facts);
        });
    }

    private static (ExecutableNode Root, ActivityDataSelectionDocument Source, StructureOwner Owner, ActivityTemplateOccurrenceCompilation[] Overlays)
        OriginalLocalScope(bool portable, string? change = null)
    {
        var sourceRead = new ArgumentState("value", new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "actual", declaringScopeId = "owner" }), "Variable"), null, null, null, null);
        var authoredSetter = new ActivityNode("setter", "elsa.intrinsic.set-output@1", [sourceRead], [])
        { Intrinsic = new(AuthoredWorkflowIntrinsicKind.SetOutput, new("String")) };
        var authored = new ActivityNode("owner", "sequence", [], [], new("owned", "1", JsonSerializer.SerializeToElement(new { })));
        var owner = new StructureOwner(authored, authoredSetter);
        var source = new ActivityDataSelectionDocument("ActivityDefinition", "draft", "revision", authored,
            [new InputDefinition("body-key", "BodyKey", new("String"), null, "Body key", null, true)], [], []);
        var key = change == "missing-key" ? "missing" : "actual";
        var scope = change == "foreign-scope" ? "foreign" : "owner";
        var read = portable ? Expression(new VariableExpressionParameterBinding(scope, key)) : VariableBinding(key, scope);
        var setter = new ExecutableNode("set-output:setter", "setter", "elsa.intrinsic.setoutput", "1",
            "intrinsic", JsonSerializer.SerializeToElement(new { parentOccurrenceId = "owner", childSlotName = "body", childIndex = 0 }),
            new Dictionary<string, RuntimeInputBinding> { [read.InputKey] = read, ["name"] = new("name", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new("String"), JsonSerializer.SerializeToElement("Answer"), ValueProtectionPolicy.InstanceInline)) }, new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.SetOutput);
        var boundary = Closed("boundary", "body-key", [], null, setter);
        var root = new ExecutableNode(boundary.ExecutableNodeId, boundary.AuthoredActivityId, boundary.ActivityType, boundary.ActivityTypeVersion,
            boundary.Descriptor, boundary.InputBindings, boundary.OutputCaptures, new Dictionary<string, string>(),
            childSlots: [new("graph-local-nodes", [setter])], activityContract: boundary.ActivityContract);
        var compiled = Container("owner", [change == "changed-declaration" ? "other" : "actual"]);
        var overlay = new ActivityTemplateOccurrenceCompilation("owner", JsonSerializer.SerializeToElement(Array.Empty<object>()),
            new("owned", "1", compiled.Structure!.Payload));
        return (root, source, owner, change == "missing-overlay" ? [] : [overlay]);
    }
}
