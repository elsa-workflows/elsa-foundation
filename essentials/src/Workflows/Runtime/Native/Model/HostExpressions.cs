using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>An expression for the host to evaluate, as a <see cref="HostExpressionBinding"/> carries it.</summary>
public sealed record HostExpression(string Language, string Text, string? ResultType, JsonObject? Options);

/// <summary>
/// The host's evaluator for <see cref="HostExpressionBinding"/>s (owner decision of 25 November). Bindings are evaluated
/// synchronously inside a turn, so evaluation is synchronous. A burst opens one session the first time it evaluates a
/// host expression and disposes it when it ends, so a host can hold what it needs for the turn, such as a service scope.
/// </summary>
public interface INativeExpressions
{
    INativeExpressionSession Open(CancellationToken cancellationToken);
}

public interface INativeExpressionSession : IDisposable
{
    /// <summary>
    /// The expression's value. The parameters are resolved, and the variables are those visible where the node runs, by
    /// name. Any exception faults the activation that reads it with <c>expression.failed</c>.
    /// </summary>
    JsonNode? Evaluate(HostExpression expression, IReadOnlyDictionary<string, JsonNode?> parameters, IReadOnlyDictionary<string, JsonNode?> variables,
        string instanceId);
}

/// <summary>A host expression that did not evaluate; the burst faults the frame that read it, naming the node and input.</summary>
public sealed class NativeExpressionException(string message, Exception? inner = null) : Exception(message, inner)
{
    public string? Input { get; init; }
    public NativeExpressionException For(string input) => new(Message, InnerException) { Input = input };
}

internal sealed partial class Burst
{
    private INativeExpressionSession? _expressions;

    private JsonNode? EvaluateHost(HostExpressionBinding binding, IScope scope)
    {
        var parameters = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var parameter in binding.Parameters) parameters[parameter.Name] = Evaluate(parameter.Value, scope);
        var variables = scope is IVisibleScope visible ? visible.Visible() : new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (options.Expressions is not { } host)
            throw new NativeExpressionException($"The host composes no evaluator for {binding.Language} expressions.");
        try
        {
            _expressions ??= host.Open(cancellationToken);
            return _expressions.Evaluate(new HostExpression(binding.Language, binding.Text, binding.ResultType, binding.Options), parameters, variables, turn.Actor.Id);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            if (error is NativeExpressionException) throw;
            var message = $"The {binding.Language} expression failed: {error.GetType().Name}: {error.Message}";
            throw new NativeExpressionException(message[..Math.Min(message.Length, 2048)], error);
        }
    }

    private void CloseExpressions()
    {
        _expressions?.Dispose();
        _expressions = null;
    }

    /// <summary>Runs work that evaluates bindings and faults <paramref name="frame"/> when a host expression in it does not evaluate.</summary>
    private void Guard(Frame frame, Action action)
    {
        try { action(); }
        catch (NativeExpressionException failed) { Fail(frame, ExpressionFault(frame, failed)); }
    }

    private static ActivityFault ExpressionFault(Frame frame, NativeExpressionException failed) =>
        new("expression.failed", failed.Input is { } input ? $"Node '{frame.Node}', input '{input}': {failed.Message}" : $"Node '{frame.Node}': {failed.Message}");

    /// <summary>The variables visible to a frame, by name, innermost first: what a host expression reads as <c>variables</c>.</summary>
    internal Dictionary<string, JsonNode?> VisibleVariables(Frame? frame)
    {
        var visible = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var scope in VisibleScopes(frame))
            foreach (var (name, value) in scope) visible.TryAdd(name, value?.DeepClone());
        if (BodyScope(frame) is null)
            foreach (var (name, value) in _s.Variables) visible.TryAdd(name, value?.DeepClone());
        return visible;
    }

    internal Dictionary<string, JsonNode?> VisibleInScope(Frame owner, string lexicalScope, bool closed)
    {
        var visible = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var values in LexicalValues(owner.Id, lexicalScope, new(StringComparer.Ordinal)))
            foreach (var (name, value) in values) visible.TryAdd(name, value?.DeepClone());
        if (!closed)
            foreach (var (name, value) in VisibleVariables(owner)) visible.TryAdd(name, value);
        return visible;
    }
}

/// <summary>A scope that can list what it shows, for a host expression's ambient variables.</summary>
internal interface IVisibleScope
{
    IReadOnlyDictionary<string, JsonNode?> Visible();
}
