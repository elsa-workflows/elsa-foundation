using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

public sealed class ScriptWorkflowStateTests
{
    [Fact]
    public async Task Default_parameter_reads_the_outer_capability_before_body_var_scope()
    {
        var state = new State();
        await Evaluate("function f(x = workflowState.setCorrelationId('new')) { var workflowState; } f(); return 1;", state);
        Assert.Equal("new", state.CorrelationId);
    }

    [Fact]
    public async Task Top_level_var_is_local_to_the_body_wrapper()
    {
        var state = new State();
        var result = await Evaluate("var workflowState; return typeof workflowState;", state);
        Assert.Equal("undefined", result!.Value.GetString());
        Assert.Null(state.CorrelationId);
    }

    [Fact]
    public async Task Calls_preserve_pending_identity_json_copies_and_undefined_setter_results()
    {
        var state = new State();
        var result = await Evaluate("""
            const missing = workflowState.getOutput('missing') === undefined;
            const setter = workflowState.setOutput('order', {Amount: 1}) === undefined;
            const copy = workflowState.getOutput('order'); copy.Amount = 9;
            workflowState.setOutput('null', null);
            workflowState.setCorrelationId('new'); workflowState.setInstanceName(null);
            return [missing, setter, workflowState.getOutput('order').Amount, workflowState.getOutput('null') === null,
                workflowState.getCorrelationId(), workflowState.getInstanceName(), Object.isExtensible(workflowState)];
            """, state);
        Assert.Equal("[true,true,1,true,\"new\",null,false]", result!.Value.GetRawText());
    }

    [Theory]
    [InlineData("workflowState.setOutput('', 1)")]
    [InlineData("workflowState.setOutput('x', undefined)")]
    [InlineData("workflowState.setOutput('x', 1, 2)")]
    [InlineData("workflowState.getOutput()")]
    [InlineData("workflowState.getCorrelationId(1)")]
    [InlineData("workflowState.setCorrelationId(3)")]
    [InlineData("workflowState.setInstanceName(undefined)")]
    [InlineData("workflowState.requireExactIdentity(' ')")]
    [InlineData("workflowState.setOutput('x', { toJSON() { throw new Error('sentinel'); } })")]
    public async Task Invalid_calls_remain_host_refusals_when_script_catches_them(string call)
    {
        var state = new State();
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Evaluate($"try {{ {call}; }} catch (e) {{ }} return 1;", state));
        Assert.Contains("VF-STATE-", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identity_guard_and_json_hooks_evaluate_once_in_source_order()
    {
        var result = await Evaluate("""
            let calls = 0;
            const value = () => { calls++; return 'new'; };
            workflowState.setCorrelationId(workflowState.requireExactIdentity(value()));
            workflowState.setOutput('x', { toJSON() { calls++; return 42; } });
            return [calls, workflowState.getCorrelationId(), workflowState.getOutput('x')];
            """, new State());
        Assert.Equal("[2,\"new\",42]", result!.Value.GetRawText());
    }

    [Fact]
    public async Task Arguments_do_not_grant_a_capability_when_the_request_has_none()
    {
        var result = await Evaluate("return typeof workflowState;", null);
        Assert.Equal("undefined", result!.Value.GetString());
    }

    [Fact]
    public async Task Allowed_lossy_output_writes_are_not_reported_as_read_only_losses()
    {
        var services = new ServiceCollection();
        new JintFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Singleton<IJavaScriptPrecisionPolicy>(new AllowLoss()));
        await using var provider = services.BuildServiceProvider();
        var state = new State();
        state.SetOutput("Big", JsonDocument.Parse("9007199254740993").RootElement);
        var warnings = new Warnings();
        await provider.GetRequiredService<IJavaScriptScriptEvaluator>().EvaluateAsync(new JavaScriptScriptEvaluationRequest(
            "workflowState.setOutput('Copy', workflowState.getOutput('Big')); return 0;", JsonSerializer.SerializeToElement(new { }), CancellationToken.None)
            { WorkflowState = state, Warnings = warnings });
        Assert.True(state.TryGetOutput("Copy", out var copy));
        Assert.Equal("9007199254740992", copy.GetRawText());
        Assert.DoesNotContain(warnings.Items, warning => warning.Message.Contains("Nothing was written", StringComparison.Ordinal));
    }

    private sealed class AllowLoss : IJavaScriptPrecisionPolicy
    {
        public JavaScriptPrecisionDecision Decide(JavaScriptPrecisionContext context) => JavaScriptPrecisionDecision.Allow();
    }
    private sealed class Warnings : IExecutionWarningSink
    {
        public List<ExecutionWarning> Items { get; } = [];
        public void Report(ExecutionWarning warning) => Items.Add(warning);
    }

    private static async Task<JsonElement?> Evaluate(string source, State? state)
    {
        var services = new ServiceCollection();
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>().EvaluateAsync(
            new JavaScriptScriptEvaluationRequest(source, JsonSerializer.SerializeToElement(new { workflowState = new { } }), CancellationToken.None)
            { WorkflowState = state })).Value;
    }

    private sealed class State : IScriptWorkflowState
    {
        private Exception? _refusal;
        private readonly Dictionary<string, JsonElement> _outputs = new(StringComparer.Ordinal);
        public bool HasReadProtectedValue => false;
        public string? CorrelationId { get; set; }
        public string? InstanceName { get; set; }
        public bool TryGetOutput(string name, out JsonElement value) => _outputs.TryGetValue(name, out value);
        public void SetOutput(string name, JsonElement value) => _outputs[name] = value.Clone();
        public void Refuse(string code, string reason) { _refusal ??= new InvalidOperationException($"{code}: {reason}"); throw _refusal; }
        public void ThrowIfRefused() { if (_refusal is { } fault) throw fault; }
    }
}
