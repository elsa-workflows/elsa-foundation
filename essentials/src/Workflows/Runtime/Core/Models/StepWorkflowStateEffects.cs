using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Value-free accepted effect kinds retained on the existing activity state.</summary>
public static class StepWorkflowStateEffects
{
    public const string MetadataKey = "workflowStateEffects";
    public const string ReceiptsMetadataKey = "workflowStateEffectReceipts";

    /// <summary>Accepted evaluation receipts retained without attempted values, output names or payload history.</summary>
    public static IReadOnlyCollection<StepWorkflowStateEffectReceipt> ReadReceipts(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(ReceiptsMetadataKey, out var json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<StepWorkflowStateEffectReceipt[]>(json) ?? [])
                .Where(receipt => receipt is { Kinds: not null } && receipt.Transition is "Completion" or "Suspension")
                .Select(receipt => receipt with { Kinds = receipt.Kinds.Where(Known).Distinct(StringComparer.Ordinal).ToArray() }).ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static bool Known(string kind) => kind is "outputs" or "correlationId" or "instanceName";

    /// <summary>Reads only recognized kinds; never returns output names or payloads from metadata.</summary>
    public static IReadOnlyCollection<string> Read(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(MetadataKey, out var json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Where(Known)
                .Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException) { return []; }
    }
}

/// <summary>Value-free attribution to the accepted evaluation, retained across later failed or cancelled evaluations.</summary>
public sealed record StepWorkflowStateEffectReceipt(string? AttemptId, DateTimeOffset AcceptedAt, string Transition, IReadOnlyCollection<string> Kinds);
