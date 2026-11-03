using System.Text.Json.Serialization;

namespace Elsa.Integrations.Sdk.Manifest;

/// <summary>Packaging copy of one CLR activity's attribute metadata. Not a catalog row.</summary>
public sealed class ConnectorActivityEntry
{
    public required string TypeName { get; init; }

    public required string Version { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsTrigger { get; init; }

    public IReadOnlyList<ConnectorInputEntry> Inputs { get; init; } = [];
}
