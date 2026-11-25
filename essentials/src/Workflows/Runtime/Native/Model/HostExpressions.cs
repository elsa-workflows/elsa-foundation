using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>
/// An expression for the host to evaluate, as a <see cref="HostExpressionBinding"/> carries it. The result type is the alias
/// of the type the reading input expects, as the old core's portable evaluator receives it; no handler reads more of it.
/// </summary>
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

public interface INativeExpressionSession : IAsyncDisposable
{
    /// <summary>
    /// The expression's value. The parameters are resolved, and the variables are those visible where the node runs, by
    /// name, valid for the call; each is copied only when read, so a host reads only those its expression references (as the old core passes
    /// JavaScript only the ambient variables its source names). Any exception faults the activation that reads it with
    /// <c>expression.failed</c>.
    /// </summary>
    JsonNode? Evaluate(HostExpression expression, IReadOnlyDictionary<string, JsonNode?> parameters, IReadOnlyDictionary<string, JsonNode?> variables);
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
        var variables = scope is IVisibleScope visible ? visible.Visible() : VisibleValues.Empty;
        if (options.Expressions is not { } host)
            throw new NativeExpressionException($"The host composes no evaluator for {binding.Language} expressions.");
        try
        {
            _expressions ??= host.Open(cancellationToken);
            return _expressions.Evaluate(new HostExpression(binding.Language, binding.Text, binding.ResultType, binding.Options), parameters, variables);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            if (error is NativeExpressionException) throw;
            var message = $"The {binding.Language} expression failed: {error.GetType().Name}: {error.Message}";
            throw new NativeExpressionException(message[..Math.Min(message.Length, 2048)], error);
        }
    }

    private async ValueTask CloseExpressionsAsync()
    {
        if (_expressions is not { } session) return;
        _expressions = null;
        await session.DisposeAsync();
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
    internal VisibleValues VisibleVariables(Frame? frame) => new([.. VisibleSources(frame)]);

    internal VisibleValues VisibleInScope(Frame owner, string lexicalScope, bool closed) =>
        new([.. LexicalValues(owner.Id, lexicalScope, new(StringComparer.Ordinal)), .. closed ? Enumerable.Empty<IReadOnlyDictionary<string, JsonNode?>>() : VisibleSources(owner)]);

    private IEnumerable<IReadOnlyDictionary<string, JsonNode?>> VisibleSources(Frame? frame)
    {
        foreach (var scope in VisibleScopes(frame)) yield return scope;
        if (BodyScope(frame) is null) yield return _s.Variables;
    }
}

/// <summary>
/// Variables by name over scopes, innermost first; a value is copied when read, so an expression that reads none copies none.
/// </summary>
internal sealed class VisibleValues(IReadOnlyList<IReadOnlyDictionary<string, JsonNode?>> scopes) : IReadOnlyDictionary<string, JsonNode?>
{
    public static readonly VisibleValues Empty = new([]);

    public bool TryGetValue(string key, out JsonNode? value)
    {
        foreach (var scope in scopes)
            if (scope.TryGetValue(key, out var found)) { value = found?.DeepClone(); return true; }
        value = null;
        return false;
    }

    public JsonNode? this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
    public bool ContainsKey(string key) => scopes.Any(s => s.ContainsKey(key));
    public IEnumerable<string> Keys => scopes.SelectMany(s => s.Keys).Distinct(StringComparer.Ordinal);
    public IEnumerable<JsonNode?> Values => Keys.Select(k => this[k]);
    public int Count => Keys.Count();
    public IEnumerator<KeyValuePair<string, JsonNode?>> GetEnumerator() => Keys.Select(k => KeyValuePair.Create(k, this[k])).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A scope that can list what it shows, for a host expression's ambient variables.</summary>
internal interface IVisibleScope
{
    IReadOnlyDictionary<string, JsonNode?> Visible();
}
