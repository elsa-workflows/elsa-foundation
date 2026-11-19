using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Declares a JavaScript value contract a replacement contract: exactly one implementation is active per host. Shell
/// activation fails when more than one is registered, naming every registration (framework §2.6.2).
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class JavaScriptValueReplacementContractAttribute : Attribute;

/// <summary>
/// Decides what happens when a number a JavaScript number cannot hold exactly reaches a write (ADR 0046 V9, D-17,
/// spec 1228 FR-012). Replacement contract: exactly one implementation is active per host, host-wide, with no
/// per-workflow or per-node override. The JavaScript Jint feature installs <c>RefusingPrecisionPolicy</c>; a host that
/// wants other handling replaces it (<c>services.Replace(...)</c>) from its own feature.
/// </summary>
/// <remarks>
/// Called only for a write that carries at least one loss: a script's or an input expression's result, or a change a
/// script made. Computing with a lossy value and passing one to a host function never reach the policy; they warn.
/// </remarks>
[JavaScriptValueReplacementContract]
public interface IJavaScriptPrecisionPolicy
{
    JavaScriptPrecisionDecision Decide(JavaScriptPrecisionContext context);
}

/// <summary>A write that carries precision losses.</summary>
/// <param name="Target">What is written, such as <c>result</c> or <c>variables.order</c>.</param>
/// <param name="Losses">Each lossy number the written value carries, with its path.</param>
public sealed record JavaScriptPrecisionContext(string Target, IReadOnlyList<JavaScriptPrecisionLoss> Losses);

/// <summary>The policy's decision: allow or refuse, with a diagnostic per loss.</summary>
public sealed record JavaScriptPrecisionDecision(bool Allowed, IReadOnlyList<JavaScriptPrecisionDiagnostic> Diagnostics)
{
    public static JavaScriptPrecisionDecision Allow(IReadOnlyList<JavaScriptPrecisionDiagnostic>? diagnostics = null) => new(true, diagnostics ?? []);
    public static JavaScriptPrecisionDecision Refuse(IReadOnlyList<JavaScriptPrecisionDiagnostic> diagnostics) => new(false, diagnostics);
}

public enum JavaScriptDiagnosticSeverity
{
    Warning,
    Error
}

/// <summary>One precision diagnostic: where, the stored and the written value, why, and what to do.</summary>
public sealed record JavaScriptPrecisionDiagnostic(
    string Path,
    string Stored,
    string Written,
    string Reason,
    string Remedy,
    JavaScriptDiagnosticSeverity Severity);
