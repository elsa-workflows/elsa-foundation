namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Reads, by parsing (never by regular expression, spec 1222 FR-014), which workflow variables a script names
/// literally and whether it also reads variables it does not name: a computed name (<c>variables[expr]</c>,
/// <c>getVariable(expr)</c>), the <c>variables</c> root or <c>getVariable</c> used as a value, or the script's own
/// declaration of either name. One implementation per expression language; it never evaluates the source.
/// </summary>
public interface IScriptVariableReadAnalyzer
{
    /// <summary>The expression language this analyzer parses, compared case-insensitively.</summary>
    string Language { get; }

    /// <summary>
    /// Analyzes <paramref name="source"/>. <paramref name="scriptBody"/> parses it as a script body (top-level
    /// <c>return</c> allowed), otherwise as a value binding. Returns <see langword="null"/> when the source does not
    /// parse; the expression's own validation reports that.
    /// </summary>
    ScriptVariableReadAnalysis? Analyze(string source, bool scriptBody = false);
}

/// <summary>The variable reads a script's source shows.</summary>
public sealed record ScriptVariableReadAnalysis
{
    public ScriptVariableReadAnalysis(IReadOnlyList<string> literalNames, bool hasComputedRead)
    {
        ArgumentNullException.ThrowIfNull(literalNames);
        LiteralNames = literalNames.Distinct(StringComparer.Ordinal).ToArray();
        HasComputedRead = hasComputedRead;
    }

    /// <summary>The names read literally (<c>variables.X</c>, <c>variables['X']</c>, <c>getVariable('X')</c>), in first-read order, each once.</summary>
    public IReadOnlyList<string> LiteralNames { get; }

    /// <summary>Whether the script also reads variables it does not name literally, so publish sets the fallback marker.</summary>
    public bool HasComputedRead { get; }

    /// <summary>Whether the script reads any variable.</summary>
    public bool ReadsVariables => LiteralNames.Count > 0 || HasComputedRead;

    public static ScriptVariableReadAnalysis None { get; } = new([], false);
}
