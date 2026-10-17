using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Crosses the worker boundary using the pinned result type. The sender cannot select a CLR type,
/// and the coordinator does not activate the activity again to reconstruct its completion.
/// </summary>
internal sealed class BackgroundActivityCompletionCodec(IWellKnownTypeRegistry types)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Type ResolveResultType(ActivityContract contract)
    {
        var descriptor = contract.Result.Type;
        var type = TypeReferenceFactory.Resolve(descriptor.ToTypeReference(), alias =>
            types.TryGetTypeOrDefault(alias, out var resolved) && resolved != typeof(object)
                ? resolved
                : throw new InvalidOperationException($"BG-ACT-004: Background result alias '{alias}' is not a registered concrete type."));
        if (!contract.Result.Policy.IsPersistable || contract.Result.Policy.HidesValue() ||
            contract.Result.Projections.Values.Any(projection => !projection.Policy.IsPersistable || projection.Policy.HidesValue()))
            throw new InvalidOperationException("BG-ACT-004: Background results require a persistable result policy that can cross the worker boundary without unimplemented encryption.");
        return type;
    }

    public string Serialize(ActivityContract contract, IActivityCompletionTransition completion, int maximumBytes)
    {
        ValidateOutcome(contract, completion.Outcome);
        var type = ResolveResultType(contract);
        if (completion.Result is null && contract.Result.IsRequired)
            throw new InvalidOperationException("BG-ACT-005: The background activity did not return its required result.");
        if (completion.Result is not null && !type.IsInstanceOfType(completion.Result))
            throw new InvalidOperationException("BG-ACT-005: The background result does not match its pinned result type.");
        var json = JsonSerializer.Serialize(completion.Result, type, JsonOptions);
        ValidateSize(json, maximumBytes);
        return json;
    }

    public ActivityTransition Deserialize(ActivityContract contract, string json, string outcome, int maximumBytes)
    {
        ValidateOutcome(contract, outcome);
        ValidateSize(json, maximumBytes);
        var type = ResolveResultType(contract);
        var result = JsonSerializer.Deserialize(json, type, JsonOptions);
        if (result is null && contract.Result.IsRequired)
            throw new InvalidOperationException("BG-ACT-005: The background receipt is missing its required result.");
        return new RehydratedCompletion(result, type, outcome);
    }

    private static void ValidateOutcome(ActivityContract contract, string outcome)
    {
        if (!contract.Outcomes.Contains(outcome, StringComparer.Ordinal))
            throw new InvalidOperationException($"BG-ACT-005: Outcome '{outcome}' is not declared by the pinned background activity contract.");
    }

    private static void ValidateSize(string json, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (Encoding.UTF8.GetByteCount(json) > maximumBytes)
            throw new InvalidOperationException("BG-ACT-006: The background result exceeds the configured receipt size limit.");
    }

    private sealed record RehydratedCompletion(object? Result, Type ResultType, string Outcome)
        : ActivityTransition, IActivityCompletionTransition
    {
        public override Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind Kind =>
            Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind.Complete;
    }
}
