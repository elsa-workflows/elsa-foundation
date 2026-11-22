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
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class StepWorkflowStateRunTests
{
    [Fact]
    public async Task Mixed_state_and_variable_changes_commit_with_pending_reads_and_result()
    {
        var (run, values) = await RunAsync("""
            variables.total = 15;
            workflowState.setOutput('total', {Amount: 20});
            workflowState.setCorrelationId('new');
            workflowState.setInstanceName('Ready');
            return [variables.total, workflowState.getOutput('total').Amount,
                workflowState.getCorrelationId(), workflowState.getInstanceName()];
            """);
        run.AssertWorkflowCompleted();
        Assert.Equal("[15,20,\"new\",\"Ready\"]", Result(run).GetRawText());
        Assert.Equal("15", run.WorkflowState!.RootVariableFrame!.Values["var-total"].InlineValue!.Value.GetRawText());
        Assert.Equal("new", run.WorkflowState.CorrelationId);
        Assert.Equal("Ready", run.WorkflowState.SystemMetadata[RuntimeMetadataKeys.InstanceName]);
        var output = Assert.Single(values, value => value.ValueId == "output:total");
        Assert.Equal("{\"Amount\":20}", output.InlineValue!.Value.GetRawText());
        Assert.Equal("Any", output.Type.Id);
        Assert.Equal(DurableValueStorage.Inline, output.Storage);
        Assert.Equal("new", Assert.Single(values, value => value.ValueId == "identity:correlationId").InlineValue!.Value.GetString());
        Assert.Contains("outputs", run.AssertCompleted("node-js").Metadata["workflowStateEffects"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("throw new Error('stop');")]
    [InlineData("try { workflowState.setOutput('', 1); } catch (e) {} return 1;")]
    [InlineData("try { workflowState.setOutput('other', undefined); } catch (e) {} return 1;")]
    [InlineData("try { workflowState.setCorrelationId(12); } catch (e) {} return 1;")]
    [InlineData("try { workflowState.requireExactIdentity(' '); } catch (e) {} return 1;")]
    [InlineData("return complete(1, 'Undeclared');")]
    public async Task Faults_and_caught_host_refusals_discard_every_successful_effect(string ending)
    {
        var (run, values) = await RunAsync("variables.total = 99; workflowState.setOutput('total', 88); " +
            "workflowState.setCorrelationId('new'); workflowState.setInstanceName('Changed'); " + ending);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-js").Status);
        Assert.Equal("10", run.WorkflowState!.RootVariableFrame!.Values["var-total"].InlineValue!.Value.GetRawText());
        Assert.Null(run.WorkflowState.CorrelationId);
        Assert.False(run.WorkflowState.SystemMetadata.ContainsKey(RuntimeMetadataKeys.InstanceName));
        Assert.DoesNotContain(values, value => value.ValueId == "output:total");
        Assert.False(run.State("node-js").Metadata.ContainsKey("workflowStateEffects"));
    }

    [Fact]
    public async Task Output_reads_are_copies_and_missing_null_and_undefined_returns_remain_distinct()
    {
        var (run, values) = await RunAsync("""
            const missing = workflowState.getOutput('missing') === undefined;
            const noResult = workflowState.setOutput('__proto__', {n: 1}) === undefined;
            const copy = workflowState.getOutput('__proto__'); copy.n = 999;
            workflowState.setOutput('nullable', null);
            workflowState.setOutput('nullable', 3);
            workflowState.setOutput('nullable', null);
            workflowState.setCorrelationId('x'); workflowState.setCorrelationId(null);
            workflowState.setInstanceName('x'); workflowState.setInstanceName(' ');
            return [missing, noResult, workflowState.getOutput('__proto__').n,
                workflowState.getOutput('nullable') === null, workflowState.getCorrelationId(), workflowState.getInstanceName()];
            """);
        run.AssertWorkflowCompleted();
        Assert.Equal("[true,true,1,true,null,null]", Result(run).GetRawText());
        Assert.Equal(JsonValueKind.Null, Assert.Single(values, value => value.ValueId == "output:nullable").InlineValue!.Value.ValueKind);
        Assert.Null(run.WorkflowState!.CorrelationId);
    }

    [Fact]
    public async Task Dead_literal_names_cannot_authorize_protected_computed_names()
    {
        var (run, values) = await RunAsync("if(false) workflowState.setOutput('Safe',0); variables.total = 99; workflowState.setOutput(workflowState.getOutput('Protected'),42); return 1;", protectedOutput: true);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-js").Status);
        Assert.Equal("10", run.WorkflowState!.RootVariableFrame!.Values["var-total"].InlineValue!.Value.GetRawText());
        Assert.DoesNotContain(values, value => value.ValueId == "output:Safe");
        Assert.DoesNotContain("Safe", JsonSerializer.Serialize(run.State("node-js").Fault), StringComparison.Ordinal);
    }

    private static JsonElement Result(WorkflowExecutionRun run) => run.AssertCompleted("node-js").Completion!.Result.InlineValue!.Value.GetProperty("value");

    private static async Task<(WorkflowExecutionRun, IReadOnlyCollection<DurableValueState>)> RunAsync(string script, bool protectedOutput = false)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesScriptingFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging(); services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
            }).Build("actexec-state");
        var type = new ValueTypeDescriptor("Int32");
        var policy = ValueProtectionPolicy.InstanceInline;
        var declaration = new RuntimeVariableDeclaration("var-total", "total", type, policy,
            new RuntimeInputBinding("var-total", type, policy, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(10), policy)));
        var node = new ExecutableNode("node-js", "authored-js", typeof(RunJavaScript).FullName!, "1.0.0", "test",
            JsonSerializer.SerializeToElement(new { }), inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["script"] = Literal("script", "String", JsonSerializer.SerializeToElement(script)),
                ["arguments"] = Literal("arguments", "JsonElement", JsonSerializer.SerializeToElement(new { }))
            }, metadata: new Dictionary<string, string> { ["workflowStateEffects"] = "{\"outputNames\":[\"Safe\"],\"conservative\":true}" });
        if (protectedOutput)
            await harness.Services.GetRequiredService<IDurableValueStateStore>().SaveAsync(new DurableValueState(
                "durable-output:Protected", harness.ExecutionId, "output:Protected", new RuntimeValueTypeDescriptor("Any", "Single", null),
                DurableValueLifecycle.Instance, DurableValueStorage.Inline, JsonSerializer.SerializeToElement("Safe"), null, null,
                DateTimeOffset.UnixEpoch, new Dictionary<string, string> { [RuntimeMetadataKeys.IsSensitive] = "true" }));
        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node, declaration),
            allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);
        var values = await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId, CancellationToken.None);
        return (run, values);
    }

    private static RuntimeInputBinding Literal(string key, string alias, JsonElement value, bool sensitive = false)
    {
        var type = new ValueTypeDescriptor(alias);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: sensitive);
        return new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, value, policy));
    }
}
