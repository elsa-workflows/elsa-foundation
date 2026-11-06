using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Workflows.Publishing.Tests;

public sealed class ActivityTemplateDataSelectionTests
{
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
        Assert.Equal(findings, rejected.Findings);
        Assert.Equal("actual-source", analyzer.Document!.ResourceId);
        Assert.Equal(projector.Revision, analyzer.Document.ContractRevision);
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
