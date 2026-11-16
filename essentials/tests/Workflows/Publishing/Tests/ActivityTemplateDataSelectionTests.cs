using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Workflows.Publishing.Tests;

public sealed class ActivityTemplateDataSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Declared_workflow_request_context_stops_at_the_closed_body_and_does_not_open_variables(bool portable)
    {
        RuntimeInputBinding Request(string key) => portable ? Expression(new WorkflowRequestExpressionParameterBinding(key)) : RequestBinding(key);
        var root = Container("workflow-root", [], Node("ordinary", Request("legacy")),
            Closed("boundary", "body-key", [], Request("legacy"), Node("body", Request("body-key"))));
        var facts = ActivityTemplatePlacer.DescribeDataSelectionFacts(root, ["legacy"]);
        ActivityTemplateDataSelectionGuard.Validate(root, true, facts);
        var undeclared = Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(root, true,
            ActivityTemplatePlacer.DescribeDataSelectionFacts(root, [])));
        Assert.Contains(undeclared.Findings, finding => finding.Path == "ordinary/inputs/value" && finding.Type == "ActivityDataSelection/InputUnknown");
        var invalidBody = Closed("boundary", "body-key", [], Request("legacy"), Node("body", Request("legacy")));
        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(invalidBody, true,
            ActivityTemplatePlacer.DescribeDataSelectionFacts(invalidBody, ["legacy"])));
        Assert.Contains(rejected.Findings, finding => finding.Path == "body/inputs/value" && finding.Type == "ActivityDataSelection/InputUnknown");
        var variable = Node("variable", VariableBinding("missing", "workflow"));
        Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(variable, true,
            ActivityTemplatePlacer.DescribeDataSelectionFacts(variable, [])));
        var native = Node("native", Request("legacy"));
        Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(native, false,
            ActivityTemplatePlacer.DescribeDataSelectionFacts(native, [])));
    }

    [Fact]
    public async Task Existing_constructor_fails_closed_before_using_provider_or_store_services()
    {
        var compiler = new ActivityTemplateCompiler(null!, null!, null!, null!, null!, null!, TimeProvider.System);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(Request()).AsTask());
        Assert.Contains("shared activity data selection analyzer", error.Message);
    }

    [Fact]
    public async Task Located_source_findings_reject_the_exact_candidate_before_provider_or_dependency_work()
    {
        var findings = ImmutableArray.Create(new ValidationError("consumer/inputs/value", "ValueFlow/ProducerNotFound", "The producer is missing."));
        var analyzer = new Analyzer(findings);
        var projector = new Projector();
        var compiler = new ActivityTemplateCompiler(null!, null!, null!, null!, null!, null!, TimeProvider.System, [projector], analyzer);
        var rejected = await Assert.ThrowsAsync<ActivityDataSelectionRejectedException>(() => compiler.CompileAsync(Request()).AsTask());
        Assert.Collection(rejected.Findings, finding => Assert.Equal(findings[0], finding));
        Assert.Equal("actual-source", analyzer.Document!.ResourceId);
        Assert.Equal(projector.Revision, analyzer.Document.ContractRevision);
        Assert.Equal("ActivitySourceVersion", rejected.ResourceKind);
        Assert.Equal("actual-source", rejected.ResourceId);
        Assert.Equal("provider", rejected.ProviderKey);
    }

    [Fact]
    public async Task Duplicate_provider_document_ownership_is_a_composition_error()
    {
        var compiler = new ActivityTemplateCompiler(null!, null!, null!, null!, null!, null!, TimeProvider.System,
            [new Projector(), new Projector()], new Analyzer([]));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(Request()).AsTask());
        Assert.Contains("Multiple providers", error.Message);
    }

    [Fact]
    public void Public_request_identity_uses_the_actual_stage_without_a_name_key_union()
    {
        var contract = new DesignContract("1", [
            new("same-as-other-name", "PublicName", new("String"), false, true, null, "json"),
            new("other-key", "same-as-other-name", new("String"), false, true, null, "json")], [], []);
        var root = Node("consumer", RequestBinding("same-as-other-name"));
        ActivityTemplateDataSelectionGuard.Validate(root, contract.Inputs.Select(x => x.Name), false);
        ActivityTemplateDataSelectionGuard.Validate(root, contract.Inputs.Select(x => x.ReferenceKey), false);
        var wrongStage = Node("consumer", RequestBinding("PublicName"));
        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplateDataSelectionGuard.Validate(wrongStage, contract.Inputs.Select(x => x.ReferenceKey), false));
        Assert.Contains(rejected.Findings, x => x.Type == "ActivityDataSelection/InputUnknown");
        Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplateDataSelectionGuard.Validate(root, ["same-as-other-name", "same-as-other-name"], false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_public_request_is_rejected_for_direct_and_portable_reads(bool portable)
    {
        var binding = portable ? Expression(new WorkflowRequestExpressionParameterBinding("missing")) : RequestBinding("missing");
        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplateDataSelectionGuard.Validate(Node("consumer", binding), ["declared"], false));
        Assert.Contains(rejected.Findings, x => x.Path == "consumer/inputs/value" && x.Type == "ActivityDataSelection/InputUnknown");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_producers_are_rejected_even_with_a_prelinked_plan_or_portable_parameter(bool portable)
    {
        var identity = new ValueConversionPlan(1, ValueRepresentation.TextValue, new("String"), new("String"),
            ValueConversionMode.None, ValueConversionOperation.Identity, null, null, null);
        var binding = portable ? Expression(new ActivityResultExpressionParameterBinding("missing", "$result")) :
            new RuntimeInputBinding("value", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
                activityResult: new("missing", "$result", "workflow"), conversionPlan: identity);
        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplateDataSelectionGuard.Validate(Node("consumer", binding), [], true));
        Assert.Contains(rejected.Findings, x => x.Type == "ActivityDataSelection/ProducerUnknown");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_prior_analysis_does_not_authorize_a_changed_compiled_variable_key(bool portable)
    {
        var binding = portable ? Expression(new VariableExpressionParameterBinding("workflow", "changed")) :
            new RuntimeInputBinding("value", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
                variable: new("changed", "workflow"));
        var declarations = new Dictionary<string, IReadOnlyCollection<string>> { ["workflow"] = ["original"] };
        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplateDataSelectionGuard.Validate(Node("consumer", binding), [], true, declarations));
        Assert.Contains(rejected.Findings, x => x.Type == "ActivityDataSelection/VariableUnknown");
    }

    [Fact]
    public void A_closed_body_keeps_its_owner_request_contract_and_not_the_outer_workflow_contract()
    {
        var root = Node("body", RequestBinding("ReusableCustomer"));
        var members = new Dictionary<string, IReadOnlyCollection<string>> { ["body"] = ["ReusableCustomer"] };
        ActivityTemplateDataSelectionGuard.Validate(root, ["OuterCustomer"], true, requestMembersByConsumer: members);
        Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplateDataSelectionGuard.Validate(Node("body", RequestBinding("OuterCustomer")), ["OuterCustomer"], true,
                requestMembersByConsumer: members));
    }

    [Fact]
    public void Unknown_result_projections_are_rejected_even_when_the_producer_and_analysis_exist()
    {
        var binding = new RuntimeInputBinding("value", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new("producer", "missing-projection", "workflow"));
        var producerContract = new Elsa.Activities.Runtime.Core.Models.ActivityContract("native", "1", "native", JsonSerializer.SerializeToElement(new { }), [],
            new ActivityResultContract(new("String"), true, ActivityValuePolicy.Default, []), ["done"], new ActivityActivationRequirement("native", "native"));
        var producer = new ExecutableNode("producer", "producer", "native", "1", new("native", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(), activityContract: producerContract);
        var consumer = new ExecutableNode("consumer", "consumer", "native", "1", new("native", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = binding }, new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("children", [producer])]);
        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(consumer, [], true));
        Assert.Contains(rejected.Findings, x => x.Type == "ActivityDataSelection/ProjectionUnknown");
    }

    [Fact]
    public void A_variable_declaration_in_an_unrelated_compiled_scope_is_not_visible()
    {
        var variable = new RuntimeInputBinding("value", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
            variable: new("declared", "unrelated"));
        var declarations = new Dictionary<string, IReadOnlyCollection<string>> { ["unrelated"] = ["declared"] };
        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplateDataSelectionGuard.Validate(Node("consumer", variable), [], true, declarations));
        Assert.Contains(rejected.Findings, x => x.Type == "ActivityDataSelection/VariableScopeInvisible");
    }

    [Fact]
    public void Runtime_instance_metadata_does_not_require_a_provider_graph_or_business_value()
    {
        ActivityTemplateDataSelectionGuard.Validate(Node("consumer", Expression(
            new WorkflowInstanceExpressionParameterBinding(WorkflowInstanceExpressionParameterBinding.Members.InstanceId))), [], false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Owner_facts_admit_actual_workflow_and_container_variables_for_direct_and_portable_reads(bool portable)
    {
        var local = portable ? Expression(new VariableExpressionParameterBinding("container", "local")) : VariableBinding("local", "container");
        var child = Node("consumer", local);
        var root = Container("container", ["local"], child);
        var facts = ActivityTemplatePlacer.DescribeDataSelectionFacts(root, [],
            new Dictionary<string, IReadOnlyCollection<string>> { ["workflow"] = ["outer"] });
        ActivityTemplateDataSelectionGuard.Validate(root, true, facts);
        Assert.Equal(new[] { "workflow" }, facts.VisibleVariableScopesByConsumer["container"]);
        Assert.Contains("container", facts.VisibleVariableScopesByConsumer["consumer"]);
        var changed = Container("container", ["local"], Node("consumer", VariableBinding("missing", "container")));
        Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(changed, true,
            ActivityTemplatePlacer.DescribeDataSelectionFacts(changed, [])));
    }

    [Fact]
    public void Actual_authored_scope_projection_is_checked_against_its_compiled_contract_without_walking_opaque_JSON()
    {
        var authoredChild = new ActivityNode("consumer", "v", [], []);
        var authoredRoot = new ActivityNode("container", "v", [], [], new("owned", "1", JsonSerializer.SerializeToElement(new
        {
            variables = new[] { new { referenceKey = "spoof" } }
        })));
        var owner = new StructureOwner(authoredRoot, authoredChild);
        var compiled = Container("container", ["actual"], Node("consumer", VariableBinding("actual", "container")));
        var facts = ActivityTemplatePlacer.DescribeDataSelectionFacts(compiled, [], authoredRoot: authoredRoot, structures: owner);
        ActivityTemplateDataSelectionGuard.Validate(compiled, true, facts);
        Assert.True(owner.Projected);
        Assert.Equal(new[] { "actual" }, facts.VariableDeclarations["container"]);
        var corrupted = Container("container", ["spoof"], Node("consumer", VariableBinding("spoof", "container")));
        var rejected = Assert.Throws<ActivityDataSelectionRejectedException>(() =>
            ActivityTemplatePlacer.DescribeDataSelectionFacts(corrupted, [], authoredRoot: authoredRoot, structures: owner));
        Assert.Contains(rejected.Findings, finding => finding.Type == "ActivityDataSelection/VariableDeclarationMismatch");
    }

    [Fact]
    public void Closed_boundary_itself_keeps_caller_names_but_body_uses_reference_keys_and_private_variables()
    {
        var consumer = Node("body", RequestBinding("body-key"));
        var variable = Node("body-variable", VariableBinding("private", "boundary"));
        var root = Closed("boundary", "body-key", ["private"], RequestBinding("OuterName"), consumer, variable);
        var outer = new Dictionary<string, IReadOnlyCollection<string>> { ["workflow"] = ["outer"] };
        var facts = ActivityTemplatePlacer.DescribeDataSelectionFacts(root, ["OuterName"], outer);
        ActivityTemplateDataSelectionGuard.Validate(root, true, facts);
        Assert.Equal(new[] { "OuterName" }, facts.RequestMembersByConsumer["boundary"]);
        Assert.Equal(new[] { "body-key" }, facts.RequestMembersByConsumer["body"]);
        Assert.DoesNotContain("workflow", facts.VisibleVariableScopesByConsumer["body"]);
        var outerCapture = Closed("boundary", "body-key", ["private"], RequestBinding("OuterName"),
            Node("body", VariableBinding("outer", "workflow")));
        Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(outerCapture, true,
            ActivityTemplatePlacer.DescribeDataSelectionFacts(outerCapture, ["OuterName"], outer)));
        var wrongName = Closed("boundary", "body-key", ["private"], RequestBinding("OuterName"), Node("body", RequestBinding("PublicDisplayName")));
        Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(wrongName, true,
            ActivityTemplatePlacer.DescribeDataSelectionFacts(wrongName, ["OuterName"], outer)));
    }

    [Fact]
    public void Nearest_nested_closed_boundary_resets_body_requests_and_variable_visibility()
    {
        var inner = Closed("inner", "inner-key", ["inner-var"], RequestBinding("outer-key"),
            Node("inner-body", RequestBinding("inner-key")), Node("inner-variable", VariableBinding("inner-var", "inner")));
        var root = Closed("outer", "outer-key", ["outer-var"], null, inner);
        var facts = ActivityTemplatePlacer.DescribeDataSelectionFacts(root, ["WorkflowName"]);
        ActivityTemplateDataSelectionGuard.Validate(root, true, facts);
        Assert.Equal(new[] { "outer-key" }, facts.RequestMembersByConsumer["inner"]);
        Assert.Equal(new[] { "inner-key" }, facts.RequestMembersByConsumer["inner-body"]);
        Assert.Equal(new[] { "inner" }, facts.VisibleVariableScopesByConsumer["inner-variable"]);
        var capture = Closed("inner", "inner-key", ["inner-var"], RequestBinding("outer-key"),
            Node("inner-body", VariableBinding("outer-var", "outer")));
        var invalid = Closed("outer", "outer-key", ["outer-var"], null, capture);
        Assert.Throws<ActivityDataSelectionRejectedException>(() => ActivityTemplateDataSelectionGuard.Validate(invalid, true,
            ActivityTemplatePlacer.DescribeDataSelectionFacts(invalid, [])));
    }

    [Fact]
    public void Native_literal_request_and_workflow_instance_reads_need_no_source_graph_or_variable_capture()
    {
        var literal = new RuntimeInputBinding("value", new("String"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new("String"), JsonSerializer.SerializeToElement("hello"), ValueProtectionPolicy.InstanceInline));
        foreach (var binding in new[] { literal, RequestBinding("PublicName"), Expression(new WorkflowInstanceExpressionParameterBinding(
                     WorkflowInstanceExpressionParameterBinding.Members.InstanceId)) })
        {
            var root = Node("native", binding);
            ActivityTemplateDataSelectionGuard.Validate(root, false,
                ActivityTemplatePlacer.DescribeDataSelectionFacts(root, ["PublicName"]));
        }
    }

    private static RuntimeInputBinding VariableBinding(string key, string scope) => new("value", new("String"),
        ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead, variable: new(key, scope));

    private static ExecutableNode Container(string id, string[] keys, params ExecutableNode[] children) =>
        new(id, id, "native", "1", new("native", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
            childSlots: [new("body", children)], structure: new("owned", "1", JsonSerializer.SerializeToElement(new
            {
                variables = keys.Select(key => new RuntimeVariableDeclaration(key, key, new("String"), ValueProtectionPolicy.InstanceInline)).ToArray()
            }, DescriptorPayloadSerializer.Options)));

    private static ExecutableNode Closed(string id, string inputKey, string[] keys, RuntimeInputBinding? boundaryInput, params ExecutableNode[] children)
    {
        var descriptor = new RuntimeActivityDescriptor("elsa.graph-activity", "1", JsonSerializer.SerializeToElement(new
        {
            scopeModel = RuntimeMetadataKeys.ActivityExecutionScopeClosed,
            variables = keys.Select(key => new { referenceKey = key, name = key, type = new TypeReference("String"), storageDriverKey = "elsa.json" })
        }));
        var contract = new Elsa.Activities.Runtime.Core.Models.ActivityContract("graph", "1", "graph", descriptor.Payload,
            [new Elsa.Activities.Runtime.Core.Models.ActivityInputContract(inputKey, "PublicDisplayName", new("String"), false, true, false, null, ActivityValuePolicy.Default)],
            new(new("Object"), true, ActivityValuePolicy.Default, []), ["Done"], new("graph", id));
        return new(id, id, "graph", "1", descriptor,
            boundaryInput is null ? new Dictionary<string, RuntimeInputBinding>() : new Dictionary<string, RuntimeInputBinding> { [boundaryInput.InputKey] = boundaryInput },
            new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string> { [RuntimeMetadataKeys.ActivityExecutionScope] = RuntimeMetadataKeys.ActivityExecutionScopeClosed },
            childSlots: [new("body", children)], activityContract: contract);
    }

    private sealed class StructureOwner(ActivityNode root, ActivityNode child) : IActivityStructureService
    {
        public bool SupportsScopedVariables(ActivityNode activity) => activity.NodeId == root.NodeId;
        public bool Projected { get; private set; }
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => activity.NodeId == root.NodeId ? [new("body", [child])] : [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> children) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity)
        {
            Projected = true;
            return activity.NodeId == root.NodeId ? [new("actual", "Actual", new("String"), null, null)] : [];
        }
    }

    private static RuntimeInputBinding RequestBinding(string key) => new("value", new("String"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new(key));

    private static RuntimeInputBinding Expression(ExpressionParameterBinding parameter) => new("value", new("String"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Expression, expression: new("JavaScript", "source", parameters: new Dictionary<string, ExpressionParameterBinding> { ["source"] = parameter }));

    private static ExecutableNode Node(string id, RuntimeInputBinding binding) => new(id, id, "native", "1",
        new("native", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding },
        new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());

    private static ActivityTemplateCompilerRequest Request() => new(new ActivityDefinition { Id = "definition", ActivityTypeKey = "type", Category = "Test" },
        new ActivityTemplateCandidate("ActivitySourceVersion", "actual-source", 3, new("1", [], [], []),
            new("provider", "1", JsonSerializer.SerializeToElement(new { }))), "version", "1.0.0", 0);

    private sealed class Projector : IActivityDataSelectionDocumentProjector
    {
        public string? Revision { get; private set; }
        public ValueTask<ActivityDataSelectionDocument?> TryProjectAsync(ActivityProviderManifest manifest, DesignContract contract, string resourceId,
            string contractRevision, CancellationToken cancellationToken = default)
        {
            Revision = contractRevision;
            return ValueTask.FromResult<ActivityDataSelectionDocument?>(new("ActivityDefinition", resourceId, contractRevision, null, [], [], []));
        }
    }

    private sealed class Analyzer(ImmutableArray<ValidationError> findings) : IActivityDataSelectionAnalyzer
    {
        public ActivityDataSelectionDocument? Document { get; private set; }
        public ValueTask<ActivityDataSelectionAnalysis> AnalyzeAsync(ActivityDataSelectionDocument document, ActivityDataSelectionTarget? target = null,
            ActivityDataSelectionQuery? query = null, CancellationToken cancellationToken = default)
        {
            Document = document;
            return ValueTask.FromResult(new ActivityDataSelectionAnalysis(document.ContractRevision, null, [], findings));
        }
    }
}
