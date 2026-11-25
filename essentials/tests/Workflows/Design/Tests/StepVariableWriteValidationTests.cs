using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

/// <summary>
/// Spec 1231 (S-7) publish checks of step writes: variable-reference bindings (FR-024, User Story 2 scenario 4), step
/// writers in the concurrent-write rule (FR-028, User Story 4 scenario 4) and the overlap with the node's own capture
/// (FR-029, User Story 3 scenario 7).
/// </summary>
public sealed partial class StepVariableWriteValidationTests
{
    private const string WriterVersion = "test.writer@1";
    private const string ReaderVersion = "test.reader@1";
    private const string MarkedVersion = "test.marked@1";
    private const string ScriptVersion = "test.script@1";
    private const string DeclaredScriptVersion = "test.declared-script@1";

    [Fact]
    public async Task An_unbound_variable_reference_is_refused_naming_the_input()
    {
        var errors = await Validate(Sequence("root", new ActivityNode("writer", WriterVersion, [], [])));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/VariableReference");
        Assert.Equal("writer/inputs/Balance", error.Path);
        Assert.Contains("must be bound to a declared variable", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(ValidationSeverities.Warning, error.Severity);
    }

    [Fact]
    public async Task A_variable_reference_bound_to_another_expression_is_refused()
    {
        var node = new ActivityNode("writer", WriterVersion, [new ArgumentState("Balance", ArgumentValue.FromClr(5, "Literal"), null, null, null, null)], []);

        var error = Assert.Single(await Validate(Sequence("root", node)), item => item.Type == "ValueFlow/VariableReference");
        Assert.Contains("not to a 'Literal' expression", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_variable_reference_bound_to_an_undeclared_variable_is_refused()
    {
        var errors = await Validate(Sequence("root", Writer("writer", "missing")));

        var error = Assert.Single(errors, item => item.Type == "ValueFlow/VariableReference");
        Assert.Contains("'missing' in scope 'workflow', which is not declared", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reference_bound_to_a_key_its_container_does_not_declare_is_refused_in_either_direction()
    {
        // The enclosing Sequence ("root") declares no "currentItem" (a loop item is not a declared variable yet) nor "tpyo".
        var errors = await Validate(Sequence("root",
            Writer("writer", "currentItem", scopeId: "root"),
            Reference("reader", ReaderVersion, "Amount", "tpyo", scopeId: "root")));

        var refused = errors.Where(item => item.Type == "ValueFlow/VariableReference").OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        Assert.Equal(["reader/inputs/Amount", "writer/inputs/Balance"], refused.Select(item => item.Path));
        Assert.All(refused, item => Assert.Contains("which is not declared in a scope visible to the node", item.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_marked_activities_in_parallel_warn_on_a_container_variable_nobody_else_writes()
    {
        var inner = new VariableDefinition("inner", "inner", new TypeReference("Int32"), null, null);
        var errors = await Validate(Structured("root", "elsa.sequence.structure", new
        {
            activities = new[]
            {
                Parallel(("left", new ActivityNode("generic-left", MarkedVersion, [], [])), ("right", new ActivityNode("generic-right", MarkedVersion, [], [])))
            },
            variables = new[] { inner }
        }));

        var warning = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite" && item.Path == "root/variables/inner");
        Assert.Contains("'generic-left', 'generic-right'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reference_bound_to_a_variable_of_another_type_is_refused()
    {
        // "total" is an Int32; the writer reads and writes a Decimal.
        var error = Assert.Single(await Validate(Sequence("root", Writer("writer", "total"))), item => item.Type == "ValueFlow/VariableReference");
        Assert.Contains("reads and writes a Decimal, but variable 'total' is a Int32", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bound_references_to_declared_variables_are_accepted()
    {
        var errors = await Validate(Sequence("root", Writer("writer", "balance"), Reference("reader", ReaderVersion, "Amount", "balance")));

        Assert.DoesNotContain(errors, item => item.Type == "ValueFlow/VariableReference");
    }

    [Fact]
    public async Task Two_parallel_steps_writing_one_variable_through_references_warn()
    {
        var errors = await Validate(Sequence("root", Parallel(("left", Writer("write-left", "balance")), ("right", Writer("write-right", "balance")))));

        var warning = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Equal("$workflow/variables/balance", warning.Path);
        Assert.Contains("'write-left', 'write-right'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_only_reference_is_not_a_writer()
    {
        var errors = await Validate(Sequence("root", Parallel(
            ("left", Writer("write-left", "balance")),
            ("right", Reference("reader", ReaderVersion, "Amount", "balance")))));

        Assert.DoesNotContain(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
    }

    [Fact]
    public async Task A_marked_activity_counts_as_writing_every_visible_variable()
    {
        var errors = await Validate(Sequence("root", Parallel(
            ("left", new ActivityNode("generic", MarkedVersion, [], [])),
            ("right", Capture("capture", "total")))));

        var warning = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Contains("'generic', 'capture'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("may write any visible variable", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_that_writes_a_variable_and_captures_into_it_warns_that_the_capture_wins()
    {
        var node = Writer("writer", "balance") with { Outputs = [VariableOutput("Result", "balance")] };

        var warning = Assert.Single(await Validate(Sequence("root", node)), item => item.Type == "ValueFlow/StepWriteOverlapsCapture");
        Assert.Equal("writer/outputs/Result", warning.Path);
        Assert.Equal(ValidationSeverities.Warning, warning.Severity);
        Assert.Contains("the capture wins", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_script_that_writes_a_variable_literally_is_a_writer_of_that_variable()
    {
        // Spec 1231 FR-019, FR-028: the may-write set of a script body joins the concurrent-write rule.
        var errors = await Validate(Sequence("root", Parallel(
            ("left", Script("script", "variables.total = variables.total + 1;")),
            ("right", Capture("capture", "total")))));

        var warning = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Equal("$workflow/variables/total", warning.Path);
        Assert.Contains("'script', 'capture'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_script_that_only_reads_or_lets_a_scalar_escape_is_not_a_writer()
    {
        var errors = await Validate(Sequence("root", Parallel(
            ("left", Script("script", "const t = variables.total; return t + variables.balance;")),
            ("right", Capture("capture", "total")))));

        Assert.DoesNotContain(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
    }

    [Theory]
    [InlineData("variables[name] = 1;")]
    [InlineData("setVariable(name, 1);")]
    [InlineData("const v = variables; v.total = 1;")]
    public async Task A_script_with_a_computed_or_opaque_write_counts_as_writing_every_visible_variable(string script)
    {
        // FR-020a, User Story 1 / T029a: counted against every visible variable.
        var errors = await Validate(Sequence("root", Parallel(
            ("left", Script("script", script)),
            ("right", Capture("capture", "balance")))));

        var warning = Assert.Single(errors, item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Contains("'script', 'capture'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("may write any visible variable", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_script_activity_type_that_declares_the_marker_itself_writes_every_visible_variable()
    {
        // FR-028: its C# code may write any visible variable, whatever its body writes.
        var node = Script("script", "return 1;") with { ActivityVersionId = DeclaredScriptVersion };

        var warning = Assert.Single(await Validate(Sequence("root", Parallel(("left", node), ("right", Capture("capture", "total"))))),
            item => item.Type == "ValueFlow/ConcurrentWrite");
        Assert.Contains("may write any visible variable", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_script_that_writes_a_variable_it_also_captures_into_warns_that_the_capture_wins()
    {
        var node = Script("script", "setVariable('total', 2); return 1;") with { Outputs = [VariableOutput("result", "total")] };

        var warning = Assert.Single(await Validate(Sequence("root", node)), item => item.Type == "ValueFlow/StepWriteOverlapsCapture");
        Assert.Equal("script/outputs/result", warning.Path);
    }

    private static ActivityNode Script(string nodeId, string script) =>
        new(nodeId, ScriptVersion, [new ArgumentState("script", ArgumentValue.FromClr(script, "Literal"), null, null, null, null)], []);

    private static ActivityNode Writer(string nodeId, string variableKey, string scopeId = VariableReference.WorkflowScopeId) =>
        Reference(nodeId, WriterVersion, "Balance", variableKey, scopeId);

    private static ActivityNode Reference(string nodeId, string versionId, string inputKey, string variableKey, string scopeId = VariableReference.WorkflowScopeId) =>
        new(nodeId, versionId, [new ArgumentState(inputKey, VariableValue(variableKey, scopeId), null, null, null, null)], []);

    private static ActivityNode Capture(string nodeId, string variableKey) =>
        new(nodeId, "test.activity@1", [], [VariableOutput("result", variableKey)]);

    private static ArgumentValue VariableValue(string variableKey, string scopeId) =>
        new(JsonSerializer.SerializeToElement(new { referenceKey = variableKey, declaringScopeId = scopeId }), "Variable");

    private static ArgumentState VariableOutput(string outputKey, string variableKey) =>
        new(outputKey, VariableValue(variableKey, VariableReference.WorkflowScopeId), null, null, null, null);

    private static async Task<IReadOnlyCollection<ValidationError>> Validate(ActivityNode root)
    {
        var catalog = new StubActivityCatalog()
            .Add("test.activity@1").Add("elsa.sequence.structure@1").Add("elsa.parallel.structure@1")
            .Add(WriterVersion, [ReferenceInput("Balance", InputVariableAccess.ReadWrite)], [new OutputDefinition("Result", "Result", new TypeReference("Decimal"), null, "Result", null, false)])
            .Add(ReaderVersion, [ReferenceInput("Amount", InputVariableAccess.Read)])
            .Add(MarkedVersion, designFacets:
            [
                new ActivityDesignFacet(VariableAccessDesignFacet.Kind, VariableAccessDesignFacet.SchemaVersion,
                    JsonSerializer.SerializeToElement(new Dictionary<string, bool> { [VariableAccessDesignFacet.AccessesUndeclaredVariablesMember] = true }))
            ])
            .Add(ScriptVersion, designFacets:
            [
                new ActivityDesignFacet(WorkflowStateDesignFacet.Kind, WorkflowStateDesignFacet.SchemaVersion, JsonSerializer.SerializeToElement(new { declared = false })),
                // An engine script activity carries the type-level marker too, as Run JavaScript does; its body decides.
                new ActivityDesignFacet("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new { ports = Array.Empty<object>(), scriptInput = "script" })),
                new ActivityDesignFacet(VariableAccessDesignFacet.Kind, VariableAccessDesignFacet.SchemaVersion,
                    JsonSerializer.SerializeToElement(new Dictionary<string, bool> { [VariableAccessDesignFacet.AccessesUndeclaredVariablesMember] = true }))
            ])
            .Add(DeclaredScriptVersion, designFacets:
            [
                new ActivityDesignFacet("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new { ports = Array.Empty<object>(), scriptInput = "script" })),
                new ActivityDesignFacet(VariableAccessDesignFacet.Kind, VariableAccessDesignFacet.SchemaVersion,
                    JsonSerializer.SerializeToElement(new Dictionary<string, bool>
                    {
                        [VariableAccessDesignFacet.AccessesUndeclaredVariablesMember] = true,
                        [VariableAccessDesignFacet.DeclaredMember] = true
                    }))
            ]);
        using var services = ActivityDataSelectionTestSupport.Provider(catalog,
            configure: collection => new Elsa.Expressions.JavaScript.JavaScriptFeature().ConfigureServices(collection));
        // Resolved as the feature registers it, so these tests also prove that DI hands the validator the activity
        // catalog: without it every step-write check is skipped (FR-024, FR-028).
        using var scope = services.CreateScope();
        var validator = scope.ServiceProvider.GetServices<IDraftValidator>().OfType<ValueFlowValidator>().Single();
        var variables = new[]
        {
            new VariableDefinition("balance", "balance", new TypeReference("Decimal"), null, null),
            new VariableDefinition("total", "total", new TypeReference("Int32"), null, null)
        };
        return [.. await validator.Validate(new StubDraft(new WorkflowDefinitionState(variables, root, [], [], null)), CancellationToken.None)];
    }

    private static InputDefinition ReferenceInput(string key, string access) =>
        new(key, key, new TypeReference("Decimal"), null, key, null, IsNullable: false, IsRequired: true,
            UiHint: InputVariableAccess.UiHint, DefaultSyntax: InputVariableAccess.ExpressionType, VariableAccess: access);

    private static ActivityNode Sequence(string nodeId, params ActivityNode[] activities) =>
        Structured(nodeId, "elsa.sequence.structure", new { activities, variables = Array.Empty<object>() });

    private static ActivityNode Parallel(params (string Name, ActivityNode Activity)[] branches) =>
        Structured(
            "parallel",
            "elsa.parallel.structure",
            new { branches = branches.Select(branch => new { name = branch.Name, activity = branch.Activity }).ToArray(), threshold = (int?)null });

    private static ActivityNode Structured(string nodeId, string kind, object payload) =>
        new(
            nodeId,
            $"{kind}@1",
            [],
            [],
            new ActivityNodeStructure(kind, "1.0.0", JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft";
        public string WorkflowDefinitionId => "workflow";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
