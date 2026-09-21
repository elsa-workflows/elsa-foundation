using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Loads and stages the <see cref="BpmnExecutionState"/> blob as one typed, versioned structural
/// private-state document, mirroring <c>FlowchartStatePersister</c>: web serializer defaults, ordinal
/// enum persistence, and prune-on-save so a long-running process never re-serializes an ever-growing
/// blob.
/// </summary>
public sealed class BpmnStatePersister
{
    /// <summary>Maximum diagnostics retained in the persisted state blob; oldest are dropped first.</summary>
    private const int DiagnosticsCap = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static BpmnExecutionState CreateInitialState() => new();

    public static BpmnExecutionState? LoadState(ActivityExecutionState activityExecutionState)
    {
        if (activityExecutionState.PrivateState is not { } privateState)
            return null;

        if (privateState.StateVersion != BpmnExecutionEngine.StateSchemaVersion ||
            !StringComparer.Ordinal.Equals(privateState.Value.Type.Alias, BpmnExecutionEngine.StateTypeAlias) ||
            privateState.Value.InlineValue is not { } payload)
        {
            throw new BpmnExecutionException("BPMN private state does not match the required type and schema version.");
        }

        try
        {
            return payload.Deserialize<BpmnExecutionState>(SerializerOptions)
                   ?? throw new BpmnExecutionException("BPMN private state resolved to null.");
        }
        catch (BpmnExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new BpmnExecutionException("BPMN private state is invalid.", exception);
        }
    }

    /// <summary>
    /// Trims records that can never influence a future engine decision before the state is persisted:
    /// consumed tokens not referenced by an active child, and diagnostics beyond the cap.
    /// <see cref="BpmnTokenStatus.Canceled"/> tokens are <b>never</b> pruned: terminate strips
    /// in-flight children from <c>ActiveChildren</c> while their activities may still complete, and the
    /// late completion is absorbed via the by-id token lookup ("ignored completion for canceled
    /// token") — prune the record and that lookup faults the process (the same reasoning as the
    /// Flowchart persister's Canceled/Faulted path retention). Canceled counts are bounded by graph
    /// structure. Pruning shapes only what is written — the persisted schema is unchanged and
    /// <see cref="BpmnExecutionState.Sequence"/> is not bumped.
    /// </summary>
    private static BpmnExecutionState PruneForPersistence(BpmnExecutionState state)
    {
        var retainedTokenIds = state.ActiveChildren.Select(child => child.TokenId).ToHashSet(StringComparer.Ordinal);
        var tokens = state.Tokens
            .Where(token => token.Status != BpmnTokenStatus.Consumed || retainedTokenIds.Contains(token.TokenId))
            .ToArray();

        var diagnostics = state.Diagnostics.Count <= DiagnosticsCap
            ? state.Diagnostics
            : state.Diagnostics.Skip(state.Diagnostics.Count - DiagnosticsCap).ToArray();

        return state with { Tokens = tokens, Diagnostics = diagnostics };
    }

    public RuntimeStructuralContinuation StageState(RuntimeStructuralContinuation continuation, BpmnExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(state);

        var value = ValueEnvelope.Inline(
            new Elsa.Primitives.Models.ValueTypeDescriptor(
                BpmnExecutionEngine.StateTypeAlias,
                schemaVersion: BpmnExecutionEngine.StateSchemaVersion),
            JsonSerializer.SerializeToElement(PruneForPersistence(state), SerializerOptions),
            ValueProtectionPolicy.InstanceInline);

        return continuation.WithState(value, BpmnExecutionEngine.StateSchemaVersion);
    }
}
