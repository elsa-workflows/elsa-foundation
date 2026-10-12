using System.Text.Json;

namespace Elsa.Cli.Worker;

/// <summary>
/// A conservative, value-free check for resource keys when the selected host predates the context API.
/// The worker cannot compose that host, so key presence only prevents an unsafe legacy downgrade.
/// </summary>
internal static class HostResourceHints
{
    private static readonly JsonDocumentOptions Json = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    internal static bool Exist(string hostDirectory, string environment)
    {
        foreach (var name in new[]
                 {
                     "appsettings.json", $"appsettings.{environment}.json",
                     "shells.json", $"shells.{environment}.json"
                 })
        {
            var path = Path.Join(hostDirectory, name);
            if (!File.Exists(path))
                continue;
            try
            {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream, Json);
                if (ContainsHint(document.RootElement, []))
                    return true;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
            {
                throw WorkerRefusal.Resolution("configuration-context-invalid",
                    "The selected host configuration could not be inspected for persistence resource keys.");
            }
        }
        return false;
    }

    private static bool ContainsHint(JsonElement value, IReadOnlyList<string> path)
    {
        if (IsHint(path))
            return true;
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select((item, index) => (item, index))
                .Any(entry => ContainsHint(entry.item, [.. path, entry.index.ToString()]));
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        return value.EnumerateObject().Any(property =>
            ContainsHint(property.Value, [.. path, .. property.Name.Split(':', StringSplitOptions.RemoveEmptyEntries)]));
    }

    private static bool IsHint(IReadOnlyList<string> path)
    {
        if (StartsWith(path, "Elsa", "Persistence") && path.Count >= 3 &&
            Is(path[2], "Resources", "DefaultResource"))
            return true;
        return path.Count >= 7 && StartsWith(path, "CShells", "Shells") &&
               Is(path[3], "Configuration") && Is(path[4], "Elsa") &&
               Is(path[5], "Persistence") && Is(path[6], "DefaultResource", "Bindings");
    }

    private static bool StartsWith(IReadOnlyList<string> path, string first, string second) =>
        path.Count >= 2 && Is(path[0], first) && Is(path[1], second);

    private static bool Is(string value, params string[] candidates) =>
        candidates.Any(candidate => StringComparer.OrdinalIgnoreCase.Equals(value, candidate));
}
