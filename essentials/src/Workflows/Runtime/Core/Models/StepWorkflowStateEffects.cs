using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Value-free accepted effect kinds retained on the existing activity state.</summary>
public static class StepWorkflowStateEffects
{
    // This closed metadata protocol was retained with PascalCase before S-9. Its own format stays
    // compatible while public API contexts continue to use the ordinary camelCase baseline.
    private static readonly JsonSerializerOptions ReceiptJson = CreateReceiptJson();
    public const string MetadataKey = "workflowStateEffects";
    public const string ReceiptsMetadataKey = "workflowStateEffectReceipts";

    /// <summary>Accepted evaluation receipts retained without attempted values, output names or payload history.</summary>
    public static IReadOnlyCollection<StepWorkflowStateEffectReceipt> ReadReceipts(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(ReceiptsMetadataKey, out var json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<StepWorkflowStateEffectReceipt[]>(json, ReceiptJson) ?? [])
                .Where(receipt => receipt is { Kinds: not null } && receipt.Transition is "Completion" or "Suspension")
                .Select(receipt => receipt with { Kinds = receipt.Kinds.Where(Known).Distinct(StringComparer.Ordinal).ToArray() }).ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static bool Known(string kind) => kind is "outputs" or "correlationId" or "instanceName";

    /// <summary>Writes the retained, value-free receipt protocol; this profile never configures API serialization.</summary>
    public static string WriteReceipts(IEnumerable<StepWorkflowStateEffectReceipt> receipts) => JsonSerializer.Serialize(receipts, ReceiptJson);

    private static JsonSerializerOptions CreateReceiptJson()
    {
        var options = JsonBaseline.CreateCopy();
        options.PropertyNamingPolicy = null;
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Reads only recognized kinds; never returns output names or payloads from metadata.</summary>
    public static IReadOnlyCollection<string> Read(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(MetadataKey, out var json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json, JsonBaseline.Options) ?? [])
                .Where(Known)
                .Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException) { return []; }
    }
}

/// <summary>Value-free attribution to the accepted evaluation, retained across later failed or cancelled evaluations.</summary>
public sealed record StepWorkflowStateEffectReceipt(string? AttemptId, DateTimeOffset AcceptedAt, string Transition, IReadOnlyCollection<string> Kinds);
