using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

public sealed class ScriptWorkflowStateTests
{
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
