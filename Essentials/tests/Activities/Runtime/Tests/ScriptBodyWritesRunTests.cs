using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 1231 User Story 1 (T025, T027, T029a): a Run JavaScript body writes variables, end to end through the real
/// engine, the step's variable session and its checkpoint commit. Whole assignments by every form (<c>variables.x = v</c>,
/// <c>variables['x'] = v</c>, <c>setVariable('x', v)</c>, compound and update forms) commit together with the step, each
/// with a write record of source "session change"; a fault saves nothing; a read sees the step's own write; a read or an
/// equal assignment writes nothing. Computed and opaque writes resolve at run time and fault on an undeclared name.
/// </summary>
public sealed class ScriptBodyWritesRunTests
{
    private const string Order = """{"id":9007199254740993,"price":10.50,"note":"a\u0022b","lines":[1,2]}""";

    [Fact]
    public async Task A_script_that_changes_two_variables_commits_both_with_the_step()
    {
        // Acceptance 1.
        var run = await RunAsync("variables.total = variables.total + 5; variables.count++; return 1;");

        run.AssertWorkflowCompleted();
        Assert.Equal("15", Stored(run, "var-total"));
        Assert.Equal("1", Stored(run, "var-count"));
        var writes = run.AssertCompleted("node-js").VariableWrites!;
        Assert.Equal(["var-count", "var-total"], writes.Select(write => write.VariableKey).Order(StringComparer.Ordinal));
        Assert.All(writes, write => Assert.Equal(VariableWriteSource.SessionChange, write.Writer.Source));
        Assert.All(writes, write => Assert.Equal("node-js", write.Writer.ExecutableNodeId));
    }

    [Fact]
    public async Task A_script_that_throws_after_assigning_saves_nothing()
    {
        // Acceptance 2.
        var run = await RunAsync("variables.total = 99; variables.count++; throw new Error('stop');");

        var state = run.State("node-js");
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Null(state.VariableWrites);
        Assert.Equal("10", Stored(run, "var-total"));
        Assert.Equal("0", Stored(run, "var-count"));
    }

    [Theory]
    [InlineData("const before = variables.count; variables.count = 5; return before * 100 + variables.count;")]
    [InlineData("const before = getVariable('count'); setVariable('count', 5); return before * 100 + getVariable('count');")]
    [InlineData("const before = variables['count']; variables['count'] = 5; return before * 100 + variables['count'];")]
    public async Task A_read_after_a_whole_assignment_sees_it(string script)
    {
        // Acceptance 3 (read-your-writes): a read before the write sees the committed 0, the read after it the 5.
        var run = await RunAsync(script);

        run.AssertWorkflowCompleted();
        Assert.Equal(5, Result(run).GetInt32());
        Assert.Equal("5", Stored(run, "var-count"));
    }

    [Fact]
    public async Task A_script_that_only_reads_leaves_text_and_version_unchanged()
    {
        // Acceptance 4 (C1).
        var before = await RunAsync("return 1;");
        var run = await RunAsync("const t = variables.total; const o = variables.order; return t + variables.count + o.lines.length + JSON.stringify(o).length;");

        run.AssertWorkflowCompleted();
        Assert.Null(run.AssertCompleted("node-js").VariableWrites);
        Assert.Equal(Stored(before, "var-total"), Stored(run, "var-total"));
        Assert.Equal(Version(before, "var-total"), Version(run, "var-total"));
        // An object value keeps its stored text exactly: the large integer, the trailing zero, the escape.
        Assert.Equal(Order, Stored(run, "var-order"));
        Assert.Equal(Version(before, "var-order"), Version(run, "var-order"));
    }

    [Theory]
    [InlineData("variables.count = 0; return 1;")]
    [InlineData("setVariable('count', 0); return 1;")]
    public async Task Assigning_the_value_a_variable_already_holds_writes_nothing(string script)
    {
        // Acceptance 5 (C4): no write, the version stays.
        var before = await RunAsync("return 1;");
        var run = await RunAsync(script);

        run.AssertWorkflowCompleted();
        Assert.Null(run.AssertCompleted("node-js").VariableWrites);
        Assert.Equal(Version(before, "var-count"), Version(run, "var-count"));
    }

    [Fact]
    public async Task SetVariable_assigns_a_variable_as_a_whole()
    {
        // FR-005 (T025): setVariable('x', v) is the same whole assignment as variables.x = v.
        var run = await RunAsync("setVariable('total', getVariable('total') + 1); return typeof setTotal;");

        run.AssertWorkflowCompleted();
        Assert.Equal("11", Stored(run, "var-total"));
        // FR-021: no generated set<Name>() accessor.
        Assert.Equal("undefined", Result(run).GetString());
        Assert.Equal(VariableWriteSource.SessionChange, Assert.Single(run.AssertCompleted("node-js").VariableWrites!).Writer.Source);
    }

    [Fact]
    public async Task SetVariable_without_a_name_faults_the_step()
    {
        var run = await RunAsync("setVariable(1, 2); return 1;");

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-js").Status);
        Assert.Equal("0", Stored(run, "var-count"));
    }

    [Theory]
    [InlineData("const k = 'to' + 'tal'; variables[k] = 7; return 1;")]
    [InlineData("const k = 'to' + 'tal'; setVariable(k, 7); return 1;")]
    [InlineData("const v = variables; v.total = 7; return 1;")]
    [InlineData("const s = setVariable; s('total', 7); return 1;")]
    [InlineData("eval(\"setVariable('total', 7)\"); return 1;")]
    [InlineData("const run = eval; run(\"setVariable('total', 7)\"); return 1;")]
    [InlineData("new Function(\"setVariable('total', 7)\")(); return 1;")]
    [InlineData("const engine = globalThis; engine.setVariable('total', 7); return 1;")]
    [InlineData("const engine = globalThis; engine.variables.total = 7; return 1;")]
    public async Task A_computed_or_opaque_write_resolves_at_run_time_and_commits_with_the_step(string script)
    {
        // FR-020a (T029a).
        var run = await RunAsync(script);

        run.AssertWorkflowCompleted();
        Assert.Equal("7", Stored(run, "var-total"));
        Assert.Equal(VariableWriteSource.SessionChange, Assert.Single(run.AssertCompleted("node-js").VariableWrites!).Writer.Source);
    }

    [Fact]
    public async Task A_plain_write_in_a_script_that_aliases_variables_still_works()
    {
        // FR-020a: the aliasing makes the node possibly write everything; its literal writes keep working.
        var run = await RunAsync("const v = variables; v.count = 4; variables.total = 1; return 1;");

        run.AssertWorkflowCompleted();
        Assert.Equal("4", Stored(run, "var-count"));
        Assert.Equal("1", Stored(run, "var-total"));
    }

    [Theory]
    [InlineData("const k = 'no' + 'pe'; variables[k] = 1; variables.total = 2; return 1;")]
    [InlineData("const k = 'no' + 'pe'; setVariable(k, 1); variables.total = 2; return 1;")]
    public async Task A_computed_write_to_an_undeclared_name_faults_the_step_before_its_commit(string script)
    {
        // FR-020a, FR-014: nothing is created and nothing else the step changed is saved.
        var run = await RunAsync(script);

        var state = run.State("node-js");
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Null(state.VariableWrites);
        Assert.Equal("10", Stored(run, "var-total"));
        Assert.Contains("'nope'", state.Fault!.Message, StringComparison.Ordinal);
    }

    private static JsonElement Result(WorkflowExecutionRun run) =>
        run.AssertCompleted("node-js").Completion!.Result.InlineValue!.Value.GetProperty("value");

    private static string Stored(WorkflowExecutionRun run, string key) =>
        Assert.IsType<VariableFrameState>(run.WorkflowState?.RootVariableFrame).Values[key].InlineValue!.Value.GetRawText();

    private static long? Version(WorkflowExecutionRun run, string key) =>
        Assert.IsType<VariableFrameState>(run.WorkflowState?.RootVariableFrame).Versions is { } versions && versions.TryGetValue(key, out var version)
            ? version.Version
            : null;

    private static async Task<WorkflowExecutionRun> RunAsync(string script)
    {
        await using var harness = WorkflowExecutionHarness.Create()
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
            .Build("actexec-js-writes");
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
            metadata: new Dictionary<string, string>());

        return await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(node,
                Declare("var-total", "total", "Int32", "10"), Declare("var-count", "count", "Int32", "0"), Declare("var-order", "order", "Any", Order)),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);
    }

    private static RuntimeVariableDeclaration Declare(string key, string name, string alias, string json)
    {
        var type = new ValueTypeDescriptor(alias);
        var policy = ValueProtectionPolicy.InstanceInline;
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
