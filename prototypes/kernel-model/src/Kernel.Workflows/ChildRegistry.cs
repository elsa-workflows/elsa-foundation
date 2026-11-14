using System.Text.Json;
using Kernel.Durable;

namespace Kernel.Workflows;

internal static class ChildRegistry
{
    internal static List<CreationAddress> Read(string? json)
    {
        if (json is null) return [];
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
            ? new CreationAddress(item.GetString()!, 0) : item.Deserialize<CreationAddress>()!).ToList();
    }

    internal static string? Write(List<CreationAddress> children) => children.Count == 0 ? null
        : children.All(c => c.Creation == 0) ? Json.Write(children.Select(c => c.Id).ToList()) : Json.Write(children);
}
