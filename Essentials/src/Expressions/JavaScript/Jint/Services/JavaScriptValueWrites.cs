using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Services;
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
    /// One warning per evaluation for the lossy numbers it read but did not write (FR-015). It never refuses. It goes to
    /// the run-time warning channel, which records it on the activity execution, and to the log. It names the paths and
    /// why, never a value (spec 1229 FR-006).
    /// </summary>
    public static void WarnLossyReads(
        IScriptValueEvaluation values,
        IReadOnlyCollection<JavaScriptPrecisionLoss> written,
        IExecutionWarningSink? sink,
        ILogger? logger)
    {
        if (values.LossyNumbers.Count == 0)
            return;
        var writtenPaths = written.Select(loss => loss.Entered.Path)
            .Concat(values.WrittenExactly.Select(number => number.Path))
            .ToHashSet(StringComparer.Ordinal);
        var read = values.LossyNumbers.Where(number => !writtenPaths.Contains(number.Path)).ToArray();
        if (read.Length == 0)
            return;
        var paths = string.Join(", ", read.Select(number => number.Path).Distinct(StringComparer.Ordinal));
        var integers = read.Any(number => number.IsInteger);
        var decimals = read.Any(number => !number.IsInteger);
        var why = (integers, decimals) switch
        {
            (true, false) => $"integers beyond {RefusingPrecisionPolicy.MaxSafeInteger}",
            (false, true) => "decimals with more digits than a JavaScript number holds",
            _ => $"integers beyond {RefusingPrecisionPolicy.MaxSafeInteger} or decimals with more digits than a JavaScript number holds"
        };
        var message =
            $"The script read numbers a JavaScript number cannot hold exactly ({why}) and computed with the nearest value it holds: {paths}. " +
            "Nothing was written from them. Hold such numbers as strings to keep every digit.";
        sink?.Report(new ExecutionWarning(
            JavaScriptValueDiagnostics.PrecisionLossRead,
            message,
            ExecutionWarningSeverity.Warning,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["paths"] = paths }));
        logger?.LogWarning("{Code}: {Message}", JavaScriptValueDiagnostics.PrecisionLossRead, message);
    }
}
