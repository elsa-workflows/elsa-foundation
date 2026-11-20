using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Design-time reading of a script activity's JavaScript for the outcomes it selects (spec 1208). It parses the source
/// and never evaluates it, so a design-time consumer (the draft validator) can check a script without a JavaScript
/// parser dependency of its own. Computed names and aliased <c>complete</c> calls are not reported: they are checked at
/// run time (<see cref="JavaScriptCompletionException"/>, <c>VF-ACT-006</c>).
/// </summary>
public interface IJavaScriptScriptOutcomeAnalyzer
{
    /// <summary>Analyzes <paramref name="script"/>, a script-activity program (top-level <c>return</c> allowed).</summary>
    /// <returns>The analysis, or <see langword="null"/> when the script does not parse; the expression tooling reports syntax errors.</returns>
    JavaScriptScriptOutcomeAnalysis? Analyze(string script);
}

/// <summary>What a script's source says about its outcome selection, as far as literals decide it.</summary>
public sealed class JavaScriptScriptOutcomeAnalysis
{
    public JavaScriptScriptOutcomeAnalysis(
        IReadOnlyList<string> literalOutcomes,
        IReadOnlyList<string> returnedStrings,
        IReadOnlyList<ExpressionToolingRange> reservedNameDeclarations)
    {
        ArgumentNullException.ThrowIfNull(literalOutcomes);
        ArgumentNullException.ThrowIfNull(returnedStrings);
        ArgumentNullException.ThrowIfNull(reservedNameDeclarations);
        LiteralOutcomes = Array.AsReadOnly(literalOutcomes.ToArray());
        ReturnedStrings = Array.AsReadOnly(returnedStrings.ToArray());
        ReservedNameDeclarations = Array.AsReadOnly(reservedNameDeclarations.ToArray());
    }

    /// <summary>
    /// The string literals passed as the outcomes of a <c>complete(result, outcomes)</c> call (one literal or an array of
    /// literals) wherever the call is, nested functions included; in source order, duplicates kept.
    /// </summary>
    public IReadOnlyList<string> LiteralOutcomes { get; }

    /// <summary>The string literals the script returns at its top level (outside every function).</summary>
    public IReadOnlyList<string> ReturnedStrings { get; }

    /// <summary>
    /// Where the script declares <see cref="JavaScriptScriptBuiltIns.Complete"/>, a reserved built-in name: as a variable
    /// (destructuring included), function, class, parameter (default and rest included) or catch binding. Zero-based.
    /// </summary>
    public IReadOnlyList<ExpressionToolingRange> ReservedNameDeclarations { get; }
}
