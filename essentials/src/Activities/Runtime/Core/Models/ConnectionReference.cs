using System.Text.Json.Serialization;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// The only connection value that may appear in workflow JSON: a technical name and the connection
/// type it must resolve to. It carries no secret material.
/// </summary>
public sealed record ConnectionReference
{
    public ConnectionReference()
    {
    }

    public ConnectionReference(string name, string? type = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Type = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
    }

    /// <summary>The connection's immutable technical name.</summary>
    [JsonPropertyName("connection")]
    public string Name { get; init; } = "";

    /// <summary>The required connection type, for example <c>http</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    public override string ToString() => Type is { Length: > 0 } ? $"{Name} ({Type})" : Name;
}
