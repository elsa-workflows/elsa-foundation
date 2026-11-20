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
                : throw new BackgroundActivityValidationException($"BG-ACT-004: Background result alias '{alias}' is not a registered concrete type."));
        if (!contract.Result.Policy.IsPersistable || contract.Result.Policy.HidesValue() ||
            contract.Result.Projections.Values.Any(projection => !projection.Policy.IsPersistable || projection.Policy.HidesValue()))
            throw new BackgroundActivityValidationException("BG-ACT-004: Background results require a persistable result policy that can cross the worker boundary without unimplemented encryption.");
        return type;
    }

    public string Serialize(ActivityContract contract, IActivityCompletionTransition completion, int maximumBytes)
    {
        ValidateOutcome(contract, completion.Selection);
        var type = ResolveResultType(contract);
        if (completion.Result is null && contract.Result.IsRequired)
            throw new BackgroundActivityValidationException("BG-ACT-005: The background activity did not return its required result.");
        if (completion.Result is not null && !type.IsInstanceOfType(completion.Result))
            throw new BackgroundActivityValidationException("BG-ACT-005: The background result does not match its pinned result type.");
        var json = JsonSerializer.Serialize(completion.Result, type, JsonOptions);
        ValidateSize(json, maximumBytes);
        return json;
    }

    public ActivityTransition Deserialize(ActivityContract contract, string json, OutcomeSelection selection, int maximumBytes)
    {
        ValidateOutcome(contract, selection);
        ValidateSize(json, maximumBytes);
        var type = ResolveResultType(contract);
        var result = JsonSerializer.Deserialize(json, type, JsonOptions);
        if (result is null && contract.Result.IsRequired)
            throw new BackgroundActivityValidationException("BG-ACT-005: The background receipt is missing its required result.");
        return new RehydratedCompletion(result, type, selection);
    }

    private static void ValidateOutcome(ActivityContract contract, OutcomeSelection selection)
    {
        // The same rules as every other completion (ADR 0045 §6): the receipt's selection is checked against the node's
        // pinned outcome set and multi-outcome flag before it is accepted.
        try
        {
            ActivityOutcomeSelectionRules.Validate(contract, selection);
        }
        catch (ActivityOutcomeSelectionException exception)
        {
            throw new BackgroundActivityValidationException($"BG-ACT-005: {ActivityOutcomeSelectionRules.FaultCode}: {exception.Message}");
        }
    }

    private static void ValidateSize(string json, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (Encoding.UTF8.GetByteCount(json) > maximumBytes)
            throw new BackgroundActivityValidationException("BG-ACT-006: The background result exceeds the configured receipt size limit.");
    }

    private sealed record RehydratedCompletion(object? Result, Type ResultType, OutcomeSelection Selection)
        : ActivityTransition, IActivityCompletionTransition
    {
        public override Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind Kind =>
            Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind.Complete;
    }
}
