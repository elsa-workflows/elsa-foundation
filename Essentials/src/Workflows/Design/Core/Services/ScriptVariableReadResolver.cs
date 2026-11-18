using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Services;

/// <summary>
/// Resolves the variable reads of one script at publish (spec 1222 FR-001, FR-002, FR-008, FR-009, FR-014): it parses
/// the source with its language's <see cref="IScriptVariableReadAnalyzer"/>, resolves each literal name nearest scope
/// first over the scopes visible to the node (loop iteration values innermost, then containers, then the workflow), and
/// reports a script with a computed or opaque read conservatively. It never rewrites the source.
/// </summary>
public sealed class ScriptVariableReadResolver(IEnumerable<IScriptVariableReadAnalyzer> analyzers)
{
    private readonly IReadOnlyDictionary<string, IScriptVariableReadAnalyzer> _analyzers = analyzers
        .GroupBy(analyzer => analyzer.Language, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a script in <paramref name="language"/> can read variables by name.</summary>
    public bool Supports(string? language) => language is not null && _analyzers.ContainsKey(language);

    /// <summary>Parses <paramref name="source"/>, or returns <see langword="null"/> when the language has no analyzer or the source does not parse.</summary>
    public ScriptVariableReadAnalysis? Analyze(string language, string source, bool scriptBody = false) =>
        _analyzers.TryGetValue(language, out var analyzer) ? analyzer.Analyze(source, scriptBody) : null;

    /// <summary>
    /// Resolves the reads of <paramref name="source"/> against <paramref name="visibleScopes"/> (nearest first, as
    /// <see cref="ScopedVariableVisibility.GetVisibleScopes"/> returns them with iteration scopes included). Returns
    /// <see langword="null"/> when the language has no analyzer or the source does not parse.
    /// </summary>
    public ScriptVariableReadPlan? Resolve(
        string language,
        string source,
        IReadOnlyList<VisibleVariableScope> visibleScopes,
        bool scriptBody = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(visibleScopes);
        if (!_analyzers.TryGetValue(language, out var analyzer) || analyzer.Analyze(source, scriptBody) is not { } analysis)
            return null;
        if (!analysis.ReadsVariables)
            return ScriptVariableReadPlan.Empty;

        var reachable = Reachable(visibleScopes);
        var pinned = new List<ResolvedScriptVariableRead>();
        var unresolved = new List<string>();
        foreach (var name in analysis.LiteralNames)
        {
            if (reachable.FirstOrDefault(read => StringComparer.Ordinal.Equals(read.Name, name)) is { } resolved)
                pinned.Add(resolved);
            else
                unresolved.Add(name);
        }

        return new ScriptVariableReadPlan(pinned, unresolved, analysis.HasComputedRead ? reachable : null);
    }

    /// <summary>Every variable a name can reach from the node: nearest scope first, each name once (the innermost declaration).</summary>
    private static IReadOnlyList<ResolvedScriptVariableRead> Reachable(IReadOnlyList<VisibleVariableScope> visibleScopes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reachable = new List<ResolvedScriptVariableRead>();
        foreach (var scope in visibleScopes)
        {
            foreach (var variable in scope.Variables)
            {
                if (seen.Add(variable.Name))
                    reachable.Add(new ResolvedScriptVariableRead(variable.Name, scope.ScopeId, variable, scope.IsIterationScope));
            }
        }

        return reachable;
    }
}

/// <summary>What publish resolved for one script's variable reads.</summary>
public sealed record ScriptVariableReadPlan(
    IReadOnlyList<ResolvedScriptVariableRead> Pinned,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<ResolvedScriptVariableRead>? Candidates)
{
    public static ScriptVariableReadPlan Empty { get; } = new([], [], null);

    /// <summary>Whether the script has a computed or opaque read and needs the run-time fallback.</summary>
    public bool NeedsFallback => Candidates is not null;

    /// <summary>The pinned <c>Variable</c> parameters, keyed <c>variables.&lt;name&gt;</c> (spec 1222 FR-002, FR-003).</summary>
    public IReadOnlyDictionary<string, VariableExpressionParameterBinding> PinnedParameters() =>
        Pinned.ToDictionary(
            read => ExpressionParameterSurfaces.VariablesKey(read.Name),
            read => new VariableExpressionParameterBinding(read.ScopeId, read.Variable.ReferenceKey, ExpressionParameterSurfaces.Variables),
            StringComparer.Ordinal);

    /// <summary>The fallback marker with the conservative read report, or <see langword="null"/> for a literal-only script.</summary>
    public VariableReadFallback? Fallback() =>
        Candidates is null
            ? null
            : new VariableReadFallback(Candidates
                .Select(read => new VariableReadCandidate(read.Name, read.ScopeId, read.Variable.ReferenceKey))
                .ToArray());
}

/// <summary>One variable a script's name resolves to: the name, the scope that declares it, the declaration, and whether it is a loop iteration value.</summary>
public sealed record ResolvedScriptVariableRead(string Name, string ScopeId, VariableDefinition Variable, bool IsIterationValue);
