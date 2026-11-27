using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>
/// The private structural state of one <c>BulkDispatchWorkflows</c> execution (spec 1242 plan §2): the item count, the
/// next item index to schedule and the indexes whose passes have ended. It commits with each structural evaluation, so
/// a repeated callback for an ended pass cannot schedule a further item and exceed the window. The items themselves are
/// not held; they are re-read from the committed <c>Items</c> input.
/// </summary>
internal sealed record BulkDispatchWorkflowsState(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("next")] int Next,
    [property: JsonPropertyName("ended")] IReadOnlyList<int> Ended)
{
    public const string TypeAlias = "Elsa.BulkDispatchWorkflows.State";
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(Elsa.Serialization.Core.JsonBaseline.Options);

    public int Open => Next - Ended.Count;

    public bool IsEnded(int index) => Ended.Contains(index);

    public bool AllEnded => Ended.Count >= Count;

    /// <summary>Marks <paramref name="index"/> ended, keeping the list sorted.</summary>
    public BulkDispatchWorkflowsState End(int index)
    {
        var ended = Ended.Append(index).Distinct().Order().ToArray();
        return this with { Ended = ended };
    }

    public static BulkDispatchWorkflowsState Load(ActivityExecutionState state)
    {
        if (state.PrivateState is not { } privateState ||
            privateState.StateVersion != SchemaVersion ||
            !StringComparer.Ordinal.Equals(privateState.Value.Type.Alias, TypeAlias) ||
            privateState.Value.InlineValue is not { } payload)
        {
            throw new InvalidOperationException("BulkDispatchWorkflows private state is missing or does not match the required type and schema version.");
        }

        try
        {
            return payload.Deserialize<BulkDispatchWorkflowsState>(SerializerOptions)
                   ?? throw new InvalidOperationException("BulkDispatchWorkflows private state resolved to null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidOperationException("BulkDispatchWorkflows private state is invalid.", exception);
        }
    }

    public RuntimeStructuralContinuation Stage(RuntimeStructuralContinuation continuation) =>
        continuation.WithState(
            ValueEnvelope.Inline(
                new ValueTypeDescriptor(TypeAlias, schemaVersion: SchemaVersion),
                JsonSerializer.SerializeToElement(this, SerializerOptions),
                ValueProtectionPolicy.InstanceInline),
            SchemaVersion);
}
