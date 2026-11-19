using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>What happens to a value leaving JavaScript to be written (spec 1228 FR-012 to FR-015).</summary>
internal static class JavaScriptValueWrites
{
    /// <summary>
    /// A write that carries a precision loss goes to the host's one precision policy; a refusal throws, so nothing of
    /// the step is written. A write with no loss never reaches the policy.
    /// </summary>
    public static ScriptValueWrite Apply(ScriptValueWrite write, string target, IJavaScriptPrecisionPolicy policy)
    {
        if (write.Losses.Count == 0)
            return write;
        var decision = policy.Decide(new JavaScriptPrecisionContext(target, write.Losses));
        if (!decision.Allowed)
            throw new JavaScriptPrecisionException(target, decision.Diagnostics);
        return write;
    }

    /// <summary>
    /// One warning per evaluation for the lossy numbers it read but did not write (FR-015). It never refuses. The
    /// warning is logged; the run-time warning channel that shows it on the instance is a follow-up.
    /// </summary>
    public static void WarnLossyReads(IScriptValueEvaluation values, IReadOnlyCollection<JavaScriptPrecisionLoss> written, ILogger? logger)
    {
        if (logger is null || values.LossyNumbers.Count == 0)
            return;
        var writtenPaths = written.Select(loss => loss.Entered.Path).ToHashSet(StringComparer.Ordinal);
        var read = values.LossyNumbers.Where(number => !writtenPaths.Contains(number.Path)).ToArray();
        if (read.Length == 0)
            return;
        logger.LogWarning(
            "{Code}: the script read numbers a JavaScript number cannot hold exactly, and computed with the nearest value it holds: {Paths}. Hold such numbers as strings.",
            JavaScriptValueDiagnostics.PrecisionLossRead,
            string.Join("; ", read.Select(number => $"'{number.Path}' stored {number.Stored}, held {number.Held}")));
    }
}
