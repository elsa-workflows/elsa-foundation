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
    public ScriptVariableReadAnalysis(
        IReadOnlyList<string> literalNames,
        bool hasComputedRead,
        IReadOnlyList<ScriptVariableLiteralRead>? literalReads = null)
    {
        ArgumentNullException.ThrowIfNull(literalNames);
        LiteralNames = literalNames.Distinct(StringComparer.Ordinal).ToArray();
        HasComputedRead = hasComputedRead;
        LiteralReads = literalReads?.ToArray() ?? [];
    }

    /// <summary>
    /// Where each literal read names its variable in the source, when the parser gives positions (spec 1222 FR-022): a
    /// rename edits exactly these. Empty when the language's parser gives none; a rename then flags the reads instead.
    /// </summary>
    public IReadOnlyList<ScriptVariableLiteralRead> LiteralReads { get; }

    /// <summary>The names read literally (<c>variables.X</c>, <c>variables['X']</c>, <c>getVariable('X')</c>), in first-read order, each once.</summary>
    public IReadOnlyList<string> LiteralNames { get; }

    /// <summary>Whether the script also reads variables it does not name literally, so publish sets the fallback marker.</summary>
    public bool HasComputedRead { get; }

    /// <summary>Whether the script reads any variable.</summary>
    public bool ReadsVariables => LiteralNames.Count > 0 || HasComputedRead;

    /// <summary>
    /// The writes the source shows (spec 1228 FR-006a): an assignment, a compound assignment, <c>++</c>, <c>--</c> or
    /// <c>delete</c> whose target is rooted at <c>variables.X</c>, <c>variables['X']</c> or <c>getVariable('X')</c>.
    /// An input expression with one is refused at publish; other writes are caught at run time.
    /// </summary>
    public IReadOnlyList<ScriptVariableWrite> Writes { get; init; } = [];

    /// <summary>
    /// The variables named literally whose value the source lets escape (spec 1231 FR-019): into a local, a function
    /// argument, a closure, a returned function value, or a method call on it (<c>variables.items.push(1)</c>). A script
    /// can change an object or array value that escaped, so such a variable may be written; a scalar value cannot be.
    /// </summary>
    public IReadOnlyList<string> Escapes { get; init; } = [];

    /// <summary>
    /// Whether the source writes variables it does not name literally (spec 1231 FR-020a): a computed write
    /// (<c>variables[k] = v</c>, <c>setVariable(k, v)</c>), the value of a computed read escaping, or <c>variables</c>,
    /// <c>getVariable</c> or <c>setVariable</c> used as a value. Such a node may write every visible variable.
    /// </summary>
    public bool HasComputedWrite { get; init; }

    /// <summary>The variables the source may write by name: each written or escaped literal name, once, in source order.</summary>
    public IReadOnlyList<string> MayWriteNames => Writes.Select(write => write.Name).Concat(Escapes).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Independent workflow-state access and possible effects, unrelated to variable names.</summary>
    public Elsa.Expressions.Core.Models.ScriptWorkflowStateEffects WorkflowState { get; init; } = Elsa.Expressions.Core.Models.ScriptWorkflowStateEffects.None;

    public static ScriptVariableReadAnalysis None { get; } = new([], false);
}

/// <summary>
/// One literal variable read in the source: the name, and the zero-based range [<paramref name="Start"/>,
/// <paramref name="End"/>) of the token that spells it: the member name of <c>variables.X</c>
/// (<paramref name="IsMemberName"/>), or the string literal, quotes included, of <c>variables['X']</c> and
/// <c>getVariable('X')</c>.
/// </summary>
public sealed record ScriptVariableLiteralRead(string Name, int Start, int End, bool IsMemberName);

/// <summary>One write to a variable the source shows: the variable's name and the path written, such as <c>variables.order.total</c>.</summary>
public sealed record ScriptVariableWrite(string Name, string Path)
{
    /// <summary>The zero-based offset in the source where the write starts, or -1 when the parser gives none.</summary>
    public int Start { get; init; } = -1;
}
