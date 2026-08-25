using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Provides the one current JSON envelope configuration for runtime rows.</summary>
internal static class GroundworkV2RuntimeJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string Serialize(object value) =>
        JsonSerializer.Serialize(value, value.GetType(), Options);

    public static T? Deserialize<T>(string content) => JsonSerializer.Deserialize<T>(content, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type != typeof(WorkflowExecutable))
                return;

            typeInfo.Properties.Remove(typeInfo.Properties.Single(property =>
                StringComparer.OrdinalIgnoreCase.Equals(property.Name, nameof(WorkflowExecutable.Nodes))));
            typeInfo.Properties.Remove(typeInfo.Properties.Single(property =>
                StringComparer.OrdinalIgnoreCase.Equals(property.Name, nameof(WorkflowExecutable.NodesById))));
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = resolver
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
