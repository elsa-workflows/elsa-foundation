using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Behavioral.Infrastructure;

/// <summary>
/// Collects what a drive actually <em>observed</em> — outcomes emitted, outputs populated, required inputs that
/// faulted cleanly when absent — so the coverage assertions compare observation against the declared contract.
/// </summary>
/// <remarks>
/// Observations are read out of the persisted <see cref="ActivityExecutionState"/> rather than asserted by the
/// driver by hand. A driver cannot claim an outcome it did not reach: it hands the recorder the run and the node,
/// and the recorder reads the completion the engine actually committed.
/// </remarks>
public sealed class ActivityDriveRecorder
{
    private readonly ConcurrentDictionary<(Type Activity, string Outcome), byte> _outcomes = new();
    private readonly ConcurrentDictionary<(Type Activity, string Output), byte> _outputs = new();
    private readonly ConcurrentDictionary<(Type Activity, string Input), byte> _requiredInputFaults = new();
    private readonly ConcurrentDictionary<Type, byte> _driven = new();

    /// <summary>Activities that were driven through at least one real workflow execution.</summary>
    public bool WasDriven(Type activityType) => _driven.ContainsKey(activityType);

    public bool ObservedOutcome(Type activityType, string outcome) => _outcomes.ContainsKey((activityType, outcome));

    public bool ObservedOutput(Type activityType, string outputKey) => _outputs.ContainsKey((activityType, outputKey));

    public bool ObservedRequiredInputFault(Type activityType, string inputKey) =>
        _requiredInputFaults.ContainsKey((activityType, inputKey));

    public IReadOnlyCollection<string> ObservedOutcomes(Type activityType) => _outcomes.Keys
        .Where(key => key.Activity == activityType)
        .Select(key => key.Outcome)
        .OrderBy(outcome => outcome, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Records everything a completed node reveals: the outcome the engine committed and every declared output the
    /// committed result actually carries a non-null value for. Every execution state for the node is read, so a
    /// looping node contributes each pass.
    /// </summary>
    public void Record(Type activityType, WorkflowExecutionRun run, string executableNodeId)
    {
        _driven.TryAdd(activityType, 0);

        foreach (var state in run.States(executableNodeId))
        {
            foreach (var outcome in WorkflowExecutionRun.CompletionOutcomes(state))
                _outcomes.TryAdd((activityType, outcome), 0);

            if (state.Completion?.Result.InlineValue is { ValueKind: JsonValueKind.Object } result)
                foreach (var (key, path) in OutputPaths(activityType))
                    if (result.TryGetProperty(path, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                        _outputs.TryAdd((activityType, key), 0);
        }
    }

    /// <summary>
    /// Records that omitting a required input faulted rather than being silently tolerated, reading the outcome from
    /// committed state: either the activity/workflow ended Faulted, or the value-flow guard refused the start
    /// command before the activity ran.
    /// </summary>
    /// <param name="admissionRejection">
    /// The rejection thrown when the runtime refuses to admit the run at all. Requiredness is enforced at two
    /// different depths — <c>VF-ACT-004</c> rejects a null/absent binding at command admission, while an activity
    /// that validates in its own body faults mid-run — and both keep the contract. Only the rejection's mention of
    /// the input counts, so an unrelated failure cannot be mistaken for enforcement.
    /// </param>
    public void RecordRequiredInputFault(
        Type activityType,
        string inputKey,
        WorkflowExecutionRun? run,
        string executableNodeId,
        Exception? admissionRejection = null)
    {
        _driven.TryAdd(activityType, 0);

        if (admissionRejection is not null)
        {
            if (admissionRejection.ToString().Contains(inputKey, StringComparison.Ordinal))
                _requiredInputFaults.TryAdd((activityType, inputKey), 0);
            return;
        }

        if (run is null)
            return;

        var faulted = run.States(executableNodeId)
            .Any(state => state.Status == ActivityExecutionStatus.Faulted || state.Fault is not null);
        if (faulted || run.WorkflowState?.Status == WorkflowExecutionStatus.Faulted)
            _requiredInputFaults.TryAdd((activityType, inputKey), 0);
    }

    /// <summary>
    /// Records an outcome the engine committed on a node the caller identifies itself. Used only where the drive
    /// cannot go through a full workflow run — e.g. a per-node dynamic outcome port whose name is authored data.
    /// </summary>
    public void RecordOutcome(Type activityType, string outcome)
    {
        _driven.TryAdd(activityType, 0);
        _outcomes.TryAdd((activityType, outcome), 0);
    }

    /// <summary>Records that the activity ran, for a drive whose value is the run itself (no branch to assert).</summary>
    public void RecordDriven(Type activityType) => _driven.TryAdd(activityType, 0);

    // Output contract key → the camelCase JSON path the result projection writes it under, mirroring
    // WorkflowExecutionHarness.NewActivityContract's projection construction.
    private static IEnumerable<(string Key, string Path)> OutputPaths(Type activityType)
    {
        var resultType = activityType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .Distinct()
            .SingleOrDefault();

        if (resultType is null)
            yield break;

        foreach (var property in resultType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = property.GetCustomAttribute<OutputAttribute>(inherit: true);
            if (attribute is null)
                continue;

            yield return (attribute.Key ?? property.Name, attribute.Path ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name));
        }
    }
}
