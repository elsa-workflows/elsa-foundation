using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Workflows.Publishing.Tests;

public sealed partial class ActivityTemplateDataSelectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Original_staged_whole_result_reads_use_exact_source_and_dependency_contracts(bool portable, bool collision)
    {
        var (root, facts) = OriginalWholeResultCase("valid", portable, collision);
        ActivityTemplateDataSelectionGuard.Validate(root, true, facts);
        Assert.Single(facts.OriginalResultReads!);
        Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(root, [], true));
        Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(root, false, facts));
    }

    [Theory]
    [InlineData("unknown-producer")]
    [InlineData("ambiguous-dependency")]
    [InlineData("ambiguous-source")]
    [InlineData("wrong-version")]
    [InlineData("wrong-template")]
    [InlineData("wrong-projection")]
    [InlineData("wrong-revision")]
    [InlineData("source-error")]
    [InlineData("unprojected-source")]
    [InlineData("rewritten-producer")]
    [InlineData("rewritten-projection")]
    [InlineData("rewritten-scope")]
    [InlineData("rewritten-optional")]
    [InlineData("rewritten-role")]
    [InlineData("rewritten-parameter")]
    [InlineData("literal-lookalike")]
    public void Original_result_facts_do_not_authorize_changed_missing_or_opaque_material(string change)
    {
        var (root, facts) = OriginalWholeResultCase(change, change is "rewritten-parameter" or "literal-lookalike", true);
        Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(root, true, facts));
    }

    private static (ExecutableNode Root, ActivityTemplateDataSelectionFacts Facts) OriginalWholeResultCase(string change, bool portable, bool collision)
    {
        var producerId = collision ? "set-output:consumer" : "producer";
        var projection = change == "wrong-projection" ? "missing" : "Result";
        var reference = new { producerNodeId = producerId, projectionKey = projection, producerScopeId = "original-scope", isOptional = false };
        var expression = new ExpressionDefinition("JavaScript", "args.source", new("String"),
            new Dictionary<string, ExpressionParameterBinding> { ["source"] = new ActivityResultExpressionParameterBinding(producerId, projection) },
            JsonSerializer.SerializeToElement(new { }), "binding-pure-v1");
        var sourceValue = portable ? new ArgumentValue(expression, change == "literal-lookalike" ? "Literal" : "JavaScript")
            : new ArgumentValue(JsonSerializer.SerializeToElement(reference), "ActivityResult");
        var producer = new ActivityNode(producerId, "producer-version", [], []);
        var consumer = new ActivityNode("consumer", "elsa.intrinsic.set-output@1", [new("value", sourceValue, null, null, null, null)], [])
        { Intrinsic = new(AuthoredWorkflowIntrinsicKind.SetOutput, new("String")) };
        var authoredRoot = new ActivityNode("owner", "sequence-version", [], [], new("owned", "1", JsonSerializer.SerializeToElement(new { })));
        var owner = new StructureOwner(authoredRoot, change == "ambiguous-source" ? [producer, consumer, producer] : [producer, consumer]);
        var source = new ActivityDataSelectionDocument("ActivityDefinition", "draft", "revision", authoredRoot, [], [], []);
        var analysis = new ActivityDataSelectionAnalysis(change == "wrong-revision" ? "changed" : "revision", null, [],
            change == "source-error" ? [new("consumer/inputs/value", "ValueFlow/ProducerNotFound", "Missing producer.")] : []);
        var resultContract = new ActivityResultContract(new("String"), false, ActivityValuePolicy.Default,
            [new ActivityResultProjectionContract("Result", "Result", new("String"), false, ActivityValuePolicy.Default),
             new ActivityResultProjectionContract("Other", "Other", new("String"), false, ActivityValuePolicy.Default)]);
        var contract = new Elsa.Activities.Runtime.Core.Models.ActivityContract("producer", "1", "native", JsonSerializer.SerializeToElement(new { }), [],
            resultContract, ["done"], new("native", "producer"));
        var producerRoot = new ExecutableNode("template-root", "template-authored", "native", "1", new("native", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(), activityContract: contract);
        var template = new ExecutableActivityTemplate("template", "sha256:exact", producerRoot, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            [], [], [], "provider", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var dependency = new ActivityResolvedDependency("producer-definition", change == "wrong-version" ? "changed" : "producer-version", "1",
            template.TemplateId, change == "wrong-template" ? "sha256:changed" : template.TemplateHash, new DesignContract("1", [], [], []),
            ActivityDefinitionVersionLifecycle.Active, null, producerId, []);
        var dependencies = change == "unknown-producer" ? Array.Empty<ActivityResolvedDependency>() : change == "ambiguous-dependency" ? [dependency, dependency] : new[] { dependency };
        var changedProducer = change is "rewritten-producer" or "rewritten-parameter" ? "changed-producer" : producerId;
        RuntimeInputBinding binding = portable ? Expression(new ActivityResultExpressionParameterBinding(changedProducer, projection)) :
            new("value", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
                activityResult: new(changedProducer, change == "rewritten-projection" ? "Other" : projection,
                    change == "rewritten-scope" ? "changed-scope" : "original-scope", change == "rewritten-optional"));
        if (change == "rewritten-role") binding = new("value", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new("String"), JsonSerializer.SerializeToElement("changed"), ValueProtectionPolicy.InstanceInline));
        var setter = new ExecutableNode("set-output:consumer", "consumer", "elsa.intrinsic.setoutput", "1", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = binding, ["name"] = new("name", new("String"), ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new("String"), JsonSerializer.SerializeToElement("Answer"), ValueProtectionPolicy.InstanceInline)) },
            new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.SetOutput);
        var boundary = Closed("graph-boundary", "body-key", [], null, setter);
        var root = new ExecutableNode(boundary.ExecutableNodeId, boundary.AuthoredActivityId, boundary.ActivityType, boundary.ActivityTypeVersion,
            boundary.Descriptor, boundary.InputBindings, boundary.OutputCaptures, new Dictionary<string, string>(),
            childSlots: [new("graph-local-nodes", [setter])], activityContract: boundary.ActivityContract);
        var empty = new Dictionary<string, IReadOnlyCollection<string>>();
        var facts = new ActivityTemplateDataSelectionFacts(empty, empty, empty)
        {
            OriginalResultReads = ActivityTemplateOriginalResultReadFacts.Describe(root, change == "unprojected-source" ? null : source,
                analysis, dependencies, new Dictionary<string, ExecutableActivityTemplate> { [template.TemplateId] = template }, owner)
        };
        return (root, facts);
    }
}
