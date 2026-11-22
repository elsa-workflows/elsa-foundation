using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Value-free accepted effect kinds retained on the existing activity state.</summary>
public static class StepWorkflowStateEffects
{
    public const string MetadataKey = "workflowStateEffects";

    /// <summary>Reads only recognized kinds; never returns output names or payloads from metadata.</summary>
    public static IReadOnlyCollection<string> Read(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(MetadataKey, out var json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Where(kind => kind is "outputs" or "correlationId" or "instanceName")
                .Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException) { return []; }
    }
}
