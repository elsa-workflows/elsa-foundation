using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Spec 1222 at publish (T009, T033, T035, T049): each literal variable read is pinned as a <c>variables.</c> parameter
/// to its nearest declaring scope, a computed read sets the fallback marker, an unresolvable literal name refuses
/// publication, the script body's reads become its body read plan, and a read that moved to another declaration is a
/// warning against the published version.
/// </summary>
public sealed partial class WorkflowExecutableCompilerTests
{
    private static readonly ScriptVariableReadResolver ReadResolver = new([new FakeReadAnalyzer()]);

    private static readonly Elsa.Expressions.Core.Models.VariableDefinition Greeting =
        new("greeting-key", "greeting", new TypeReference("String"), null, null);

    [Fact]
    public async Task A_literal_read_is_pinned_to_its_declaring_scope_without_rewriting_the_source()
    {
        var executable = await CompileReadsAsync(Node("consumer", JavaScriptText("variables.greeting + '!'")), [Greeting]);

        var expression = executable.RootActivity.InputBindings["Text"].Expression!;
        Assert.Equal("variables.greeting + '!'", expression.Expression);
        var parameter = Assert.IsType<VariableExpressionParameterBinding>(Assert.Single(expression.Parameters).Value);
        Assert.Equal("variables.greeting", expression.Parameters.Keys.Single());
        Assert.Equal((VariableReference.WorkflowScopeId, "greeting-key", ExpressionParameterSurfaces.Variables), (parameter.DeclaringScopeNodeId, parameter.VariableKey, parameter.Surface));
        Assert.Null(expression.VariableReadFallback);
    }

    [Fact]
    public async Task A_container_declaration_shadows_the_workflow_variable()
    {
        var inner = new Elsa.Expressions.Core.Models.VariableDefinition("inner-greeting", "greeting", new TypeReference("String"), null, null);

        var executable = await CompileReadsAsync(SequenceNode("container", [Node("consumer", JavaScriptText("variables.greeting"))], [inner]), [Greeting]);

        var parameter = (VariableExpressionParameterBinding)executable.NodesById["consumer"].InputBindings["Text"].Expression!.Parameters["variables.greeting"];
        Assert.Equal(("container", "inner-greeting"), (parameter.DeclaringScopeNodeId, parameter.VariableKey));
    }

    [Fact]
    public async Task A_computed_read_sets_the_fallback_marker_with_the_visible_variables()
    {
        var executable = await CompileReadsAsync(Node("consumer", JavaScriptText("variables[key]")), [Greeting]);

        var fallback = executable.RootActivity.InputBindings["Text"].Expression!.VariableReadFallback!;
        var candidate = Assert.Single(fallback.Candidates);
        Assert.Equal(("greeting", VariableReference.WorkflowScopeId, "greeting-key"), (candidate.Name, candidate.DeclaringScopeNodeId, candidate.VariableKey));
    }

    [Fact]
    public async Task An_unresolvable_literal_name_refuses_publication()
    {
        // FR-012, FR-023.
        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            CompileReadsAsync(Node("consumer", JavaScriptText("variables.missing")), [Greeting]).AsTask());

        Assert.Contains(ScriptVariableReadDiagnostics.UnresolvedLiteralRead, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'missing'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authored_variables_parameters_are_derived_again_and_explicit_args_parameters_stay()
    {
        // Q6: an explicit args parameter bound to a variable is the author's own and is kept.
        var definition = new ExpressionDefinition(
            "JavaScript",
            "variables.greeting + args.greeting",
            new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding>
            {
                ["greeting"] = new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, "greeting-key"),
                ["variables.greeting"] = new VariableExpressionParameterBinding("stale-scope", "stale-key", ExpressionParameterSurfaces.Variables)
            },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingReadonlyV1);
        var input = new WorkflowArgumentState("Text", ArgumentValue.FromClr(Elsa.Serialization.Core.JsonConversionContract.SerializeToElement(definition), "JavaScript"), null, null, null, null);

        var executable = await CompileReadsAsync(Node("consumer", input), [Greeting]);

        var parameters = executable.RootActivity.InputBindings["Text"].Expression!.Parameters;
        Assert.Equal(["greeting", "variables.greeting"], parameters.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(VariableReference.WorkflowScopeId, ((VariableExpressionParameterBinding)parameters["variables.greeting"]).DeclaringScopeNodeId);
        Assert.False(((VariableExpressionParameterBinding)parameters["greeting"]).IsVariablesSurface);
    }

    [Fact]
    public async Task A_placed_reusable_activitys_authored_input_is_pinned_too()
    {
        // Boundary inputs compile before the node tree; their script reads get pinned all the same.
        var executable = await PlacedReusableCompiler(
            new WorkflowArgumentState("value", ArgumentValue.FromClr("variables.CallerResult + 1", "JavaScript"), null, null, null, null),
            readResolver: ReadResolver).CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        var parameter = (VariableExpressionParameterBinding)executable.RootActivity.InputBindings["value"].Expression!.Parameters["variables.CallerResult"];
        Assert.Equal((VariableReference.WorkflowScopeId, "caller-result"), (parameter.DeclaringScopeNodeId, parameter.VariableKey));
    }

    [Fact]
    public async Task A_script_body_gets_a_body_read_plan_apart_from_its_inputs()
    {
        // FR-004, FR-033, T049: body targets are never materialized into the input snapshot.
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(ScriptProbe), TypeAliasConvention.CanonicalAlias(typeof(ScriptProbe)));
        var probe = ClrActivityVersion("activity-script-probe", "Test.ScriptProbe", TypeAliasConvention.CanonicalAlias(typeof(ScriptProbe)));
        probe.Inputs = [new InputDefinition("script", "Script", new TypeReference("String"), null, "Script", null, IsNullable: true)];
        var node = new Elsa.Workflows.Design.Core.Models.ActivityNode(
            "script",
            "activity-script-probe",
            [new WorkflowArgumentState("script", ArgumentValue.FromClr("const g = variables.greeting; return g + variables[k];", "Literal"), null, null, null, null)],
            Outputs: []);
        var compiler = TestCompiler.Create(
            new FakeVersionStore(WorkflowVersion(node, variables: [Greeting])),
            new FakeActivityVersionStore([probe]),
            _activityStructureService,
            registry,
            scriptVariableReadResolver: ReadResolver);

        var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));

        var root = executable.RootActivity;
        Assert.Equal(RuntimeInputBindingSource.Literal, root.InputBindings["script"].Source);
        var plan = ScriptBodyReadPlan.Of(root)!;
        Assert.Equal("script", plan.InputKey);
        Assert.Equal(["variables.greeting"], plan.Parameters.Keys);
        Assert.Equal("greeting-key", plan.Parameters["variables.greeting"].VariableKey);
        Assert.Equal("greeting", Assert.Single(plan.VariableReadFallback!.Candidates).Name);
    }

    [Fact]
    public async Task A_read_that_now_resolves_to_another_declaration_is_a_warning_against_the_published_version()
    {
        // FR-024, FR-025: the published version keeps its own pin; the new one is published with a warning.
        var inner = new Elsa.Expressions.Core.Models.VariableDefinition("inner-greeting", "greeting", new TypeReference("String"), null, null);
        var published = await CompileReadsAsync(SequenceNode("container", [Node("consumer", JavaScriptText("variables.greeting"))]), [Greeting]);
        var candidate = await CompileReadsAsync(SequenceNode("container", [Node("consumer", JavaScriptText("variables.greeting"))], [inner]), [Greeting]);

        var warning = Assert.Single(ScriptReadShadowChanges.Find(published, candidate));

        Assert.StartsWith(ScriptVariableReadDiagnostics.ShadowChange, warning, StringComparison.Ordinal);
        Assert.Contains("'consumer'", warning, StringComparison.Ordinal);
        Assert.Empty(ScriptReadShadowChanges.Find(candidate, candidate));
        Assert.Equal(VariableReference.WorkflowScopeId,
            ((VariableExpressionParameterBinding)published.NodesById["consumer"].InputBindings["Text"].Expression!.Parameters["variables.greeting"]).DeclaringScopeNodeId);
    }

    [Fact]
    public async Task An_input_expression_that_assigns_a_variable_refuses_publication()
    {
        // Spec 1228 FR-006a, ADR 0083 binding-readonly-v1.
        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            CompileReadsAsync(Node("consumer", JavaScriptText("variables.greeting = 'x'")), [Greeting]).AsTask());

        Assert.Contains(ScriptVariableReadDiagnostics.InputExpressionWrite, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'consumer'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Text'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("variable 'greeting' at 'variables.greeting'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Input expressions only read", exception.Message, StringComparison.Ordinal);
    }

    private ValueTask<WorkflowExecutable> CompileReadsAsync(
        Elsa.Workflows.Design.Core.Models.ActivityNode root,
        IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> variables) =>
        TestCompiler.Create(
                new FakeVersionStore(WorkflowVersion(root, variables: variables)),
                new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create(),
                scriptVariableReadResolver: ReadResolver)
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));

    /// <summary>
    /// A stand-in for the JavaScript analyzer, which this project does not reference: <c>variables.X</c> is a literal
    /// read and <c>variables[</c> a computed one. The parser itself is tested with the JavaScript feature.
    /// </summary>
    private sealed class FakeReadAnalyzer : IScriptVariableReadAnalyzer
    {
        public string Language => "JavaScript";

        public ScriptVariableReadAnalysis Analyze(string source, bool scriptBody = false) =>
            new(
                Regex.Matches(source, @"variables\.([A-Za-z_][\w]*)").Select(match => match.Groups[1].Value).ToArray(),
                source.Contains("variables[", StringComparison.Ordinal))
            {
                Writes = Regex.Matches(source, @"(variables\.([A-Za-z_]\w*)[\w.]*)\s*=[^=]")
                    .Select(match => new ScriptVariableWrite(match.Groups[2].Value, match.Groups[1].Value) { Start = match.Index })
                    .ToArray(),
                Escapes = Regex.Matches(source, @"=\s*variables\.([A-Za-z_]\w*)\s*;").Select(match => match.Groups[1].Value).ToArray(),
                HasComputedWrite = Regex.IsMatch(source, @"variables\[[^\]]*\]\s*=[^=]")
            };
    }

    // An engine script activity, as Run JavaScript is: its type carries the free-name marker (spec 1231).
    [ActivityScriptOutcomes("script")]
    private sealed class ScriptProbe : Activity, Elsa.Activities.Runtime.Core.Contracts.IStepVariableSessionConsumer
    {
        [ActivityInput(Key = "script")]
        public string? Script { get; set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition<ActivityUnit>.Complete(ActivityUnit.Value));
    }
}
