using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Validates and materializes a successful transition as one completion document plus read-only
/// projections. Nothing is returned when any projection or outcome is invalid.
/// </summary>
public sealed class ActivityCompletionProjector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public ActivityCompletionProjection Project(
        string invocationId,
        ActivityAttempt attempt,
        ActivityContract contract,
        ActivityTransition transition,
        DateTimeOffset completedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(transition);

        if (transition is not IActivityCompletionTransition completion)
            throw new InvalidOperationException($"Transition '{transition.Kind}' cannot be projected as an activity completion.");
        if (!contract.Outcomes.Contains(completion.Outcome, StringComparer.Ordinal))
            throw new InvalidOperationException($"VF-ACT-006: Outcome '{completion.Outcome}' is not declared by activity contract '{contract.ActivityTypeKey}'.");
        if (!contract.Result.Policy.IsPersistable)
            throw new InvalidOperationException($"VF-ACT-005: Result for activity contract '{contract.ActivityTypeKey}' is not persistable.");
        if (contract.Result.IsRequired && completion.Result is null)
            throw new InvalidOperationException($"VF-ACT-006: Activity contract '{contract.ActivityTypeKey}' requires a result.");

        var resultJson = JsonSerializer.SerializeToElement(completion.Result, completion.ResultType, SerializerOptions);
        var resultEnvelope = ToEnvelope(contract.Result.Type, resultJson, contract.Result.Policy);
        var projections = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal);

        foreach (var projection in contract.Result.Projections.Values)
        {
            if (!projection.Policy.IsPersistable)
                throw new InvalidOperationException($"VF-ACT-005: Result projection '{projection.Key}' is not persistable.");

            if (!TrySelect(resultJson, projection.Path, out var selected))
            {
                if (projection.IsRequired)
                    throw new InvalidOperationException($"VF-ACT-006: Required result projection '{projection.Key}' was not produced.");

                projections.Add(projection.Key, ValueEnvelope.Absent(projection.Type, ToProtectionPolicy(projection.Policy)));
                continue;
            }

            if (projection.IsRequired && selected.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                throw new InvalidOperationException($"VF-ACT-006: Required result projection '{projection.Key}' was null.");

            projections.Add(projection.Key, ToEnvelope(projection.Type, selected, projection.Policy));
        }

        var durableCompletion = new ActivityCompletion(
            invocationId,
            attempt.AttemptId,
            resultEnvelope,
            completion.Outcome,
            completedAt,
            contract.SchemaFingerprint);
        return new ActivityCompletionProjection(durableCompletion, projections);
    }

    private static ValueEnvelope ToEnvelope(ValueTypeDescriptor type, JsonElement value, ActivityValuePolicy policy) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? ValueEnvelope.Null(type, ToProtectionPolicy(policy))
            : ValueEnvelope.Inline(type, value, ToProtectionPolicy(policy));

    private static ValueProtectionPolicy ToProtectionPolicy(ActivityValuePolicy policy) =>
        policy.IsPersistable
            ? new ValueProtectionPolicy(
                DurableValueLifecycle.Instance,
                DurableValueStorage.Inline,
                policy.IsSensitive,
                policy.RequiresEncryption,
                policy.RedactionMode)
            : ValueProtectionPolicy.Transient;

    private static bool TrySelect(JsonElement root, string path, out JsonElement selected)
    {
        selected = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (selected.ValueKind != JsonValueKind.Object || !selected.TryGetProperty(segment, out selected))
                return false;
        }

        return true;
    }
}

public sealed record ActivityCompletionProjection(
    ActivityCompletion Completion,
    IReadOnlyDictionary<string, ValueEnvelope> Projections);
