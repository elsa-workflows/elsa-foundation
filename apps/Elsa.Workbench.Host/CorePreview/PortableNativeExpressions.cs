using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Kernel.Native;

namespace Elsa.Workbench.CorePreview;

/// <summary>
/// Host-evaluated expressions on the kernel host (owner decision of 25 November): each one runs on the shell's
/// <see cref="IPortableExpressionEvaluator"/>, which applies the binding-pure profile and normalizes the result by its type,
/// in one service scope a turn opens the first time it evaluates one. The resolved parameters become the definition's
/// literal parameters; JavaScript also sees the visible variables its source names, as the old core passes them. An
/// evaluation that does not complete synchronously faults, since a turn evaluates bindings synchronously. A shell that is
/// not active, or that is going away, makes the evaluator unavailable: the turn rolls back and keeps its messages, so
/// Recover runs it again, rather than committing a fault (the Core Preview starts only once the shell is active).
/// Release 1's kernel runs on the default shell only, and host expressions use that shell's JavaScript limits.
/// </summary>
internal sealed class PortableNativeExpressions(Func<(IServiceProvider Services, IAsyncDisposable Lifetime)> open) : INativeExpressions
{
    public INativeExpressionSession Open(CancellationToken cancellationToken)
    {
        var (services, lifetime) = open();
        try { return new Session(services.GetRequiredService<IPortableExpressionEvaluator>(), lifetime, cancellationToken); }
        catch
        {
            _ = lifetime.DisposeAsync().AsTask().ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
            throw;
        }
    }

    private sealed class Session(IPortableExpressionEvaluator evaluator, IAsyncDisposable lifetime, CancellationToken cancellationToken) : INativeExpressionSession
    {
        private static readonly JsonElement NoOptions = JsonSerializer.SerializeToElement(new JsonObject());
        private static readonly JsonElement Null = JsonSerializer.SerializeToElement<object?>(null);

        public JsonNode? Evaluate(HostExpression expression, IReadOnlyDictionary<string, JsonNode?> parameters, IReadOnlyDictionary<string, JsonNode?> variables)
        {
            // The mapper gives every expression its reading input's type, which Liquid normalizes its text by.
            var resultType = expression.ResultType ?? throw new InvalidOperationException($"The {expression.Language} expression names no result type.");
            var values = parameters.ToDictionary(p => p.Key, p => Element(p.Value), StringComparer.Ordinal);
            var definition = new ExpressionDefinition(
                expression.Language,
                expression.Text,
                new TypeReference(resultType),
                values.ToDictionary(p => p.Key, p => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(p.Value), StringComparer.Ordinal),
                expression.Options is { } options ? JsonSerializer.SerializeToElement(options) : NoOptions,
                ExpressionCapabilityProfiles.BindingReadonlyV1);
            var ambient = StringComparer.OrdinalIgnoreCase.Equals(expression.Language, "JavaScript") ? AmbientVariables.Referenced(expression.Text, variables) : null;
            var reader = ambient is null ? null : new AmbientVariableReader(ambient);
            var pending = evaluator.EvaluateAsync(new ExpressionEvaluationRequest(definition, values, reader, cancellationToken));
            if (!pending.IsCompleted)
            {
                // Observe the abandoned evaluation so that its failure is not left unobserved.
                _ = pending.AsTask().ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                throw new InvalidOperationException($"The {expression.Language} expression did not complete synchronously.");
            }
            var result = pending.GetAwaiter().GetResult();
            return result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : JsonNode.Parse(result.GetRawText());
        }

        public ValueTask DisposeAsync() => lifetime.DisposeAsync();

        private static JsonElement Element(JsonNode? value) => value is null ? Null : JsonSerializer.SerializeToElement(value);
    }
}

/// <summary>
/// The visible variables a JavaScript source reads through <c>variables.X</c>, <c>variables['X']</c>,
/// <c>getVariable('X')</c> or <c>getX()</c>, as the old core selects them, so an unrelated variable is never copied or
/// checked against the engine's input limits. A computed access selects every visible variable.
/// </summary>
internal static class AmbientVariables
{
    public static IReadOnlyDictionary<string, JsonElement>? Referenced(string source, IReadOnlyDictionary<string, JsonNode?> visible)
    {
        if (visible.Count == 0) return null;
        var all = HasComputedAccess(source);
        var selected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var name in visible.Keys)
            if (all || IsReferenced(source, name))
                selected[name] = JsonSerializer.SerializeToElement(visible[name]);
        return selected.Count == 0 ? null : selected;
    }

    private static bool IsReferenced(string source, string name)
    {
        var escaped = Regex.Escape(name);
        return Regex.IsMatch(source, $@"variables\s*\.\s*{escaped}(?![\w$])", RegexOptions.CultureInvariant) ||
               Regex.IsMatch(source, $@"variables\s*\[\s*(['""]){escaped}\1", RegexOptions.CultureInvariant) ||
               Regex.IsMatch(source, $@"getVariable\s*\(\s*(['""]){escaped}\1", RegexOptions.CultureInvariant) ||
               Regex.IsMatch(source, $@"(?<![\w$.])get{Regex.Escape(ToPascalCase(name))}(?![\w$])", RegexOptions.CultureInvariant);
    }

    private static bool HasComputedAccess(string source) =>
        Regex.IsMatch(source, @"variables\s*\[\s*[^'""\]\s]", RegexOptions.CultureInvariant) ||
        Regex.IsMatch(source, @"getVariable\s*\(\s*[^'"")\s]", RegexOptions.CultureInvariant);

    private static string ToPascalCase(string name) => name.Length == 0 ? name : $"{char.ToUpperInvariant(name[0])}{name[1..]}";
}

/// <summary>
/// Serves the visible variables a kernel expression references as its variable reads (spec 1222 FR-008): the kernel holds
/// them in memory before evaluation, so a read is a lookup, and a name the source does not reference is missing.
/// </summary>
internal sealed class AmbientVariableReader(IReadOnlyDictionary<string, JsonElement> variables) : IExpressionVariableReader
{
    public IReadOnlyList<string> CandidateNames { get; } = variables.Keys.ToArray();

    public ExpressionVariableRead Read(string name) =>
        variables.TryGetValue(name, out var value) ? ExpressionVariableRead.Present(value) : ExpressionVariableRead.Missing;
}
