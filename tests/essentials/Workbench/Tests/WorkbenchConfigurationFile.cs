using System.Text.Json.Nodes;

namespace Elsa.Workbench.Tests;

internal static class WorkbenchConfigurationFile
{
    public static void WriteOpenIddictSigningKey(string path, string value) =>
        WriteValue(path, ["CShells", "Shells", "default", "Features", "FoundationIdentityOpenIddict", "SigningKey"], value);

    public static void WriteValue(string path, IReadOnlyList<string> segments, object value)
    {
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
        root ??= new JsonObject();
        var current = root;
        foreach (var segment in segments.Take(segments.Count - 1))
        {
            current[segment] ??= new JsonObject();
            current = current[segment]!.AsObject();
        }

        current[segments[^1]] = JsonValue.Create(value);
        var temporary = path + ".candidate";
        File.WriteAllText(temporary, root.ToJsonString());
        File.Move(temporary, path, overwrite: true);
    }
}
