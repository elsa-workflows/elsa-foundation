using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// The mutable, latest-wins <c>definition.json</c> shape (ADR 0034 D5): the sole authority for a
/// definition's name/description/soft-delete. Kept separate from the immutable <c>versions/*.json</c>
/// content so a rename never changes any version's content identity. Shared by the source (read) and
/// the exporter (write).
/// </summary>
public sealed record GitDefinitionMetadata(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("deleted")] bool Deleted)
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Reads <c>definition.json</c>, or <c>null</c> when it is missing or malformed.</summary>
    public static GitDefinitionMetadata? Read(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GitDefinitionMetadata>(File.ReadAllText(path), ReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Serializes this metadata to an indented JSON string.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);
}
