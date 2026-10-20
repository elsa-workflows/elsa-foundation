using System.Text.Json;
using Elsa.Activities.Compensation.Exceptions;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Loads and stages the <see cref="CompensationScopeState"/> blob. The runtime folds staged state into the same
/// checkpoint as the structural continuation and its child schedule requests; this type never commits state itself.
/// </summary>
internal static class CompensationScopeStatePersister
{
    public const string StateTypeAlias = "Elsa.CompensationScope.State";
    public const int StateSchemaVersion = 4;
    private const int Version1 = 1;
    private const int Version2 = 2;
    private const int Version3 = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static CompensationScopeState? Load(ActivityExecutionState activityExecutionState)
    {
        if (activityExecutionState.PrivateState is not { } privateState)
            return null;

        if (privateState.StateVersion is not (StateSchemaVersion or Version3 or Version2 or Version1) ||
            !StringComparer.Ordinal.Equals(privateState.Value.Type.Alias, StateTypeAlias) ||
            privateState.Value.InlineValue is not { } payload)
        {
            throw new CompensationScopeExecutionException("Compensation scope private state does not match the required type and schema version.");
        }

        // A scope that started before M2 (version 1), M3 (version 2) or M4 (version 3) is upgraded here and written as
        // the current version on its next commit. Upgrades chain, so a version-1 scope passes through versions 2 and 3.
        var current = privateState.StateVersion switch
        {
            Version1 => CompensationScopeStateUpcaster.FromVersion3(CompensationScopeStateUpcaster.FromVersion2(CompensationScopeStateUpcaster.FromVersion1(payload))),
            Version2 => CompensationScopeStateUpcaster.FromVersion3(CompensationScopeStateUpcaster.FromVersion2(payload)),
            Version3 => CompensationScopeStateUpcaster.FromVersion3(payload),
            _ => payload
        };
        return Deserialize(current);
    }

    public static CompensationScopeState Deserialize(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<CompensationScopeState>(SerializerOptions)
                   ?? throw new CompensationScopeExecutionException("Compensation scope private state resolved to null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new CompensationScopeExecutionException("Compensation scope private state is invalid.", exception);
        }
    }

    public static RuntimeStructuralContinuation Stage(RuntimeStructuralContinuation continuation, CompensationScopeState state)
    {
        var value = ValueEnvelope.Inline(
            new ValueTypeDescriptor(StateTypeAlias, schemaVersion: StateSchemaVersion),
            JsonSerializer.SerializeToElement(state, SerializerOptions),
            ValueProtectionPolicy.InstanceInline);

        return continuation.WithState(value, StateSchemaVersion);
    }
}
