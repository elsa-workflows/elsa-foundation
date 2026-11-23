using System.Text.Json;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Executes a stateful JavaScript program with one explicit immutable argument document. This is an
/// activity service, not an input-binding expression context, and exposes no workflow value writer.
/// </summary>
public interface IJavaScriptScriptEvaluator
{
    /// <summary>
    /// Runs the program and returns its result and its outcome selection. A program selects outcomes only by returning
    /// the value of the built-in <c>complete(result, outcomes)</c> (ADR 0045 §6, spec 1208 FR-007); any other returned
    /// value is the result alone, with an unspecified selection.
    /// </summary>
    /// <exception cref="JavaScriptCompletionException">The returned completion value is invalid (FR-007a, FR-007b).</exception>
    ValueTask<JavaScriptScriptEvaluationResult> EvaluateAsync(JavaScriptScriptEvaluationRequest request);
}

/// <summary>A complete, immutable request for one transient script-activity attempt.</summary>
public sealed class JavaScriptScriptEvaluationRequest
{
    public JavaScriptScriptEvaluationRequest(
        string source,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("JavaScript activity arguments must be one JSON object.", nameof(arguments));

        Source = source;
        Arguments = arguments.Clone();
        CancellationToken = cancellationToken;
    }

    public string Source { get; }
    public JsonElement Arguments { get; }
    public CancellationToken CancellationToken { get; }

    /// <summary>Where the evaluator reports warnings (spec 1228); the activity passes its step's sink.</summary>
    public Elsa.Expressions.Core.Contracts.IExecutionWarningSink? Warnings { get; init; }

    /// <summary>
    /// The step's variables, served to the script as the <c>variables</c> root and <c>getVariable</c> (spec 1222 FR-033);
    /// <see langword="null"/> when the step has no variable session, and the script then has no <c>variables</c> root.
    /// </summary>
    public IScriptVariableSession? Variables { get; init; }

    /// <summary>The bounded workflow-state capability; never supplied to an input expression evaluator.</summary>
    public IScriptWorkflowState? WorkflowState { get; init; }

}

/// <summary>
/// A script activity's variables (spec 1222 FR-033 to FR-035, spec 1228 FR-006): each read goes through the step's
/// variable session, which returns the committed value plus the step's own pending writes and records the read; each
/// change the script made is written back through the same session, committed with the step (ADR 0090).
/// </summary>
public interface IScriptVariableSession : Elsa.Expressions.Core.Contracts.IExpressionVariableReader
{
    /// <summary>
    /// Writes a variable's whole value: the stored value with the script's changes patched in, or the value it assigned
    /// (ADR 0090 C3). An undeclared name, a read-only or transient variable faults the step.
    /// </summary>
    void Write(string name, JsonElement? value);

    /// <summary>
    /// Whether a value the script read is protected (sensitive, encrypted or redacted): its diagnostics then use their
    /// value-free form (spec 1229 FR-006).
    /// </summary>
    bool HasReadProtectedValue { get; }

    /// <summary>Observes protection when the engine returns a value held after assignment.</summary>
    /// <remarks>The compatible default uses an ordinary session read; hosts may keep protection separate from read bases.</remarks>
    void ObservePendingRead(string name) { _ = Read(name); }

    /// <summary>
    /// Whether the variable is read-only to the script (a loop's item or index): the engine freezes it, so a change throws
    /// where the script makes it (ADR 0090, spec 1231 FR-006).
    /// </summary>
    bool IsReadOnly(string name);
}

/// <summary>The state of a script's outcome selection; mirrors the activity runtime's outcome selection.</summary>
public enum JavaScriptOutcomeSelectionState
{
    /// <summary>The script returned ordinary data, or <c>complete(result)</c> without outcomes: <c>Done</c>.</summary>
    Unspecified,

    /// <summary>The script returned <c>complete(result, names)</c> with one or more names.</summary>
    Named,

    /// <summary>The script returned <c>complete(result, [])</c>: no outgoing path.</summary>
    Empty
}

/// <summary>The result of one script evaluation: the activity's result and its outcome selection, on separate channels.</summary>
public sealed class JavaScriptScriptEvaluationResult
{
    public JavaScriptScriptEvaluationResult(
        JsonElement? value,
        JavaScriptOutcomeSelectionState selectionState = JavaScriptOutcomeSelectionState.Unspecified,
        IReadOnlyList<string>? outcomeNames = null)
    {
        if (!Enum.IsDefined(selectionState))
            throw new ArgumentOutOfRangeException(nameof(selectionState), selectionState, "The outcome selection state is undefined.");
        var names = outcomeNames?.ToArray() ?? [];
        if ((selectionState == JavaScriptOutcomeSelectionState.Named) != (names.Length > 0))
            throw new ArgumentException("Only a named selection carries outcome names, and it carries at least one.", nameof(outcomeNames));

        Value = value?.Clone();
        SelectionState = selectionState;
        OutcomeNames = Array.AsReadOnly(names);
    }

    /// <summary>The script's result; <see langword="null"/> when it returned nothing.</summary>
    public JsonElement? Value { get; }

    public JavaScriptOutcomeSelectionState SelectionState { get; }

    /// <summary>The selected names, distinct and in the order selected; empty unless the selection is named.</summary>
    public IReadOnlyList<string> OutcomeNames { get; }
}

/// <summary>The built-in names of the script-activity JavaScript surface.</summary>
public static class JavaScriptScriptBuiltIns
{
    /// <summary>
    /// The pure constructor of the returned completion form, <c>complete(result, outcomes)</c> (spec 1208 FR-007). It is
    /// installed for script activities only and is a reserved built-in name in every JavaScript surface (ADR 0045 §5).
    /// </summary>
    public const string Complete = "complete";
}

/// <summary>
/// A script returned an invalid completion value: a completion value nested in its returned data or result, or outcomes
/// that are not omitted, <c>undefined</c>, a non-blank string or an array of non-blank strings (FR-007a, FR-007b). The
/// activity faults with <see cref="FaultCode"/>.
/// </summary>
public sealed class JavaScriptCompletionException(string message) : InvalidOperationException(message)
{
    /// <summary>The fault code of an invalid outcome selection.</summary>
    public const string FaultCode = "VF-ACT-006";

    public string Code => FaultCode;
}
