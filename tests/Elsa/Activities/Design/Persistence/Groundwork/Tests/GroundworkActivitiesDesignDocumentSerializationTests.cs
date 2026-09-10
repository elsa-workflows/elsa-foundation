using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Activities.Design.Core.Models;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkActivitiesDesignDocumentSerializationTests
{
    [Fact]
    public void Create_clones_payload_options_and_wraps_payload_resolver()
    {
        var serializer = new TrackingPayloadSerializer();
        var options = GroundworkActivitiesDesignDocumentSerialization.Create(serializer);
        var json = JsonSerializer.Serialize(GraphProbe.Populated(), options);

        Assert.IsType<ExcludingJsonTypeInfoResolver>(options.TypeInfoResolver);
        Assert.NotSame(serializer.GetOptions(), options);
        Assert.Same(JavaScriptEncoder.UnsafeRelaxedJsonEscaping, options.Encoder);
        Assert.True(serializer.Resolver.Calls > 0);
        Assert.Contains(
            options.Converters,
            converter => converter is JsonConverterFactory factory && factory.CanConvert(typeof(IEnumerable<InputDefinition>)));
        AssertOmitted(json, "rowNumber", "descriptorPayloadSource", "inputsSource", "outputsSource", "designFacetsSource", "definition");
        AssertPresent(json, "\"kept\":\"visible\"", "\"stateSource\":\"shadow\"", "\"workflowDefinition\":\"wd\"");
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
        public string DescriptorPayloadSource { get; set; } = "";
        public string InputsSource { get; set; } = "";
        public string OutputsSource { get; set; } = "";
        public string DesignFacetsSource { get; set; } = "";
        public string Kept { get; set; } = "";

        public static GraphProbe Populated() => new()
        {
            RowNumber = 7,
            StateSource = "shadow",
            Definition = "nav",
            WorkflowDefinition = "wd",
            DescriptorPayloadSource = "dps",
            InputsSource = "ins",
            OutputsSource = "outs",
            DesignFacetsSource = "dfs",
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
