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
    /// Writes back what a script body changed (FR-003 to FR-007, spec 1222 FR-033): each variable it changed, member by
    /// member or as a whole, goes through the precision policy and then to the step's variable session, which commits it
    /// with the step (ADR 0090). A member change keeps the untouched members' stored text (C3). A change the script made
    /// without assigning the variable is still a write, and a warning makes it visible (ADR 0090 C2). Returns the losses
    /// the writes carried, so the read-only warning does not count them twice.
    /// </summary>
    public static IReadOnlyList<JavaScriptPrecisionLoss> WriteVariables(
        IsolatedJintEngine.ScriptVariables variables,
        IScriptVariableSession session,
        IJavaScriptPrecisionPolicy policy,
        ICollection<ExecutionWarning> warnings)
    {
        var losses = new List<JavaScriptPrecisionLoss>();
        // Every change is checked before any is written, so a refusal writes nothing of the step.
        var writes = new List<(string Name, ScriptValueWrite Write)>();
        foreach (var (name, change) in variables.Collect())
        {
            if (change.Kind == ScriptValueChangeKind.Unchanged)
                continue;
            var write = Apply(new ScriptValueWrite(change.Value, change.Losses), change.Key, policy);
            losses.AddRange(write.Losses);
            writes.Add((name, write));
            if (!variables.WasAssigned(name))
                warnings.Add(ChangeWithoutAssignment(name, change));
        }

        foreach (var (name, write) in writes)
            session.Write(name, write.Value);
        return losses;
    }

    // ADR 0090 C2: names the variable and the changed paths; the protected form names the variable only, since a member
    // path can be a key of the stored value.
    private static ExecutionWarning ChangeWithoutAssignment(string name, ScriptValueChange change)
    {
        var key = change.Key;
        var paths = string.Join(", ", change.ChangedPaths);
        string Message(string where) =>
            $"The script changed variable '{name}' ({where}) without assigning it, so the change is written. Assign the variable " +
            $"({key} = …) to make the write explicit, or copy it ({{...{key}}}) to change a local copy only.";
        return new ExecutionWarning(
            JavaScriptValueDiagnostics.ChangeWithoutAssignment,
            Message(paths),
            ExecutionWarningSeverity.Warning,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["variable"] = name, ["paths"] = paths })
        {
            ProtectedForm = StringComparer.Ordinal.Equals(paths, key)
                ? null
                : new ExecutionWarning(
                    JavaScriptValueDiagnostics.ChangeWithoutAssignment,
                    Message(key),
                    ExecutionWarningSeverity.Warning,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["variable"] = name, ["paths"] = key })
        };
    }

    /// <summary>
    /// One warning per evaluation for the lossy numbers it read but did not write (FR-015). It never refuses. It goes to
    /// the run-time warning channel, which records it on the activity execution, and to the log. It names the paths and
    /// why, never a value (spec 1229 FR-006).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex HostFunctionCall = new(
        @"^([A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*)\(\)\[\d+\]",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
        // Each host-function call's result has a path of its own (acme.lookup()[3]); reported once per function.
        var paths = string.Join(", ", read.Select(number => HostFunctionCall.Replace(number.Path, "$1()")).Distinct(StringComparer.Ordinal));
        var roots = string.Join(", ", read.Select(number => ValuePaths.Root(number.Path)).Distinct(StringComparer.Ordinal));
        var integers = read.Any(number => number.IsInteger);
        var decimals = read.Any(number => !number.IsInteger);
        var why = (integers, decimals) switch
        {
            (true, false) => $"integers beyond {RefusingPrecisionPolicy.MaxSafeInteger}",
            (false, true) => "decimals with more digits than a JavaScript number holds",
            _ => $"integers beyond {RefusingPrecisionPolicy.MaxSafeInteger} or decimals with more digits than a JavaScript number holds"
        };
        string Message(string where) =>
            $"JavaScript read numbers it cannot hold exactly ({why}) and computed with the nearest value it holds: {where}. " +
            "Nothing was written from them. Hold such numbers as strings to keep every digit.";
        // A member path can name keys of a stored value: the protected form and the log name the roots only.
        var protectedForm = StringComparer.Ordinal.Equals(paths, roots)
            ? null
            : new ExecutionWarning(
                JavaScriptValueDiagnostics.PrecisionLossRead,
                Message(roots),
                ExecutionWarningSeverity.Warning,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["paths"] = roots });
        sink?.Report(new ExecutionWarning(
            JavaScriptValueDiagnostics.PrecisionLossRead,
            Message(paths),
            ExecutionWarningSeverity.Warning,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["paths"] = paths }) { ProtectedForm = protectedForm });
        logger?.LogWarning("{Code}: {Message}", JavaScriptValueDiagnostics.PrecisionLossRead, Message(roots));
    }
}
