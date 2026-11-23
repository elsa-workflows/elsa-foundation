using System.Text.Json.Serialization;

namespace Elsa.Integrations.Sdk.Manifest;

/// <summary>Packaging copy of one <c>[ActivityInput]</c>. Not a catalog row.</summary>
public sealed class ConnectorInputEntry
{
    public required string Key { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>Camel-case <c>essential</c>, <c>optional</c>, or <c>setting</c>; null when unset.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tier { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCredential { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsSensitive { get; init; }
}
