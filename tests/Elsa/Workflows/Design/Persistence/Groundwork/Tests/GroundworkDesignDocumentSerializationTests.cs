using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkDesignDocumentSerializationTests
{
    [Fact]
    public void Create_starts_from_web_defaults_and_ignores_payload_resolver()
    {
        var serializer = new TrackingPayloadSerializer();
        var options = GroundworkDesignDocumentSerialization.Create(serializer);
        var json = JsonSerializer.Serialize(GraphProbe.Populated(), options);

        Assert.IsType<ExcludingJsonTypeInfoResolver>(options.TypeInfoResolver);
        Assert.Equal(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.NotSame(serializer.GetOptions(), options);
        Assert.NotSame(JavaScriptEncoder.UnsafeRelaxedJsonEscaping, options.Encoder);
        Assert.Equal(0, serializer.Resolver.Calls);
        Assert.Contains(
            options.Converters,
            converter => converter is JsonConverterFactory factory && factory.CanConvert(typeof(WorkflowDefinitionState)));
        AssertOmitted(json, "rowNumber", "stateSource", "definition", "workflowDefinition", "workflowDefinitionVersion", "workflowDefinitionDraft");
        AssertPresent(json, "\"kept\":\"visible\"", "\"descriptorPayloadSource\":\"dps\"", "\"inputsSource\":\"ins\"");
    }

    private static void AssertOmitted(string json, params string[] names)
    {
        foreach (var name in names)
            Assert.DoesNotContain($"\"{name}\"", json, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertPresent(string json, params string[] fragments)
    {
        foreach (var fragment in fragments)
            Assert.Contains(fragment, json, StringComparison.Ordinal);
    }

    private sealed class GraphProbe
    {
        public long RowNumber { get; set; }
        public string StateSource { get; set; } = "";
        public string Definition { get; set; } = "";
        public string WorkflowDefinition { get; set; } = "";
        public string WorkflowDefinitionVersion { get; set; } = "";
        public string WorkflowDefinitionDraft { get; set; } = "";
        public string DescriptorPayloadSource { get; set; } = "";
        public string InputsSource { get; set; } = "";
        public string Kept { get; set; } = "";

        public static GraphProbe Populated() => new()
        {
            RowNumber = 7,
            StateSource = "shadow",
            Definition = "nav",
            WorkflowDefinition = "wd",
            WorkflowDefinitionVersion = "wdv",
            WorkflowDefinitionDraft = "wdd",
            DescriptorPayloadSource = "dps",
            InputsSource = "ins",
            Kept = "visible"
        };
    }

    private sealed class TrackingResolver : IJsonTypeInfoResolver
    {
        private readonly IJsonTypeInfoResolver inner = new DefaultJsonTypeInfoResolver();

        public int Calls { get; private set; }

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            Calls++;
            return inner.GetTypeInfo(type, options);
        }
    }

    private sealed class TrackingPayloadSerializer : IPayloadSerializer
    {
        private readonly JsonSerializerOptions options;

        public TrackingPayloadSerializer()
        {
            Resolver = new TrackingResolver();
            options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = Resolver,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        public TrackingResolver Resolver { get; }

        public string Serialize(object payload) => throw new NotSupportedException();

        public JsonElement SerializeToElement(object payload) => throw new NotSupportedException();

        public object Deserialize(string serializedData) => throw new NotSupportedException();

        public object Deserialize(string serializedData, Type type) => throw new NotSupportedException();

        public object Deserialize(JsonElement serializedData) => throw new NotSupportedException();

        public T Deserialize<T>(string serializedData) => throw new NotSupportedException();

        public T Deserialize<T>(JsonElement serializedData) => throw new NotSupportedException();

        public JsonSerializerOptions GetOptions() => options;
    }
}
