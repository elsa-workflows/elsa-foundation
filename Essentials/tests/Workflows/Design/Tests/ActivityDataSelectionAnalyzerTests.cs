using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed class ActivityDataSelectionAnalyzerTests
{
    [Theory]
    [InlineData("Workflow", "PublicName", "stable-key")]
    [InlineData("WorkflowDefinition", "PublicName", "stable-key")]
    [InlineData("ActivityDefinition", "stable-key", "PublicName")]
    public async Task Request_identity_uses_its_actual_boundary_stage_without_a_name_key_union(string resourceKind, string accepted, string rejected)
    {
        using var provider = Provider();
        var document = Document(Sequence(Node("consumer"))) with { ResourceKind = resourceKind,
            Inputs = [new("stable-key", "PublicName", new("String"), null, "Public", null, true)] };
        var query = new ActivityDataSelectionQuery { Source = new(JsonSerializer.SerializeToElement(new { memberKey = accepted }), "WorkflowRequest") };
        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), query);
        Assert.Empty(result.Findings);
        var canonical = Assert.IsType<JsonElement>(Assert.Single(result.Sources).Binding.Value);
        Assert.Equal(accepted, canonical.GetProperty("memberKey").GetString());
        var invalid = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), query with
            { Source = new(JsonSerializer.SerializeToElement(new { memberKey = rejected }), "WorkflowRequest") });
        Assert.Contains(invalid.Findings, finding => finding.Type == "ActivityData/UnknownInput");
        var stable = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), query with
            { Source = new(new VariableReference("stable-key"), "Input") });
        Assert.Empty(stable.Findings);
        var wrongStable = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), query with
            { Source = new(new VariableReference("PublicName"), "Input") });
        Assert.Contains(wrongStable.Findings, finding => finding.Type == "ActivityData/UnknownInput");
    }

    [Fact]
    public async Task Only_legacy_workflow_requests_are_open_when_no_input_contract_is_declared()
    {
        using var provider = Provider();
        var document = Document(Sequence(Node("consumer"))) with { ResourceKind = "Workflow" };
        var query = new ActivityDataSelectionQuery { Source = new(JsonSerializer.SerializeToElement(new { memberKey = "legacy", path = "CustomerId" }), "WorkflowRequest") };
        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), query);
        Assert.Empty(result.Findings);
        var source = Assert.Single(result.Sources);
        Assert.Equal(ActivitySerializedShapeState.Unknown, source.Shape.State);
        Assert.Null(source.Shape.Type);
        var closed = await Analyzer(provider).AnalyzeAsync(document with { ResourceKind = "ActivityDefinition" }, new("consumer", "CustomerId"), query);
        Assert.Contains(closed.Findings, finding => finding.Type == "ActivityData/UnknownInput");
        var stable = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), query with
            { Source = new(new VariableReference("legacy"), "Input") });
        Assert.Contains(stable.Findings, finding => finding.Type == "ActivityData/UnknownInput");
    }

    [Fact]
    public async Task Untouched_target_comes_from_actual_catalog_and_metadata_absence_stays_unknown()
    {
        using var provider = Provider();
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer"))), new("consumer", "CustomerId"));
        Assert.Empty(result.Findings);
        Assert.Equal(new TypeReference("String"), result.Target!.Type);
        Assert.Equal("Customer ID", result.Target.DisplayName);
        var source = Assert.Single(result.Sources, source => source.Id == "activity:producer:$result");
        Assert.Equal(ActivitySerializedShapeState.Unknown, source.Shape.State);
        Assert.Empty(source.Shape.Members);
        Assert.Equal("NoMetadataOwner", source.Shape.ReasonCode);
        Assert.Equal(ActivityChildResultAvailability.Definite, source.Availability);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("typed")]
    [InlineData("json")]
    public async Task Missing_producer_is_invalid_even_when_occurrence_is_optional(string form)
    {
        using var provider = Provider();
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("consumer", Read(form, "missing", optional: true)))));
        Assert.Equal("consumer/inputs/CustomerId", Assert.Single(result.Findings, finding => finding.Type == "ValueFlow/UnavailableProducer").Path);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("typed")]
    [InlineData("json")]
    public async Task Named_projection_is_resolved_from_the_actual_producer_contract_without_metadata(string form)
    {
        using var provider = Provider();
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Read(form, "producer", "forged-output")))));
        Assert.Contains(result.Findings, finding => finding.Type == "ActivityData/UnknownProjection");
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("typed")]
    [InlineData("json")]
    public async Task Direct_and_portable_results_keep_the_same_structured_scope_boundary(string form)
    {
        using var provider = Provider();
        var inner = Sequence(Node("producer")) with { NodeId = "inner" };
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(inner, Node("consumer", Read(form, "producer")))));
        Assert.Equal("consumer/inputs/CustomerId", Assert.Single(result.Findings, finding => finding.Type == "ValueFlow/ScopeBoundary").Path);
    }

    [Fact]
    public async Task An_untouched_intrinsic_value_uses_the_actual_selected_variable_declaration()
    {
        using var provider = Provider();
        var intrinsic = Node("set") with { Intrinsic = new(AuthoredWorkflowIntrinsicKind.Set, new("Int32"), new("customer")) };
        var document = Document(Sequence(intrinsic)) with { Variables = [Variable("customer", "Customer")] };
        var result = await Analyzer(provider).AnalyzeAsync(document, new("set", "value"));
        Assert.Equal(new TypeReference("String"), result.Target!.Type);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task A_cleared_switch_predicate_uses_its_structure_owned_key_and_boolean_contract()
    {
        using var provider = Provider();
        var node = Node("switch") with { Structure = new(Elsa.Activities.Switch.Activities.Switch.StructureKind,
            Elsa.Activities.Switch.Activities.Switch.StructureSchemaVersion, JsonSerializer.SerializeToElement(new {
                cases = new[] { new { caseId = "a", match = "Approved", condition = ArgumentState.Null("foreign"), activity = Node("branch") } },
                @default = (ActivityNode?)null
            }, Web)) };
        var key = Elsa.Activities.Switch.Activities.Switch.ConditionInputKey("a");
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(node)), new("switch", key));
        Assert.Empty(result.Findings);
        Assert.True(result.Target!.IsStructureOwned);
        Assert.Equal(new TypeReference("Boolean"), result.Target.Type);
        Assert.Equal(key, result.Target.Target.InputKey);
    }

    [Theory]
    [InlineData("Input")]
    [InlineData("Variable")]
    [InlineData("ActivityResult")]
    public async Task Existing_whole_roles_refuse_ignored_member_paths(string role)
    {
        using var provider = Provider();
        var payload = JsonSerializer.SerializeToElement(new { referenceKey = "customer", producerNodeId = "producer", projectionKey = "$result",
            producerScopeId = "root", memberPath = new[] { "CustomerId" } });
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Input(new(payload, role))))));
        Assert.Contains(result.Findings, finding => finding.Type == "ActivityData/UnsupportedMemberSelection");
    }

    [Theory]
    [InlineData("InputMember")]
    [InlineData("WorkflowRequestMember")]
    [InlineData("VariableMember")]
    [InlineData("ActivityResultMember")]
    public async Task Reserved_member_roles_are_not_advertised_as_supported(string role)
    {
        using var provider = Provider();
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("consumer", Input(new(JsonSerializer.SerializeToElement(new { }), role))))));
        Assert.Contains(result.Findings, finding => finding.Type == "ActivityData/UnsupportedMemberBinding");
    }

    [Fact]
    public async Task Focus_re_resolves_identity_and_retains_supplied_runtime_scope_separately_from_display_scope()
    {
        using var provider = Provider();
        var binding = Result("producer", scope: "scope:root");
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer"))), new("consumer", "CustomerId"), new() { Source = binding });
        Assert.Empty(result.Findings);
        var source = Assert.Single(result.Sources);
        Assert.Equal("root", source.ScopeId);
        Assert.Equal("scope:root", Assert.IsType<JsonElement>(source.Binding.Value).GetProperty("producerScopeId").GetString());
        Assert.Equal("producer", Assert.IsType<JsonElement>(source.Binding.Value).GetProperty("producerNodeId").GetString());
    }

    [Fact]
    public async Task Focus_returns_only_canonical_identity_fields_and_never_echoes_ignored_business_data()
    {
        using var provider = Provider();
        var binding = new ArgumentValue(JsonSerializer.SerializeToElement(new {
            producerNodeId = "producer", projectionKey = "$result", producerScopeId = "scope:root", isOptional = false,
            privateBusinessData = "must-not-be-returned"
        }), "ActivityResult");
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer"))),
            new("consumer", "CustomerId"), new() { Source = binding });
        var payload = Assert.IsType<JsonElement>(Assert.Single(result.Sources).Binding.Value);
        Assert.False(payload.TryGetProperty("privateBusinessData", out _));
        Assert.Equal("scope:root", payload.GetProperty("producerScopeId").GetString());
    }

    [Fact]
    public async Task Target_only_analysis_precedes_source_metadata_and_preserves_actual_target_restrictions()
    {
        var shape = new TestShapeSource();
        using var provider = Provider(services => services.AddSingleton<IActivitySerializedShapeSource>(shape));
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer"))),
            new("consumer", "CustomerId"), new() { IncludeSources = false });
        Assert.Empty(result.Findings);
        Assert.NotNull(result.Target);
        Assert.Empty(result.Sources);
        Assert.Equal(0, shape.Calls);
    }

    [Fact]
    public async Task Request_local_input_index_preserves_ambiguous_declaration_refusal()
    {
        using var provider = Provider();
        var document = Document(Sequence(Node("consumer", Input(new(new VariableReference("customer"), "Input")))))
            with { Inputs = [Definition("customer"), Definition("customer")] };
        var result = await Analyzer(provider).AnalyzeAsync(document);
        Assert.Contains(result.Findings, finding => finding.Type == "ActivityData/UnknownInput");
    }

    [Theory]
    [InlineData("activity.shape.no-typed-result", true)]
    [InlineData("activity.shape.metadata-unavailable", false)]
    [InlineData("activity.shape.source-contract-mismatch", false)]
    public async Task Only_definitive_source_bound_absence_rejects_a_whole_result(string reason, bool invalid)
    {
        using var provider = Provider(services => services.AddSingleton<IActivitySerializedShapeSource>(new MissingResultShape(reason)));
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer", Read("direct", "producer")))));
        Assert.Equal(invalid, result.Findings.Any(finding => finding.Type == "ActivityData/UnknownProjection"));
    }

    [Fact]
    public async Task Graph_boundary_alias_resolves_the_existing_workflow_variable_without_a_synthetic_draft()
    {
        using var provider = Provider();
        var document = Document(Sequence(Node("consumer", Input(new(new VariableReference("customer", "activity-graph"), "Variable")))))
            with { ResourceKind = "ActivityDefinition", Variables = [Variable("customer", "Customer")] };
        var result = await Analyzer(provider).AnalyzeAsync(document);
        Assert.Empty(result.Findings);
        var sources = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"));
        var variable = Assert.Single(sources.Sources, source => source.Id.StartsWith("variable:", StringComparison.Ordinal));
        Assert.Equal("activity-graph", Assert.IsType<VariableReference>(variable.Binding.Value).DeclaringScopeId);
    }

    [Fact]
    public async Task Nested_scope_variables_are_visible_only_to_their_owned_descendants()
    {
        using var provider = Provider();
        var inner = Sequence(Node("inside")) with { NodeId = "inner" };
        inner = inner with { Structure = new("elsa.sequence.structure", "1.0.0", JsonSerializer.SerializeToElement(new {
            activities = new[] { Node("inside", Input(new(new VariableReference("local", "inner"), "Variable"))) },
            variables = new[] { Variable("local", "Local") }
        }, Web)) };
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(inner,
            Node("outside", Input(new(new VariableReference("local", "inner"), "Variable"))))));
        Assert.Equal("outside/inputs/CustomerId", Assert.Single(result.Findings).Path);
    }

    [Fact]
    public async Task Focused_literal_member_names_are_not_split_and_the_response_has_one_global_budget()
    {
        var shape = new TestShapeSource();
        using var provider = Provider(services => services.AddSingleton<IActivitySerializedShapeSource>(shape));
        var document = Document(Sequence(Node("producer"), Node("consumer"))) with { Inputs = [Definition("first"), Definition("second")] };
        var query = new ActivityDataSelectionQuery { ShapeLimits = new(4, 3) };
        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), query);
        Assert.Equal(3, result.Sources.Sum(source => Count(source.Shape)));
        Assert.Contains(result.Sources, source => source.Shape.State == ActivitySerializedShapeState.Truncated);
        var focused = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), new() {
            Source = Result("producer"), MemberPath = ["a.b", ""], ShapeLimits = new(2, 2)
        });
        Assert.Empty(focused.Findings);
        Assert.Equal(new[] { "a.b", "" }, shape.LastPath);
    }

    [Fact]
    public async Task More_than_one_matching_metadata_owner_refuses_instead_of_selecting_one()
    {
        using var provider = Provider(services => {
            services.AddSingleton<IActivitySerializedShapeSource>(new TestShapeSource());
            services.AddSingleton<IActivitySerializedShapeSource>(new TestShapeSource());
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Analyzer(provider).AnalyzeAsync(
            Document(Sequence(Node("producer"), Node("consumer"))), new("consumer", "CustomerId")));
    }

    [Fact]
    public async Task Owner_constraints_intersect_for_untouched_targets_and_admission_of_actual_bindings()
    {
        using var provider = Provider(services => {
            services.AddSingleton<IActivityInputSourceConstraintProvider>(new Constraint(["Literal", "Input"]));
            services.AddSingleton<IActivityInputSourceConstraintProvider>(new Constraint(["Literal", "Variable"]));
        });
        var document = Document(Sequence(Node("consumer", Input(new(new VariableReference("customer"), "Input"))))) with { Inputs = [Definition("customer")] };
        var browser = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"));
        Assert.Equal(new[] { "Literal" }, browser.Target!.Constraint!.AllowedExpressionTypes);
        Assert.Empty(browser.Sources);
        Assert.Contains((await Analyzer(provider).AnalyzeAsync(document)).Findings, finding => finding.Type == "ActivityData/SourceRestricted");
    }

    [Fact]
    public async Task Credential_target_remains_secret_only_and_sensitive_metadata_never_contains_a_value()
    {
        var catalog = Catalog(new InputDefinition("CustomerId", "CustomerId", new("String"), null, "Customer ID", null, true,
            IsSensitive: true, IsCredential: true));
        using var provider = ActivityDataSelectionTestSupport.Provider(catalog);
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), Node("consumer"))), new("consumer", "CustomerId"));
        Assert.Empty(result.Sources);
        Assert.True(result.Target!.IsCredential);
        Assert.Equal(new[] { "Secret" }, result.Target.Constraint!.AllowedExpressionTypes);
    }

    [Fact]
    public async Task A_credential_default_request_uses_the_existing_rule_even_when_it_has_no_value()
    {
        using var provider = ActivityDataSelectionTestSupport.Provider(Catalog(Definition("CustomerId") with { IsCredential = true }));
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("consumer", Input(new(null, "Default"))))));
        Assert.Contains(result.Findings, finding => finding.Type == CredentialInputBinding.RuleId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_connection_declarations_remain_connection_only(bool declaresConnectionType)
    {
        var definition = Definition("CustomerId") with {
            Type = new(declaresConnectionType ? "String" : "Elsa.Activities.Runtime.Core.Models.ConnectionReference"),
            ConnectionType = declaresConnectionType ? "test-connection" : null
        };
        using var provider = ActivityDataSelectionTestSupport.Provider(Catalog(definition));
        var document = Document(Sequence(Node("producer"), Node("consumer", Input(new("fixed-text", "Literal")))));
        var browser = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"));
        Assert.Equal(new[] { "Connection" }, browser.Target!.Constraint!.AllowedExpressionTypes);
        Assert.Empty(browser.Sources);
        Assert.Contains((await Analyzer(provider).AnalyzeAsync(document)).Findings, finding => finding.Type == ConnectionInputBinding.RuleId);
    }

    [Theory]
    [InlineData("Secret")]
    [InlineData("Connection")]
    public async Task Reference_rules_never_serialize_an_unknown_CLR_business_instance(string syntax)
    {
        var business = new ReferenceGetterTrap();
        var definition = Definition("CustomerId") with { IsCredential = syntax == "Secret" ? true : null,
            ConnectionType = syntax == "Connection" ? "test-connection" : null };
        using var provider = ActivityDataSelectionTestSupport.Provider(Catalog(definition));
        // Keep the original CLR instance: the JSON-backed Sequence fixture serializes its children.
        var document = Document(Node("consumer", Input(new(business, syntax))));
        var result = await Analyzer(provider).AnalyzeAsync(document);
        Assert.Contains(result.Findings, finding => finding.Type == (syntax == "Secret" ? CredentialInputBinding.RuleId : ConnectionInputBinding.RuleId));
        await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"));
        Assert.Equal(0, business.Reads);
    }

    [Theory]
    [InlineData("Secret")]
    [InlineData("Connection")]
    public async Task Actual_sealed_reference_records_are_read_only_through_their_canonical_members(string syntax)
    {
        var definition = Definition("CustomerId") with { IsCredential = syntax == "Secret" ? true : null,
            ConnectionType = syntax == "Connection" ? "test-connection" : null };
        object reference = syntax == "Secret"
            ? SecretReferencePayload.Read(JsonSerializer.SerializeToElement(new { name = "test-secret" })).Reference!
            : ConnectionReferencePayload.Read(JsonSerializer.SerializeToElement(new { connection = "test-connection" })).Reference!;
        using var provider = ActivityDataSelectionTestSupport.Provider(Catalog(definition));
        // A typed ConnectionReferenceMembers has Name, whereas its canonical JSON payload has connection.
        // Test the actual admitted record before any structure fixture turns it into a different JSON shape.
        Assert.Empty((await Analyzer(provider).AnalyzeAsync(Document(Node("consumer", Input(new(reference, syntax)))))).Findings);
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("Object")]
    [InlineData("Default")]
    [InlineData("CustomProvider")]
    public async Task Literal_business_objects_are_not_serialized_or_interpreted_as_portable_expressions(string syntax)
    {
        using var provider = Provider();
        var business = new ThrowingBusinessValue();
        var document = Document(Node("consumer", Input(new(business, syntax))));
        var result = await Analyzer(provider).AnalyzeAsync(document);
        Assert.Empty(result.Findings);
        await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"));
        Assert.Equal(0, business.Reads);
    }

    [Theory]
    [InlineData("Literal")]
    [InlineData("Object")]
    public async Task A_typed_expression_definition_stored_as_data_does_not_bind_its_parameters(string syntax)
    {
        using var provider = Provider();
        var businessData = Read("typed", "missing-producer").Value.Value;
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("consumer", Input(new(businessData, syntax))))));
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Compensation_handler_keeps_its_own_and_prior_forward_results_and_nearest_first_rank()
    {
        var body = new ExpressionDefinition("JavaScript", "own + prior", new("String"),
            new Dictionary<string, ExpressionParameterBinding> {
                ["own"] = new ActivityResultExpressionParameterBinding("deliver", "$result"),
                ["prior"] = new ActivityResultExpressionParameterBinding("reserve", "$result")
            }, JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingPureV1);
        var scope = new ActivityNode("compensating-scope", "test", [], [], new(
            Elsa.Activities.Compensation.Activities.CompensationScope.StructureKind,
            Elsa.Activities.Compensation.Activities.CompensationScope.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new Elsa.Activities.Compensation.Models.CompensationScopeAuthoredStructure([
                new("Inventory", Node("reserve"), Node("release")),
                new("Delivery", Node("deliver"), Node("cancel", Input(new(body, "JavaScript")))),
                new("Confirm", Node("confirm"), null)
            ]), Web)));
        using var provider = Provider(services => new Elsa.Activities.Compensation.ActivitiesCompensationFeature().ConfigureServices(services));
        Assert.Empty((await Analyzer(provider).AnalyzeAsync(Document(scope))).Findings);
        var browser = await Analyzer(provider).AnalyzeAsync(Document(scope), new("cancel", "CustomerId"));
        var results = browser.Sources.Where(source => source.Binding.ExpressionType == "ActivityResult" &&
            Assert.IsType<JsonElement>(source.Binding.Value).GetProperty("projectionKey").GetString() == "$result").ToArray();
        Assert.Equal(new[] { "activity:deliver:$result", "activity:reserve:$result" }, results.Take(2).Select(source => source.Id));
        Assert.All(results.Take(2), source => Assert.Equal(ActivityChildResultAvailability.Definite, source.Availability));
    }

    private static IActivityDataSelectionAnalyzer Analyzer(ServiceProvider provider) => provider.GetRequiredService<IActivityDataSelectionAnalyzer>();
    private static ServiceProvider Provider(Action<IServiceCollection>? configure = null) => ActivityDataSelectionTestSupport.Provider(Catalog(), configure: configure);
    private static StubActivityCatalog Catalog(InputDefinition? input = null) => new StubActivityCatalog().Add("test", [input ?? Definition("CustomerId")],
        [new("known", "known", new("String"), null, "Known output", null, false)]).Add("sequence");
    private static InputDefinition Definition(string key) => new(key, key, new("String"), null, key == "CustomerId" ? "Customer ID" : key, null, true);
    private static VariableDefinition Variable(string key, string name) => new(key, name, new("String"), null, null);
    private static ActivityDataSelectionDocument Document(ActivityNode root) => new("WorkflowDefinition", "actual-draft", "exact-candidate-sha", root, [], [], []);
    private static ActivityNode Node(string id, params ArgumentState[] inputs) => new(id, "test", inputs, []);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static ActivityNode Sequence(params ActivityNode[] children) => new("root", "sequence", [], [],
        new("elsa.sequence.structure", "1.0.0", JsonSerializer.SerializeToElement(new { activities = children, variables = Array.Empty<VariableDefinition>() }, Web)));
    private static ArgumentState Input(ArgumentValue value) => new("CustomerId", value, null, null, null, null);
    private static ArgumentValue Result(string producer, string projection = "$result", bool optional = false, string scope = "root") => new(
        JsonSerializer.SerializeToElement(new { producerNodeId = producer, projectionKey = projection, producerScopeId = scope, isOptional = optional }), "ActivityResult");
    private static ArgumentState Read(string form, string producer, string projection = "$result", bool optional = false)
    {
        var definition = new ExpressionDefinition("JavaScript", "p", new("String"),
            new Dictionary<string, ExpressionParameterBinding> { ["p"] = new ActivityResultExpressionParameterBinding(producer, projection, optional) },
            JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingPureV1);
        return Input(form switch {
            "direct" => Result(producer, projection, optional),
            "typed" => new(definition, "JavaScript"),
            _ => new(JsonSerializer.SerializeToElement(definition, Web), "JavaScript")
        });
    }
    private static int Count(ActivitySerializedShape shape) => shape.Members.Sum(member => 1 + Count(member.Shape))
        + (shape.Element is { } element ? 1 + Count(element) : 0) + (shape.MapValue is { } map ? 1 + Count(map) : 0);
    private sealed class ThrowingBusinessValue
    {
        public int Reads;
        public string Value { get { Reads++; throw new InvalidOperationException("Business getter must not run."); } }
    }
    private sealed class ReferenceGetterTrap
    {
        public int Reads;
        public string Name { get { Reads++; throw new InvalidOperationException("Business getter must not run."); } }
    }
    private sealed class Constraint(ImmutableArray<string> allowed) : IActivityInputSourceConstraintProvider
    {
        public ValueTask<ActivityDataInputConstraint?> GetConstraintAsync(IActivityDefinitionVersion activityVersion, string inputKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivityDataInputConstraint?>(new("test-owner", allowed, "ActivityData/SourceRestricted", "A fixed owning contract restricts this input."));
    }
    private sealed class TestShapeSource : IActivitySerializedShapeSource
    {
        public int Calls { get; private set; }
        public ImmutableArray<string> LastPath { get; private set; } = [];
        public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion, string projectionKey,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) => Describe(memberPath);
        public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) => Describe(memberPath);
        private ValueTask<ActivitySerializedShape?> Describe(ImmutableArray<string> path)
        {
            Calls++;
            LastPath = path;
            var scalar = new ActivitySerializedShape(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Scalar, new("String"), "contract", "profile", "metadata");
            return ValueTask.FromResult<ActivitySerializedShape?>(new(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Object, new("Object"), "contract", "profile", "metadata") {
                Members = [new("a.b", "A dot field", scalar), new("", "Empty field", scalar)]
            });
        }
    }

    private sealed class MissingResultShape(string reason) : IActivitySerializedShapeSource
    {
        public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion, string projectionKey,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivitySerializedShape?>(new(ActivitySerializedShapeState.Unavailable, ActivitySerializedValueKind.Unknown,
                null, "actual-contract", "actual-profile", "actual-metadata") { ReasonCode = reason });
        public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) => ValueTask.FromResult<ActivitySerializedShape?>(null);
    }
}
