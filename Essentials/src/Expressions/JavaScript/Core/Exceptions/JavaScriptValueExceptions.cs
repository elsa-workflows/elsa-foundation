using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Exceptions;

/// <summary>The stable codes of the JavaScript value adapter (spec 1228).</summary>
public static class JavaScriptValueDiagnostics
{
    /// <summary>The precision policy refused a write that carries a precision loss (FR-013, FR-014).</summary>
    public const string PrecisionLossRefused = "VF-PREC-001";

    /// <summary>A lossy number was read but not written: a warning, never a refusal (FR-015).</summary>
    public const string PrecisionLossRead = "VF-PREC-002";

    /// <summary>A JavaScript input expression tried to change a variable (FR-006a, ADR 0083 <c>binding-readonly-v1</c>).</summary>
    public const string InputExpressionWrite = "VF-BIND-001";
}

/// <summary>
/// The precision policy refused a write (FR-013). Nothing of the step is written. Its message names each path with the
/// stored and written number; its <see cref="ValueFreeMessage"/> names neither, for a step or expression that read a
/// protected value (spec 1229 FR-006).
/// </summary>
public sealed class JavaScriptPrecisionException : InvalidOperationException, Elsa.Expressions.Core.Contracts.IValueFreeExpressionFailure
{
    public JavaScriptPrecisionException(string target, IReadOnlyList<JavaScriptPrecisionDiagnostic> diagnostics)
        : base(Describe(target, diagnostics))
    {
        Target = target;
        Diagnostics = diagnostics;
        ValueFreeMessage =
            $"{JavaScriptValueDiagnostics.PrecisionLossRefused}: writing '{target}' was refused because a number would lose " +
            "precision: an integer beyond 9007199254740991 or a decimal with more digits than a JavaScript number holds. " +
            "The numbers and their paths are withheld because a value it read is protected. Hold such numbers as strings.";
    }

    public string Code => JavaScriptValueDiagnostics.PrecisionLossRefused;
    public string Target { get; }
    public IReadOnlyList<JavaScriptPrecisionDiagnostic> Diagnostics { get; }

    /// <inheritdoc />
    public string ValueFreeMessage { get; }

    private static string Describe(string target, IReadOnlyList<JavaScriptPrecisionDiagnostic> diagnostics)
    {
        var lines = diagnostics.Select(diagnostic =>
            $"'{diagnostic.Path}': stored {diagnostic.Stored}, written {diagnostic.Written}. {diagnostic.Reason} {diagnostic.Remedy}");
        return $"{JavaScriptValueDiagnostics.PrecisionLossRefused}: writing '{target}' was refused because a number would lose precision. " +
               string.Join(" ", lines);
    }
}

/// <summary>
/// A JavaScript input expression changed a variable (FR-006a). Input expressions only read: they may run more than once
/// (replay, each loop pass, preflight), so a write would apply a variable number of times. The evaluation fails and
/// nothing is written.
/// </summary>
public sealed class InputExpressionWriteException : InvalidOperationException, Elsa.Expressions.Core.Contracts.IValueFreeExpressionFailure
{
    /// <param name="variable">The variable changed.</param>
    /// <param name="path">The first changed path, which can name keys of the stored value.</param>
    /// <param name="declared">
    /// Whether the variable is one the expression can name (pinned, or a candidate of its lookup). A name it computed can
    /// come from a value, so the value-free message withholds it.
    /// </param>
    public InputExpressionWriteException(string variable, string path, bool declared = true)
        : base(Describe(variable, path))
    {
        Variable = variable;
        Path = path;
        ValueFreeMessage =
            $"{JavaScriptValueDiagnostics.InputExpressionWrite}: the input expression changes " +
            (declared ? $"variable '{variable}'" : "a variable it names by a computed value") +
            "; the path is withheld because a value it read is protected. " +
            "Input expressions only read; use a script activity (Run JavaScript) to change a variable, or change a local copy.";
    }

    public string Code => JavaScriptValueDiagnostics.InputExpressionWrite;
    public string Variable { get; }
    public string Path { get; }

    /// <inheritdoc />
    public string ValueFreeMessage { get; }

    /// <summary>The diagnostic text, shared with the publish-time refusal.</summary>
    public static string Describe(string variable, string path) =>
        $"{JavaScriptValueDiagnostics.InputExpressionWrite}: the input expression changes variable '{variable}' at '{path}'. " +
        "Input expressions only read; use a script activity (Run JavaScript) to change a variable, or change a local copy.";
}
