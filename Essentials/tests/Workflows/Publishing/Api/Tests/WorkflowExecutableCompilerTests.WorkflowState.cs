using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Design.Validations.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class WorkflowExecutableCompilerTests
{
    private static ScriptVariableReadResolver StateReadResolver()
    {
        var services = new ServiceCollection();
        new Elsa.Expressions.JavaScript.JavaScriptFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        return new ScriptVariableReadResolver(provider.GetServices<IScriptVariableReadAnalyzer>());
    }
    [Theory]
    [InlineData("workflowState.setOutput('',1);")]
    [InlineData("workflowState.setOutput(42,1);")]
    [InlineData("function local(workflowState) {} workflowState.setCorrelationId(42);")]
    [InlineData("workflowState.getOutput(null);")]
    [InlineData("workflowState.setOutput('x');")]
    [InlineData("workflowState.setOutput('x', void 0);")]
    [InlineData("workflowState.setCorrelationId(42);")]
    [InlineData("workflowState.setInstanceName(false);")]
    [InlineData("workflowState.getInstanceName(1);")]
    [InlineData("function f(x = workflowState.setCorrelationId(42)) { var workflowState; } f();")]
    [InlineData("switch (workflowState.setCorrelationId(42)) { case 1: const workflowState = {}; }")]
    public async Task Provably_invalid_workflow_state_calls_refuse_publication_without_values(string script)
    {
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => CompileBodyAsync(script, readResolver: StateReadResolver()).AsTask());
        Assert.Contains("VF-STATE-001", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(script, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("const key='x'; workflowState.setOutput(key,42);")]
    [InlineData("const name='new'; workflowState.setCorrelationId(name);")]
    [InlineData("function f(workflowState) { workflowState.setOutput('',1); } f({setOutput(){}});")]
    [InlineData("switch (1) { case 1: const workflowState = {setOutput(){}}; workflowState.setOutput('',1); }")]
    [InlineData("class Local { static { const workflowState = {setOutput(){}}; workflowState.setOutput('',1); } }")]
    public async Task Computed_calls_and_local_lookalikes_keep_runtime_validation(string script) => await CompileBodyAsync(script, readResolver: StateReadResolver());

    [Theory]
    [InlineData("return { workflowState: 1 };")]
    [InlineData("return args.workflowState;")]
    [InlineData("switch (1) { case 1: const workflowState={setOutput(){}}; workflowState.setOutput('',1); }")]
    [InlineData("class Local { static { var workflowState={setOutput(){}}; workflowState.setOutput('',1); } }")]
    public void Local_lookalikes_and_plain_property_keys_have_no_capability_effects(string script)
    {
        var analysis = StateReadResolver().Analyze("JavaScript", script, scriptBody: true);
        Assert.NotNull(analysis);
        Assert.False(analysis.WorkflowState.AccessesState);
        Assert.False(analysis.WorkflowState.HasInvalidLiteralCall);
    }

    [Theory]
    [InlineData("function f(x = workflowState.setCorrelationId('new')) { var workflowState; } f();")]
    [InlineData("switch (workflowState.setCorrelationId('new')) { case 1: const workflowState = {}; }")]
    [InlineData("return { workflowState };")]
    public void Real_references_preserve_the_independent_capability_signal(string script) =>
        Assert.True(StateReadResolver().Analyze("JavaScript", script, scriptBody: true)!.WorkflowState.AccessesState);

    [Theory]
    [InlineData("args.workflowState")]
    [InlineData("({workflowState: 1})")]
    public async Task Plain_property_names_remain_valid_in_read_only_input_expressions(string source) =>
        await CompileReadsAsync(Node("consumer", JavaScriptText(source)), [Greeting], StateReadResolver());

    internal async Task<Elsa.Workflows.Runtime.Core.Models.ExecutableNode> NativeStateScriptNodeAsync() =>
        (await CompileBodyAsync("return 1;", typeof(NativeStateScriptProbe), StateReadResolver())).RootActivity;

    [Fact]
    public async Task Explicit_native_capability_remains_conservative_beside_a_read_only_script()
    {
        var node = await NativeStateScriptNodeAsync();
        Assert.True(node.ActivityContract!.AccessesWorkflowState);
        Assert.Equal("true", node.Metadata["accessesWorkflowState"]);
        Assert.Contains("\"conservative\":true", node.Metadata["workflowStateEffects"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_native_capability_refuses_structural_publication_despite_read_only_script()
    {
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => CompileBodyAsync("return 1;",
            typeof(StructuralStateScriptProbe), StateReadResolver(), SequenceNode("shape", []).Structure).AsTask());
        Assert.Contains("VF-STATE-002", error.Message, StringComparison.Ordinal);
    }

    [Elsa.Activities.Runtime.Core.Attributes.AccessesWorkflowState]
    [Elsa.Activities.Runtime.Core.Attributes.ActivityScriptOutcomes("script")]
    private class NativeStateScriptProbe : Elsa.Activities.Runtime.Core.Models.Activity
    {
        [Elsa.Activities.Runtime.Core.Attributes.ActivityInput(Key = "script")]
        public string? Script { get; set; }
        protected override ValueTask<Elsa.Activities.Runtime.Core.Models.ActivityTransition<Elsa.Activities.Runtime.Core.Models.ActivityUnit>> ExecuteAsync(
            Elsa.Activities.Runtime.Core.Models.ActivityExecutionContext context)
        {
            context.WorkflowState.CorrelationId = "native";
            return ValueTask.FromResult(Elsa.Activities.Runtime.Core.Models.ActivityTransition<Elsa.Activities.Runtime.Core.Models.ActivityUnit>.Complete(
                Elsa.Activities.Runtime.Core.Models.ActivityUnit.Value));
        }
    }

    [Elsa.Activities.Runtime.Core.Attributes.ActivityStructure("elsa.sequence.structure", "1.0.0")]
    private sealed class StructuralStateScriptProbe : NativeStateScriptProbe;
}
