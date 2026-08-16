using System.Text.Json;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;

/// <summary>JSON payload helpers for the distributed runtime's Groundwork v2 rows.</summary>
internal static class DistributedGroundworkDocuments
{
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    internal static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"The Groundwork payload for {typeof(T).Name} is invalid.");

    internal static T Deserialize<T>(IReadOnlyDictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value))
            throw new InvalidOperationException($"The Groundwork row is missing '{field}'.");

        var json = value switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            _ => throw new InvalidOperationException($"The Groundwork row field '{field}' is not JSON payload text.")
        };
        return Deserialize<T>(json);
    }
}
