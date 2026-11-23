using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A successful completion whose commit a <c>Fault</c> lost update refused (spec 1221 FR-017): the outcome selection
/// and result, the attempt that produced it, and every workflow-variable write it would have made, resolved to the
/// values it would have written. An operator applies it with <c>AcceptRecordedCompletion/1</c> without running the body
/// again.
/// </summary>
public sealed record RecordedActivityCompletion
{
    [JsonConstructor]
    public RecordedActivityCompletion(
        ActivityCompletion completion,
        ActivityAttempt? attempt,
        IReadOnlyList<RecordedVariableWrite> variableWrites)
    {
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(variableWrites);
        Completion = completion;
        Attempt = attempt;
        VariableWrites = variableWrites;
    }

    /// <summary>The refused completion: result, outcome selection and the contract it was produced under.</summary>
    public ActivityCompletion Completion { get; }

    /// <summary>The attempt that produced the completion.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ActivityAttempt? Attempt { get; }

    /// <summary>Every workflow-variable write of the completion, in commit order, including the refused one.</summary>
    public IReadOnlyList<RecordedVariableWrite> VariableWrites { get; }
}

/// <summary>One workflow-variable write of a recorded completion: the variable and the value it would write.</summary>
public sealed record RecordedVariableWrite
{
    [JsonConstructor]
    public RecordedVariableWrite(string variableKey, ValueEnvelope value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        ArgumentNullException.ThrowIfNull(value);
        VariableKey = variableKey;
        Value = value;
    }

    public string VariableKey { get; }
    public ValueEnvelope Value { get; }
}
