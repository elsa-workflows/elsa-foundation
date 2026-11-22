using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>What publish resolved for one script's variable reads.</summary>
public sealed record ScriptVariableReadPlan(
    IReadOnlyList<ResolvedScriptVariableRead> Pinned,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<ResolvedScriptVariableRead>? Candidates)
{
    public static ScriptVariableReadPlan Empty { get; } = new([], [], null);

    /// <summary>The writes to literally named variables the source shows (spec 1228 FR-006a).</summary>
    public IReadOnlyList<ScriptVariableWrite> Writes { get; init; } = [];

    /// <summary>The literal names whose value the script lets escape (spec 1231 FR-019); see <see cref="ScriptVariableReadAnalysis.Escapes"/>.</summary>
    public IReadOnlyList<string> Escapes { get; init; } = [];

    /// <summary>Whether the script writes variables it does not name, so the node may write every visible variable (spec 1231 FR-020a).</summary>
    public bool HasComputedWrite { get; init; }

    /// <summary>
    /// The variables a script body may write, resolved (spec 1231 FR-019): every variable it writes literally, and every
    /// variable whose value escapes unless that value is a scalar, which a script cannot change in place. Loop items and
    /// indexes are frozen, so they are never written; names that resolve to nothing are refused before this is read.
    /// </summary>
    public IReadOnlyList<ResolvedScriptVariableRead> MayWrite()
    {
        var written = Writes.Select(write => write.Name).ToHashSet(StringComparer.Ordinal);
        var result = new List<ResolvedScriptVariableRead>();
        foreach (var name in Writes.Select(write => write.Name).Concat(Escapes).Distinct(StringComparer.Ordinal))
        {
            if (Pinned.FirstOrDefault(read => StringComparer.Ordinal.Equals(read.Name, name)) is not { IsIterationValue: false } resolved)
                continue;
            if (!written.Contains(name) && IsScalar(resolved.Variable.Type))
                continue;
            result.Add(resolved);
        }

        return result;
    }

    /// <summary>
    /// The literal writes publication refuses (spec 1231 FR-020), each with its code: a name no visible scope declares,
    /// a loop item or index, and a transient variable as <paramref name="isTransient"/> decides it (the storage profile is
    /// a runtime decision, so a caller without one passes nothing).
    /// </summary>
    public IEnumerable<(ScriptVariableWrite Write, string Code)> RefusedLiteralWrites(Func<VariableDefinition, bool>? isTransient = null)
    {
        foreach (var write in Writes)
        {
            var resolved = Pinned.FirstOrDefault(read => StringComparer.Ordinal.Equals(read.Name, write.Name));
            var code = resolved switch
            {
                null => Services.ScriptVariableReadDiagnostics.UndeclaredLiteralWrite,
                { IsIterationValue: true } => Services.ScriptVariableReadDiagnostics.ReadOnlyLiteralWrite,
                _ when isTransient?.Invoke(resolved.Variable) == true => Services.ScriptVariableReadDiagnostics.TransientLiteralWrite,
                _ => null
            };
            if (code is not null)
                yield return (write, code);
        }
    }

    private static bool IsScalar(TypeReference type) =>
        type.CollectionKind == CollectionKind.Single &&
        !string.Equals(type.Alias, TypeAliasConvention.AnyAlias, StringComparison.OrdinalIgnoreCase) &&
        TypeAliasConvention.ReservedBareAliases.Contains(type.Alias);

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
