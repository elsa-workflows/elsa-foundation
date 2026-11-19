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
/// The JavaScript value adapter (ADR 0046 V12, spec 1228 FR-027, FR-028): its rules, written in
/// <c>docs/reference/value-adapters.md</c>, are a version. A change to a rule is a new version, never an edit of this one.
/// </summary>
public static class JavaScriptValueAdapter
{
    /// <summary>The JSON profile, first version: plain changeable values in, changes out, lossy numbers refused at a write.</summary>
    public const string Version = "json-1";
}

/// <summary>The precision policy refused a write (FR-013). Nothing of the step is written.</summary>
public sealed class JavaScriptPrecisionException : InvalidOperationException
{
    public JavaScriptPrecisionException(string target, IReadOnlyList<JavaScriptPrecisionDiagnostic> diagnostics)
        : base(Describe(target, diagnostics))
    {
        Target = target;
        Diagnostics = diagnostics;
    }

    public string Code => JavaScriptValueDiagnostics.PrecisionLossRefused;
    public string Target { get; }
    public IReadOnlyList<JavaScriptPrecisionDiagnostic> Diagnostics { get; }

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
public sealed class JavaScriptInputExpressionWriteException : InvalidOperationException
{
    public JavaScriptInputExpressionWriteException(string variable, string path)
        : base(Describe(variable, path))
    {
        Variable = variable;
        Path = path;
    }

    public string Code => JavaScriptValueDiagnostics.InputExpressionWrite;
    public string Variable { get; }
    public string Path { get; }

    /// <summary>The diagnostic text, shared with the publish-time refusal.</summary>
    public static string Describe(string variable, string path) =>
        $"{JavaScriptValueDiagnostics.InputExpressionWrite}: the input expression changes variable '{variable}' at '{path}'. " +
        "Input expressions only read; use a script activity (Run JavaScript) to change a variable, or change a local copy.";
}
