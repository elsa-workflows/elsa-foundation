using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Design.Validations.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class WorkflowExecutableCompilerTests
{
    internal static ScriptVariableReadResolver StateReadResolver()
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
    [InlineData("return { globalThis: 1, Function: 2, eval: 3, Object: 4, Reflect: 5 };")]
    [InlineData("return args.Function;")]
    [InlineData("const globalThis={total:1}; return globalThis.total;")]
    [InlineData("const Function=x=>x; return Function(1);")]
    [InlineData("const Reflect={get:x=>x}; return Reflect.get(1);")]
    [InlineData("const Object={total:1}; return Object;")]
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
    [InlineData("return { globalThis };")]
    [InlineData("const constructor = Function; return constructor('return 1')();")]
    public void Real_references_preserve_the_independent_capability_signal(string script) =>
        Assert.True(StateReadResolver().Analyze("JavaScript", script, scriptBody: true)!.WorkflowState.AccessesState);

    [Theory]
    [InlineData("args.workflowState")]
    [InlineData("({workflowState: 1})")]
    [InlineData("args.Function")]
    [InlineData("args.eval")]
    [InlineData("args.globalThis")]
    [InlineData("args.Object")]
    [InlineData("args.Reflect")]
    public async Task Plain_property_names_remain_valid_in_read_only_input_expressions(string source) =>
        await CompileReadsAsync(Node("consumer", JavaScriptText(source)), [Greeting], StateReadResolver());

    // #5686: an input expression runs without the workflow-state capability (binding-readonly-v1), so a runtime key, eval
    // or `this` cannot reach it. Only a reference to the capability's own name is refused at publication.
    [Theory]
    [InlineData("const parts = ['a', 'b']; const index = variables.greeting.length % 2; return parts[index];")]
    [InlineData("const result = {}; const key = variables.greeting; result[key] = 1; return JSON.stringify(result);")]
    [InlineData("const copy = { a: 1, b: 2 }; const key = variables.greeting; delete copy[key]; return JSON.stringify(copy);")]
    [InlineData("const list = [1, 2]; list[variables.greeting.length] = 3; return list.join(',');")]
    [InlineData("return Object.keys({ a: 1 }).join(',');")]
    public async Task Runtime_keys_remain_valid_in_read_only_input_expressions(string source) =>
        await CompileReadsAsync(Node("consumer", JavaScriptText(source)), [Greeting], StateReadResolver());

    [Theory]
    [InlineData("workflowState.getCorrelationId()")]
    [InlineData("workflowState['getCorrelationId']()")]
    [InlineData("const s = workflowState; return s.getCorrelationId();")]
    public async Task Input_expressions_naming_the_workflow_state_capability_refuse_publication(string source)
    {
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            CompileReadsAsync(Node("consumer", JavaScriptText(source)), [Greeting], StateReadResolver()).AsTask());
        Assert.Contains("VF-STATE-002", error.Message, StringComparison.Ordinal);
    }

    internal async Task<Elsa.Workflows.Runtime.Core.Models.ExecutableNode> NativeStateScriptNodeAsync() =>
        (await CompileBodyAsync("return 1;", typeof(NativeStateScriptProbe), StateReadResolver())).RootActivity;

    internal async Task<Elsa.Workflows.Runtime.Core.Models.ExecutableNode> EngineStateScriptNodeAsync() =>
        (await CompileBodyAsync("return 1;", typeof(EngineStateScriptProbe), StateReadResolver())).RootActivity;

    internal static Elsa.Serialization.Core.IWellKnownTypeRegistry StateScriptTypes()
    {
        var registry = TestWellKnownTypeRegistry.Create();
        foreach (var type in new[] { typeof(EngineStateScriptProbe), typeof(NativeStateScriptProbe) })
            registry.RegisterType(type, Elsa.Primitives.Models.TypeAliasConvention.CanonicalAlias(type));
        return registry;
    }

    [Elsa.Activities.Runtime.Core.Attributes.ActivityScriptOutcomes("script")]
    private sealed class EngineStateScriptProbe : Elsa.Activities.Runtime.Core.Models.Activity, Elsa.Activities.Runtime.Core.Contracts.IStepWorkflowStateConsumer
    {
        [Elsa.Activities.Runtime.Core.Attributes.ActivityInput(Key = "script")]
        public string? Script { get; set; }
        protected override ValueTask<Elsa.Activities.Runtime.Core.Models.ActivityTransition<Elsa.Activities.Runtime.Core.Models.ActivityUnit>> ExecuteAsync(
            Elsa.Activities.Runtime.Core.Models.ActivityExecutionContext context) => ValueTask.FromResult(
                Elsa.Activities.Runtime.Core.Models.ActivityTransition<Elsa.Activities.Runtime.Core.Models.ActivityUnit>.Complete(Elsa.Activities.Runtime.Core.Models.ActivityUnit.Value));
    }

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
