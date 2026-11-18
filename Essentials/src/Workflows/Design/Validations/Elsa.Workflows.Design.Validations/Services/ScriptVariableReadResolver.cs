using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

using Elsa.Workflows.Design.Core.Services;

namespace Elsa.Workflows.Design.Validations.Services;

/// <summary>
/// Resolves the variable reads of one script at publish (spec 1222 FR-001, FR-002, FR-008, FR-009, FR-014): it parses
/// the source with its language's <see cref="IScriptVariableReadAnalyzer"/>, resolves each literal name nearest scope
/// first over the scopes visible to the node (loop iteration values innermost, then containers, then the workflow), and
/// reports a script with a computed or opaque read conservatively. It never rewrites the source.
/// </summary>
/// <remarks>A later analyzer registration for a language replaces an earlier one.</remarks>
public sealed class ScriptVariableReadResolver(IEnumerable<IScriptVariableReadAnalyzer> analyzers)
{
    private readonly IReadOnlyDictionary<string, IScriptVariableReadAnalyzer> _analyzers = analyzers
        .GroupBy(analyzer => analyzer.Language, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

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
