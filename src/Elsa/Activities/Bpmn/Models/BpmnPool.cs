using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>A BPMN pool. Visual/organizational in Phase 1; executable collaborations arrive later.</summary>
public sealed class BpmnPool
{
    [JsonConstructor]
    public BpmnPool(string poolId, string? name = null, bool isExecutable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poolId);

        PoolId = poolId;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        IsExecutable = isExecutable;
    }

    [JsonPropertyName("poolId")]
    public string PoolId { get; }

    [JsonPropertyName("name")]
    public string? Name { get; }

    [JsonPropertyName("isExecutable")]
    public bool IsExecutable { get; }
}
