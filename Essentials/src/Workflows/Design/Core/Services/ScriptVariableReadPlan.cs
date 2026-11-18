using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Services;

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
