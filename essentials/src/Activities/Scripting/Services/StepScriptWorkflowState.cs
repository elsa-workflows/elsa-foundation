using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Activities.Scripting.Services;

/// <summary>Keeps host refusals sticky across script catch blocks and evaluator replacements.</summary>
internal sealed class StepScriptWorkflowState(IStepWorkflowState state, Func<bool> protectedReads) : IScriptWorkflowState
{
    private Exception? _refusal;
    public bool HasReadProtectedValue => protectedReads() || state.HasReadProtectedValue;
    public bool TryGetOutput(string name, out JsonElement value)
    {
        try { return state.TryGetOutput(name, out value); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { _refusal ??= exception; throw; }
    }
    public void SetOutput(string name, JsonElement value) => Call(() => state.SetOutput(name, value));
    public string? CorrelationId
    {
        get => Read(() => state.CorrelationId);
        set => Call(() => state.CorrelationId = value);
    }
    public string? InstanceName
    {
        get => Read(() => state.InstanceName);
        set => Call(() => state.InstanceName = value);
    }
    public void Refuse(string code, string reason)
    {
        _refusal ??= new InvalidOperationException($"{code}: {reason}");
        throw _refusal;
    }
    public void ThrowIfRefused() { if (_refusal is { } refusal) throw refusal; }
    private string? Read(Func<string?> read)
    {
        try { return read(); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { _refusal ??= exception; throw; }
    }
    private void Call(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { _refusal ??= exception; throw; }
    }
}
