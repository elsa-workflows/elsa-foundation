using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class RuntimeArtifactJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("The persisted Runtime JSON payload was empty.");
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(WorkflowExecutable)) return;
            info.Properties.Remove(info.Properties.Single(x => x.Name.Equals(nameof(WorkflowExecutable.Nodes), StringComparison.OrdinalIgnoreCase)));
            info.Properties.Remove(info.Properties.Single(x => x.Name.Equals(nameof(WorkflowExecutable.NodesById), StringComparison.OrdinalIgnoreCase)));
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
