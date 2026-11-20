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
