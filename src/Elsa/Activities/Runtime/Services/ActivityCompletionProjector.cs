using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Validates and materializes a successful transition as one completion document plus read-only
/// projections. Nothing is returned when any projection or outcome is invalid.
/// </summary>
public sealed class ActivityCompletionProjector(IExternalPayloadStore? externalPayloadStore = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public ActivityCompletionProjection Project(
        string invocationId,
        ActivityAttempt attempt,
        ActivityContract contract,
        ActivityTransition transition,
        DateTimeOffset completedAt) =>
        ProjectCoreAsync(null, invocationId, attempt, contract, transition, completedAt, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

    public ValueTask<ActivityCompletionProjection> ProjectAsync(
        string workflowExecutionId,
        string invocationId,
        ActivityAttempt attempt,
        ActivityContract contract,
        ActivityTransition transition,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        return ProjectCoreAsync(workflowExecutionId, invocationId, attempt, contract, transition, completedAt, cancellationToken);
    }

    private async ValueTask<ActivityCompletionProjection> ProjectCoreAsync(
        string? workflowExecutionId,
        string invocationId,
        ActivityAttempt attempt,
        ActivityContract contract,
        ActivityTransition transition,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
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
        var resultPolicy = contract.Result.Projections.Values.Aggregate(
            contract.Result.Policy,
            (policy, projection) => ValuePolicyCombiner.Combine(
                policy,
                projection.Policy,
                $"Result projection '{projection.Key}' on activity contract '{contract.ActivityTypeKey}'"));
        var selectedProjections = new Dictionary<string, (ActivityResultProjectionContract Contract, JsonElement? Value, ActivityValuePolicy EffectivePolicy)>(StringComparer.Ordinal);

        foreach (var projection in contract.Result.Projections.Values)
        {
            if (!projection.Policy.IsPersistable)
                throw new InvalidOperationException($"VF-ACT-005: Result projection '{projection.Key}' is not persistable.");

            var effectivePolicy = ValuePolicyCombiner.Combine(
                resultPolicy,
                projection.Policy,
                $"Result projection '{projection.Key}' on activity contract '{contract.ActivityTypeKey}'");
            if (!TrySelect(resultJson, projection.Path, out var selected))
            {
                if (projection.IsRequired)
                    throw new InvalidOperationException($"VF-ACT-006: Required result projection '{projection.Key}' was not produced.");

                selectedProjections.Add(projection.Key, (projection, null, effectivePolicy));
                continue;
            }

            if (projection.IsRequired && selected.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                throw new InvalidOperationException($"VF-ACT-006: Required result projection '{projection.Key}' was null.");

            selectedProjections.Add(projection.Key, (projection, selected.Clone(), effectivePolicy));
        }

        // Validate every projection before external writes so a malformed projection never exposes a
        // partially projected completion. External writes use deterministic owner keys and are idempotent.
        var resultEnvelope = await ToEnvelopeAsync(
            workflowExecutionId,
            $"activity:{invocationId}:result",
            contract.Result.Type,
            resultJson,
            resultPolicy,
            externalize: true,
            cancellationToken);
        var projections = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal);
        foreach (var (key, selected) in selectedProjections)
        {
            if (!selected.Value.HasValue)
            {
                projections.Add(key, ValueEnvelope.Absent(selected.Contract.Type, ValuePolicyCombiner.ToProtectionPolicy(selected.EffectivePolicy)));
                continue;
            }

            projections.Add(key, await ToEnvelopeAsync(
                workflowExecutionId,
                $"activity:{invocationId}:projection:{key}",
                selected.Contract.Type,
                selected.Value.Value,
                selected.EffectivePolicy,
                externalize: false,
                cancellationToken));
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

    private async ValueTask<ValueEnvelope> ToEnvelopeAsync(
        string? workflowExecutionId,
        string ownerKey,
        ValueTypeDescriptor type,
        JsonElement value,
        ActivityValuePolicy policy,
        bool externalize,
        CancellationToken cancellationToken)
    {
        var effectivePolicy = ValuePolicyCombiner.ToProtectionPolicy(policy);
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ValueEnvelope.Null(type, effectivePolicy);
        // Named projections are read-only views over the atomic result and are never independent
        // durable output slots. Keep those views local; only the owning whole result is externalized.
        if (effectivePolicy.Storage != DurableValueStorage.External || !externalize)
            return ValueEnvelope.Inline(type, value, effectivePolicy);
        if (workflowExecutionId is null)
            throw new InvalidOperationException("VF-ACT-005: External result persistence requires asynchronous projection with a workflow execution ID.");
        if (externalPayloadStore is null)
            throw new InvalidOperationException("VF-ACT-005: External result persistence requires an IExternalPayloadStore.");
        if (string.IsNullOrWhiteSpace(policy.StorageProfile))
            throw new InvalidOperationException("VF-ACT-005: External result policy requires a storage profile.");

        var reference = await externalPayloadStore.WriteAsync(
            new ExternalPayloadWriteRequest(
                workflowExecutionId,
                ownerKey,
                policy.StorageProfile,
                type,
                value.Clone(),
                effectivePolicy),
            cancellationToken);
        return ValueEnvelope.External(type, reference, effectivePolicy);
    }

    private static bool TrySelect(JsonElement root, string path, out JsonElement selected)
    {
        if (StringComparer.Ordinal.Equals(path, "$"))
        {
            selected = root;
            return true;
        }

        // Stable projection keys are opaque identifiers and may themselves contain dots. Prefer an
        // exact top-level property before interpreting the path as nested member navigation.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(path, out selected))
            return true;

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
