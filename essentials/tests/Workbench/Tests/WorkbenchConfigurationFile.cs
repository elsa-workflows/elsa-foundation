using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Elsa.Workbench.Tests;

internal static class WorkbenchConfigurationFile
{
    public static void WriteOpenIddictSigningKey(string path, string value) =>
        WriteValue(path, ["CShells", "Shells", "default", "Features", "FoundationIdentityOpenIddict", "SigningKey"], value);

    /// <summary>Writes <paramref name="value"/> at <paramref name="segments"/>; a <see langword="null"/> value writes JSON <c>null</c>.</summary>
    public static void WriteValue(string path, IReadOnlyList<string> segments, object? value) =>
        Update(path, segments, (parent, key) => parent[key] = JsonValue.Create(value));

    public static void RemoveValue(string path, IReadOnlyList<string> segments) =>
        Update(path, segments, (parent, key) => parent.Remove(key));

    public static async Task WaitForBlueprintValueAsync(WorkbenchProcess host, string key, string value)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        const string blueprint = "/_admin/shells/default/blueprint";

        while (true)
        {
            var snapshot = await host.ManagementClient.GetFromJsonAsync<JsonObject>(blueprint, deadline.Token);
            if (string.Equals(snapshot?["configurationData"]?[key]?.ToString(), value, StringComparison.Ordinal))
                return;

            await Task.Delay(100, deadline.Token);
        }
    }

    private static void Update(string path, IReadOnlyList<string> segments, Action<JsonObject, string> update)
    {
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
        root ??= new JsonObject();
        var current = root;
        foreach (var segment in segments.Take(segments.Count - 1))
        {
            current[segment] ??= new JsonObject();
            current = current[segment]!.AsObject();
        }

        update(current, segments[^1]);
        var temporary = path + ".candidate";
        File.WriteAllText(temporary, root.ToJsonString());
        File.Move(temporary, path, overwrite: true);
    }
}
