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
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// A Run JavaScript body reads and writes workflow variables over the step's variable session, end to end through the
/// real engine and evaluators: spec 1222 Phase 8 (T045, T047, T050: the <c>variables</c> root and <c>getVariable</c>,
/// every read recorded in the session reads with its protection), spec 1228 User Story 3 (T026–T028: changes written
/// with the step, member changes keep the stored text, the ADR 0090 C2 warning) and User Story 7 (T040–T041: the result
/// is as protected as what the body read).
/// </summary>
public sealed class ScriptBodyVariablesRunTests
{
    private const string Order = """{"id":9007199254740993,"price":10.50,"items":[1,2]}""";
    private const string Other = """{"untouched":1.0}""";

    // ---- spec 1222 T045: the body read surface ----

    [Theory]
    [InlineData("return variables.order.price;")]
    [InlineData("return variables['order'].price;")]
    [InlineData("return getVariable('order').price;")]
    [InlineData("const name = 'or' + 'der'; return getVariable(name).price;")]
    [InlineData("const name = 'or' + 'der'; return variables[name].price;")]
    public async Task Every_read_path_reads_the_variable_and_records_it_in_the_session_reads(string script)
    {
        var run = await RunAsync(script);

        run.AssertWorkflowCompleted();
        var state = run.AssertCompleted("node-js");
        Assert.Equal(10.5, state.Completion!.Result.InlineValue!.Value.GetProperty("value").GetDouble());
        var read = Assert.Single(state.SessionReads!);
        Assert.Equal("var-order", read.Entry.VariableKey);
        Assert.Equal(ValueProtectionPolicy.InstanceInline, read.Policy);
        // A read writes nothing (spec 1228 FR-002).
        Assert.Null(state.VariableWrites);
    }

    [Fact]
    public async Task A_literal_read_reads_its_pinned_target()
    {
        var plan = new ScriptBodyReadPlan("script", "JavaScript", new Dictionary<string, VariableExpressionParameterBinding>
        {
            [ExpressionParameterSurfaces.VariablesKey("order")] =
                new(VariableReference.WorkflowScopeId, "var-order", ExpressionParameterSurfaces.Variables)
        });

        var run = await RunAsync("return variables.order.price;", plan: plan);

        run.AssertWorkflowCompleted();
        Assert.Equal("var-order", Assert.Single(run.AssertCompleted("node-js").SessionReads!).Entry.VariableKey);
    }

    [Fact]
    public async Task Every_read_path_returns_the_same_value_and_no_generated_accessor_exists()
    {
        // FR-034, FR-036.
        var run = await RunAsync(
            "return { same: variables.order === getVariable('order') && variables['order'] === variables.order, accessor: typeof getOrder };");

        run.AssertWorkflowCompleted();
        var value = run.AssertCompleted("node-js").Completion!.Result.InlineValue!.Value.GetProperty("value");
        Assert.True(value.GetProperty("same").GetBoolean());
        Assert.Equal("undefined", value.GetProperty("accessor").GetString());
    }

    [Fact]
    public async Task A_name_no_visible_variable_has_reads_undefined_with_a_warning()
    {
        // FR-011.
        var run = await RunAsync("const name = 'nope'; return typeof variables[name];");

        run.AssertWorkflowCompleted();
        var state = run.AssertCompleted("node-js");
        Assert.Equal("undefined", state.Completion!.Result.InlineValue!.Value.GetProperty("value").GetString());
        var warning = Assert.Single(state.Warnings!);
        Assert.Equal("VF-READ-001", warning.Code);
        Assert.Equal("nope", warning.Details!["variable"]);
    }

    [Fact]
    public async Task A_write_to_a_name_no_visible_variable_has_faults_the_step()
    {
        var run = await RunAsync("variables.nope = 1; return 1;");

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-js").Status);
        AssertStoredOrder(run, Order);
    }

    // ---- spec 1228 User Story 3: changes are written with the step ----

    [Fact]
    public async Task A_member_change_keeps_the_other_members_stored_text_and_warns()
    {
        // Acceptance 1 and 6: only price changes; the large id and the array keep their stored text.
        var run = await RunAsync("variables.order.price = 20; return 1;");

        run.AssertWorkflowCompleted();
        AssertStoredOrder(run, """{"id":9007199254740993,"price":20,"items":[1,2]}""");
        var warning = Assert.Single(run.AssertCompleted("node-js").Warnings!);
        Assert.Equal(JavaScriptValueDiagnostics.ChangeWithoutAssignment, warning.Code);
        Assert.Equal("order", warning.Details!["variable"]);
        Assert.Equal("variables.order.price", warning.Details["paths"]);
    }

    [Fact]
    public async Task A_whole_assignment_replaces_the_value_without_a_warning()
    {
        // Acceptance 2.
        var run = await RunAsync("variables.order = { total: 20 }; return 1;");

        run.AssertWorkflowCompleted();
        AssertStoredOrder(run, """{"total":20}""");
        Assert.Null(run.AssertCompleted("node-js").Warnings);
    }

    [Fact]
    public async Task A_changed_array_is_written_whole()
    {
        // Acceptance 3.
        var run = await RunAsync("variables.order.items.push(3); return 1;");

        run.AssertWorkflowCompleted();
        AssertStoredOrder(run, """{"id":9007199254740993,"price":10.50,"items":[1,2,3]}""");
    }

    [Fact]
    public async Task A_member_set_to_what_javascript_read_is_unchanged_and_a_string_forces_the_write()
    {
        // Acceptance 4 (ADR 0090 C2): 9007199254740992 is the JavaScript value of the stored 9007199254740993.
        var unchanged = await RunAsync("variables.order.id = 9007199254740992; return 1;");
        unchanged.AssertWorkflowCompleted();
        AssertStoredOrder(unchanged, Order);
        Assert.Null(unchanged.AssertCompleted("node-js").VariableWrites);

        var forced = await RunAsync("variables.order.id = '9007199254740993'; return 1;");
        forced.AssertWorkflowCompleted();
        AssertStoredOrder(forced, """{"id":"9007199254740993","price":10.50,"items":[1,2]}""");
    }

    [Fact]
    public async Task A_whole_assignment_that_loses_precision_is_refused_and_nothing_is_written()
    {
        // Acceptance 5: an explicit whole assignment is a write, and the precision policy applies.
        var run = await RunAsync("variables.order = { id: variables.order.id }; return 1;");

        var state = run.State("node-js");
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Contains(JavaScriptValueDiagnostics.PrecisionLossRefused, state.Fault!.Message);
        Assert.Contains("variables.order", state.Fault.Message);
        AssertStoredOrder(run, Order);
    }

    [Fact]
    public async Task A_change_through_any_read_path_is_written()
    {
        var run = await RunAsync("getVariable('order').price = 30; return 1;");

        run.AssertWorkflowCompleted();
        AssertStoredOrder(run, """{"id":9007199254740993,"price":30,"items":[1,2]}""");
    }

    [Fact]
    public async Task A_read_after_a_write_sees_it()
    {
        // Read-your-writes within the evaluation: the same JavaScript value.
        var run = await RunAsync("variables.order.price = 40; return variables.order.price;");

        run.AssertWorkflowCompleted();
        Assert.Equal(40, run.AssertCompleted("node-js").Completion!.Result.InlineValue!.Value.GetProperty("value").GetInt32());
    }

    // ---- spec 1228 User Story 7: the result is as protected as what the body read ----

    [Fact]
    public async Task A_result_built_from_a_sensitive_read_is_sensitive()
    {
        // Acceptance 1.
        var run = await RunAsync("return variables.order.price;", sensitive: true);

        run.AssertWorkflowCompleted();
        var state = run.AssertCompleted("node-js");
        Assert.True(state.Completion!.Result.Policy.IsSensitive);
        Assert.True(Assert.Single(state.SessionReads!).Policy.IsSensitive);
    }

    [Fact]
    public async Task A_result_with_no_protected_read_gets_no_protection()
    {
        // Acceptance 2.
        var run = await RunAsync("return variables.other.untouched;");

        run.AssertWorkflowCompleted();
        Assert.False(run.AssertCompleted("node-js").Completion!.Result.Policy.IsSensitive);
    }

    [Fact]
    public async Task Diagnostics_of_a_sensitive_read_name_no_member_and_no_number()
    {
        // Spec 1229 FR-006: the keys and numbers of a protected value stay out of the warning and the refusal.
        var warned = await RunAsync("variables.other.untouched = 2; const next = variables.order.id + 1; return 1;", sensitive: true);
        warned.AssertWorkflowCompleted();
        var warnings = warned.AssertCompleted("node-js").Warnings!;
        Assert.All(warnings, warning =>
        {
            Assert.DoesNotContain(".id", warning.Message);
            Assert.DoesNotContain(".untouched", warning.Message);
        });
        Assert.Contains(warnings, warning => warning.Code == JavaScriptValueDiagnostics.ChangeWithoutAssignment);
        Assert.Contains(warnings, warning => warning.Code == JavaScriptValueDiagnostics.PrecisionLossRead);

        var refused = await RunAsync("return { id: variables.order.id };", sensitive: true);
        var fault = refused.State("node-js").Fault!.Message;
        Assert.Equal(new JavaScriptPrecisionException("result", []).ValueFreeMessage, fault);
    }

    [Fact]
    public async Task The_output_evidence_of_a_sensitive_read_is_flagged_and_not_captured()
    {
        // The inspection record of the output takes the read protection too, not only the stored result.
        var (run, output) = await RunWithOutputSnapshotAsync("return variables.order.price;", sensitive: true);

        run.AssertWorkflowCompleted();
        Assert.True(output.IsSensitive);
        Assert.Null(output.Payload);
    }

    [Fact]
    public async Task A_write_to_a_computed_name_does_not_name_it_when_a_protected_value_was_read()
    {
        var run = await RunAsync("variables['k' + variables.order.price] = 1; return 1;", sensitive: true);

        var fault = run.State("node-js").Fault!.Message;
        Assert.DoesNotContain("k10.5", fault);
        // The host's own refusal passes through: it says what went wrong and names no value.
        Assert.Contains("not declared in any scope", fault);
        Assert.Contains("name is withheld", fault);
    }

    [Theory]
    [InlineData("throw new Error('ssn ' + variables.order.price);")]
    [InlineData("return JSON.parse('{' + variables.order.price);")]
    public async Task A_script_error_after_a_sensitive_read_does_not_quote_it(string script)
    {
        // Spec 1229 FR-006: the error's text can carry what the script read.
        var run = await RunAsync(script, sensitive: true);

        var fault = run.State("node-js").Fault!.Message;
        Assert.DoesNotContain("10.5", fault);
        Assert.Contains("withheld", fault);
    }

    // ---- harness ----

    private static async Task<(WorkflowExecutionRun Run, ActivityExecutionInspectionValueSnapshot Output)> RunWithOutputSnapshotAsync(string script, bool sensitive)
    {
        await using var harness = NewHarness();
        var run = await RunAsync(harness, script, sensitive, plan: null);
        var projection = await harness.Services.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IActivityExecutionInspectionStore>()
            .FindAsync(harness.ExecutionId, "actexec-js-body");
        return (run, Assert.Single(projection!.ValueSnapshots, snapshot => snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityOutput));
    }

    private static void AssertStoredOrder(WorkflowExecutionRun run, string expected)
    {
        var frame = Assert.IsType<VariableFrameState>(run.WorkflowState?.RootVariableFrame);
        Assert.Equal(expected, frame.Values["var-order"].InlineValue!.Value.GetRawText());
        Assert.Equal(Other, frame.Values["var-other"].InlineValue!.Value.GetRawText());
    }

    private static async Task<WorkflowExecutionRun> RunAsync(string script, bool sensitive = false, ScriptBodyReadPlan? plan = null)
    {
        await using var harness = NewHarness();
        return await RunAsync(harness, script, sensitive, plan);
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
            .Build("actexec-js-body");

    private static async Task<WorkflowExecutionRun> RunAsync(WorkflowExecutionHarness harness, string script, bool sensitive, ScriptBodyReadPlan? plan)
    {
        var metadata = new Dictionary<string, string>();
        if (plan is not null)
            metadata[RuntimeMetadataKeys.ScriptBodyReadPlan] = plan.Serialize();
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
                ["arguments"] = Literal("arguments", "JsonElement", JsonSerializer.SerializeToElement(new { }))
            },
            metadata: metadata);

        return await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(node, Declare("var-order", "order", Order, sensitive), Declare("var-other", "other", Other)),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);
    }

    private static RuntimeVariableDeclaration Declare(string key, string name, string json, bool sensitive = false)
    {
        var type = new ValueTypeDescriptor("Any");
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
}
