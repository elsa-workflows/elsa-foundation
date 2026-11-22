using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Elsa's only precision policy (D-17, spec 1228 FR-013, FR-014): it refuses every write that carries a precision loss.
/// It has no options. The diagnostic names the path, the stored and the written value, says why, and gives the remedy;
/// an element of an array written as a whole also names the array.
/// </summary>
/// <remarks>
/// Public so that a host's own policy can compose it: allow some losses and delegate the rest, or reuse
/// <see cref="Diagnose"/> for the same diagnostic text. It holds no state.
/// </remarks>
public sealed class RefusingPrecisionPolicy : IJavaScriptPrecisionPolicy
{
    /// <summary>The largest integer a JavaScript number holds exactly: 2^53 − 1.</summary>
    public const string MaxSafeInteger = "9007199254740991";

    public JavaScriptPrecisionDecision Decide(JavaScriptPrecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return JavaScriptPrecisionDecision.Refuse(context.Losses.Select(Diagnose).ToArray());
    }

    /// <summary>The diagnostic of one loss; also used, with warning severity, for a lossy number read but not written.</summary>
    public static JavaScriptPrecisionDiagnostic Diagnose(JavaScriptPrecisionLoss loss)
    {
        ArgumentNullException.ThrowIfNull(loss);
        var reason = Reason(loss.Entered);
        if (loss.WholeArrayPath is { } array)
            reason = $"The array '{array}' was written as a whole, and its element '{loss.Path}' is a number read from '{loss.Entered.Path}' that {reason}.";
        else
            reason = $"It is a number read from '{loss.Entered.Path}' that {reason}.";

        var remedy = loss.WholeArrayPath is null
            ? "Hold the number as a string, or do not write it."
            : "Hold the number as a string, or do not change the array.";
        return new JavaScriptPrecisionDiagnostic(loss.Path, loss.Entered.Stored, loss.Written, reason, remedy, JavaScriptDiagnosticSeverity.Error);
    }

    /// <summary>Why a number is lossy, as a clause.</summary>
    public static string Reason(JavaScriptLossyNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);
        return number.IsInteger
            ? $"exceeds the largest integer a JavaScript number holds exactly ({MaxSafeInteger})"
            : "has more digits than a JavaScript number holds";
    }
}
