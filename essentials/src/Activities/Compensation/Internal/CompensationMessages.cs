using System.Text.Json;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// The payloads of the ancestor notifications a compensation scope receives (spec 258 data model). Each carries
/// identities and keys only, never forward values: those stay in the run's value store and resolve when a handler runs.
/// </summary>
internal static class CompensationMessages
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static JsonElement Serialize<T>(T message) => JsonSerializer.SerializeToElement(message, SerializerOptions);

    public static T? Deserialize<T>(JsonElement? payload) where T : class
    {
        if (payload is not { } value)
            return null;

        try
        {
            return value.Deserialize<T>(SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>One batch of a completing inner scope's eligible entries (<c>compensation.register</c>).</summary>
internal sealed record CompensationHandOver(string OriginScope, int Batch, int BatchCount, IReadOnlyList<CompensationHandedOverEntry> Entries);

/// <summary>One handed-over entry: what the receiving scope needs to journal it and, later, run its handler.</summary>
internal sealed record CompensationHandedOverEntry(
    string OriginEntryId,
    string Step,
    string OperationKey,
    string ForwardActivityExecutionId,
    string? ForwardIterationId,
    CompensationForwardOutcome ForwardOutcome,
    CompensationEntryStatus Status,
    string? HandlerExecutableNodeId,
    CompensationEntryOrigin? Origin);

/// <summary>A Confirm activity's request (<c>compensation.confirm</c>).</summary>
internal sealed record CompensationConfirmRequest(string ConfirmingActivityExecutionId);

/// <summary>A Compensate activity's request (<c>compensation.compensate</c>); <see cref="Target"/> names an outer scope.</summary>
internal sealed record CompensationCompensateRequest(string RequestingActivityExecutionId, string? Reason, string? Target);
