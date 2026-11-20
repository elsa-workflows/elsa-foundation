using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 1228 end to end, through the real engine, the real JavaScript evaluators and Run JavaScript: a read writes
/// nothing (US1, T010), a lossy number reaching a write is refused and a string passes (US2, T019), and an input
/// expression that changes a variable fails while a local copy does not (US1 acceptance 5–6, T010c).
/// </summary>
public sealed class ScriptValueModelRunTests
{
    private const string Order = """{"id":9007199254740993,"price":10.50}""";
    private const string Other = """{"untouched":1.0}""";

    [Fact]
    public async Task A_read_only_script_leaves_the_stored_order_and_an_unread_variable_unchanged()
    {
        var run = await RunAsync("return args.order.price * 2;", "({ order: variables.order })");

        run.AssertWorkflowCompleted();
        Assert.Equal(21, run.AssertCompleted("node-js").Completion!.Result.InlineValue!.Value.GetProperty("value").GetInt32());
        var frame = Assert.IsType<VariableFrameState>(run.WorkflowState?.RootVariableFrame);
        // SC-001: no stored value changes after a read; the stored text, large id and 10.50 included, is the same, and
        // so is the version: nothing was written.
        Assert.Equal(Order, frame.Values["var-order"].InlineValue!.Value.GetRawText());
        Assert.Equal(Other, frame.Values["var-other"].InlineValue!.Value.GetRawText());
        Assert.Equal(VariableVersion.Initial.Version, frame.VersionOf("var-order").Version);
        Assert.Equal(VariableVersion.Initial.Version, frame.VersionOf("var-other").Version);

        // Spec 1228 FR-015: the script computed with a lossy id it did not write, so the step records one warning; the
        // input expression passed the order through unchanged, so it raised none.
        var state = run.AssertCompleted("node-js");
        var warning = Assert.Single(state.Warnings!);
        Assert.Equal(JavaScriptValueDiagnostics.PrecisionLossRead, warning.Code);
        Assert.Equal("args.order.id", warning.Details!["paths"]);
        Assert.Null(state.InputSnapshot!.Warnings);
    }

    [Fact]
    public async Task An_input_expression_that_computes_with_a_lossy_number_records_its_warning_with_the_input()
    {
        var run = await RunAsync("return args.total;", "({ total: variables.order.price * 2 })");

        run.AssertWorkflowCompleted();
        var state = run.AssertCompleted("node-js");
        var warning = Assert.Single(state.InputSnapshot!.Warnings!);
        Assert.Equal(JavaScriptValueDiagnostics.PrecisionLossRead, warning.Code);
        Assert.Equal("arguments", warning.Details!["input"]);
        Assert.Equal("variables.order.id", warning.Details["paths"]);
        Assert.Null(state.Warnings);
    }

    [Fact]
    public async Task A_script_result_with_a_lossy_id_faults_the_step_and_nothing_is_written()
    {
        var run = await RunAsync("return { id: args.order.id };", "({ order: variables.order })");

        var state = run.State("node-js");
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Contains(JavaScriptValueDiagnostics.PrecisionLossRefused, state.Fault!.Message);
        Assert.Contains("9007199254740993", state.Fault.Message);
        Assert.Contains("9007199254740991", state.Fault.Message);
        Assert.Null(state.Completion);
    }

    [Fact]
    public async Task A_refused_lossy_result_from_a_sensitive_input_names_no_number()
    {
        // Spec 1229 FR-006: the fault keeps its value-free form; the stored digits are part of the protected value.
        await using var harness = NewHarness();
        var run = await RunAsync(harness, "return { id: args.order.id };", "({ order: variables.order })", Order, sensitive: true);

        var state = run.State("node-js");
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal(new JavaScriptPrecisionException("result", []).ValueFreeMessage, state.Fault!.Message);
        Assert.DoesNotContain("9007199254740993", state.Fault.Message);
    }

    [Fact]
    public async Task The_same_id_held_as_a_string_passes()
    {
        var run = await RunAsync("return { id: args.order.id };", "({ order: variables.order })", order: """{"id":"9007199254740993","price":10.50}""");

        run.AssertWorkflowCompleted();
        Assert.Equal("""{"id":"9007199254740993"}""", run.AssertCompleted("node-js").Completion!.Result.InlineValue!.Value.GetProperty("value").GetRawText());
    }

    [Fact]
    public async Task A_lossy_input_expression_result_is_refused()
    {
        var (run, incident) = await RunBlockedAsync("return 1;", "({ id: variables.order.id })");

        // An input that cannot be materialized blocks the node with one incident, before it starts.
        Assert.Equal(ActivityExecutionStatus.Scheduled, run.State("node-js").Status);
        Assert.Contains(JavaScriptValueDiagnostics.PrecisionLossRefused, incident);
    }

    [Fact]
    public async Task An_input_expression_that_changes_a_variable_through_an_alias_fails_and_writes_nothing()
    {
        var (run, incident) = await RunBlockedAsync("return 1;", "(() => { const o = variables.order; o.price = 1; return {}; })()");

        Assert.Equal(ActivityExecutionStatus.Scheduled, run.State("node-js").Status);
        Assert.Contains(JavaScriptValueDiagnostics.InputExpressionWrite, incident);
        Assert.Contains("variables.order.price", incident);
        var frame = Assert.IsType<VariableFrameState>(run.WorkflowState?.RootVariableFrame);
        Assert.Equal(Order, frame.Values["var-order"].InlineValue!.Value.GetRawText());
    }

    [Fact]
    public async Task An_input_expression_that_changes_a_local_copy_succeeds()
    {
        var run = await RunAsync("return args.order.price;", "(() => { const copy = { ...variables.order }; copy.price = 3; return { order: { price: copy.price } }; })()");

        run.AssertWorkflowCompleted();
        Assert.Equal(3, run.AssertCompleted("node-js").Completion!.Result.InlineValue!.Value.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task The_write_diagnostic_of_a_sensitive_variable_names_the_variable_but_not_the_path()
    {
        // FR-006a, spec 1229 FR-006: the diagnostic keeps its value-free form. A member path can be a key of the
        // protected value, so it is withheld; the declared name stays.
        var (_, incident) = await RunBlockedAsync("return 1;", "(() => { const o = variables.order; o.price = 1; return {}; })()", sensitive: true);

        Assert.Contains(JavaScriptValueDiagnostics.InputExpressionWrite, incident);
        Assert.Contains(new InputExpressionWriteException("order", "variables.order.price").ValueFreeMessage, incident);
        Assert.DoesNotContain("price", incident);
        Assert.DoesNotContain("9007199254740993", incident);
    }

    [Fact]
    public async Task A_write_to_a_name_computed_from_a_sensitive_value_does_not_name_it()
    {
        var (_, incident) = await RunBlockedAsync("return 1;", "(() => { variables['k' + variables.order.price] = 1; return {}; })()", sensitive: true);

        Assert.Contains(JavaScriptValueDiagnostics.InputExpressionWrite, incident);
        Assert.Contains(new InputExpressionWriteException("k10.5", "variables.k10", declared: false).ValueFreeMessage, incident);
        Assert.DoesNotContain("k10.5", incident);
    }

    [Fact]
    public async Task The_write_diagnostic_of_an_unprotected_variable_names_the_path()
    {
        var (_, incident) = await RunBlockedAsync("return 1;", "(() => { const o = variables.order; o.price = 1; return {}; })()");

        Assert.Contains(InputExpressionWriteException.Describe("order", "variables.order.price"), incident);
    }

    [Fact]
    public async Task An_input_expression_that_grows_a_read_value_past_the_json_limits_fails_with_the_write_diagnostic()
    {
        // FR-006a: the change is reported as a whole assignment, not as the JSON parser's depth error.
        var (_, incident) = await RunBlockedAsync("return 1;",
            "(() => { let o = variables.order; for (let i = 0; i < 200; i++) { o.n = {}; o = o.n; } return {}; })()");

        Assert.Contains(InputExpressionWriteException.Describe("order", "variables.order"), incident);
    }

    [Fact]
    public async Task Lossy_read_warnings_of_a_sensitive_value_name_roots_only()
    {
        // Spec 1229 FR-006: a member below a root can be a key of the protected value.
        await using (var harness = NewHarness())
        {
            var run = await RunAsync(harness, "return args.order.price * 2;", "({ order: variables.order })", Order, sensitive: true);
            run.AssertWorkflowCompleted();
            Assert.Equal("args", Assert.Single(run.AssertCompleted("node-js").Warnings!).Details!["paths"]);
        }

        await using (var harness = NewHarness())
        {
            var run = await RunAsync(harness, "return args.total;", "({ total: variables.order.price * 2 })", Order, sensitive: true);
            run.AssertWorkflowCompleted();
            var input = Assert.Single(run.AssertCompleted("node-js").InputSnapshot!.Warnings!);
            Assert.Equal("variables.order", input.Details!["paths"]);
            Assert.DoesNotContain(".id", input.Message);
        }
    }

    private static async Task<(WorkflowExecutionRun Run, string Incident)> RunBlockedAsync(string script, string argumentsExpression, bool sensitive = false)
    {
        await using var harness = NewHarness();
        var run = await RunAsync(harness, script, argumentsExpression, Order, sensitive);
        var incident = Assert.Single(await harness.Services.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.True(incident.IsBlocking);
        return (run, incident.Message + " " + string.Join(" ", incident.Metadata.Values));
    }

    private static async Task<WorkflowExecutionRun> RunAsync(string script, string argumentsExpression, string order = Order)
    {
        await using var harness = NewHarness();
        return await RunAsync(harness, script, argumentsExpression, order);
    }

    private static WorkflowExecutionHarness NewHarness() => WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesScriptingFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
            })
            .Build("actexec-js");

    private static async Task<WorkflowExecutionRun> RunAsync(WorkflowExecutionHarness harness, string script, string argumentsExpression, string order, bool sensitive = false)
    {
        var node = new ExecutableNode(
            executableNodeId: "node-js",
            authoredActivityId: "authored-js",
            activityType: typeof(RunJavaScript).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["script"] = Literal("script", "String", JsonSerializer.SerializeToElement(script)),
                ["arguments"] = Expression("arguments", argumentsExpression)
            },
            metadata: new Dictionary<string, string>());

        return await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(
                node,
                Declare("var-order", "order", order, sensitive),
                Declare("var-other", "other", Other)),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);
    }

    private static RuntimeVariableDeclaration Declare(string key, string name, string json, bool sensitive = false)
    {
        var type = new ValueTypeDescriptor("Object");
        var policy = sensitive
            ? new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true)
            : ValueProtectionPolicy.InstanceInline;
        return new RuntimeVariableDeclaration(key, name, type, policy,
            new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.Deserialize<JsonElement>(json), policy)));
    }

    private static RuntimeInputBinding Literal(string key, string alias, JsonElement value)
    {
        var type = new ValueTypeDescriptor(alias);
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(type, value, policy));
    }

    private static RuntimeInputBinding Expression(string key, string source)
    {
        var type = new ValueTypeDescriptor("JsonElement");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            key,
            type,
            policy,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "JavaScript",
                source,
                new RuntimeValueTypeDescriptor("alias", "JsonElement", null),
                parameters: new Dictionary<string, ExpressionParameterBinding>
                {
                    [ExpressionParameterSurfaces.VariablesKey("order")] = new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, "var-order", ExpressionParameterSurfaces.Variables)
                },
                options: JsonSerializer.SerializeToElement(new { }),
                capabilityProfile: ExpressionCapabilityProfiles.BindingPureV1));
    }
}
