using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class RuntimeArtifactJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, JsonSerializerOptions> BoundedOptions = new();
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string value) => Deserialize<T>(value, Options);
    public static T DeserializeBounded<T>(string value, int maximumDepth)
    {
        var options = BoundedOptions.GetOrAdd(maximumDepth, depth =>
        {
            var bounded = CreateOptions(bounded: true); bounded.MaxDepth = depth; bounded.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            bounded.PropertyNameCaseInsensitive = false; // Current owner producer spelling; case aliases cannot overwrite a known field.
            return bounded;
        });
        var result = Deserialize<T>(value, options);
        // Current source profile must preserve every stored field. Constructor defaults, computed aliases and
        // normalized collections cannot silently replace a persisted source body with a different export.
        using var output = new BoundedSnapshotStream(Encoding.UTF8.GetByteCount(value));
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { MaxDepth = maximumDepth }))
        { JsonSerializer.Serialize(writer, result, options); writer.Flush(); }
        using var original = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = maximumDepth });
        using var reconstructed = JsonDocument.Parse(output.GetBuffer().AsMemory(0, checked((int)output.Length)), new JsonDocumentOptions { MaxDepth = maximumDepth });
        if (ActivityTemplateHasher.ComputeCanonicalValueHash(original.RootElement) != ActivityTemplateHasher.ComputeCanonicalValueHash(reconstructed.RootElement))
            throw new InvalidDataException("The persisted Runtime JSON payload is not lossless in the bounded current-schema profile.");
        return result;
    }

    private static T Deserialize<T>(string value, JsonSerializerOptions options)
    {
        if (typeof(T) != typeof(DurableValueState))
            return JsonSerializer.Deserialize<T>(value, options) ?? throw new InvalidDataException("The persisted Runtime JSON payload was empty.");

        // JsonElement? treats a JSON null token as nullable absence. Inline state instead
        // requires a present value, including JSON null; an omitted member must still fail.
        // Read only this state's member as JsonElement so Undefined retains that distinction.
        var payload = JsonSerializer.Deserialize<DurableValueStatePayload>(value, options)
            ?? throw new InvalidDataException("The persisted Runtime JSON payload was empty.");
        JsonElement? inlineValue = payload.InlineValue.ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.Null when payload.Storage != DurableValueStorage.Inline => null,
            _ => payload.InlineValue
        };
        var state = new DurableValueState(
            payload.DurableValueId, payload.WorkflowExecutionId, payload.ValueId, payload.Type,
            payload.Lifecycle, payload.Storage, inlineValue, payload.ExternalReference,
            payload.SourceActivityExecutionId, payload.CapturedAt, payload.Metadata);
        return (T)(object)state;
    }

    private sealed record DurableValueStatePayload(
        string DurableValueId,
        string WorkflowExecutionId,
        string ValueId,
        RuntimeValueTypeDescriptor Type,
        DurableValueLifecycle Lifecycle,
        DurableValueStorage Storage,
        JsonElement InlineValue,
        DurableValueExternalReference? ExternalReference,
        string? SourceActivityExecutionId,
        DateTimeOffset CapturedAt,
        IReadOnlyDictionary<string, string> Metadata);

    private static JsonSerializerOptions CreateOptions(bool bounded = false)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(WorkflowExecutable))
            {
                info.Properties.Remove(info.Properties.Single(x => x.Name.Equals(nameof(WorkflowExecutable.Nodes), StringComparison.OrdinalIgnoreCase)));
                info.Properties.Remove(info.Properties.Single(x => x.Name.Equals(nameof(WorkflowExecutable.NodesById), StringComparison.OrdinalIgnoreCase)));
            }
            else if (bounded && info.Type == typeof(ExecutableActivityTemplate))
                info.Properties.Remove(info.Properties.Single(x => x.Name.Equals(nameof(ExecutableActivityTemplate.NodesById), StringComparison.OrdinalIgnoreCase)));
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
        options.Converters.Add(new JsonStringEnumConverter());
        // Runtime artifact identities are opaque UTF-16 values. Encoding string values as framed UTF-16
        // preserves accepted lone surrogates in JSON before the payload reaches a provider.
        options.Converters.Add(new LosslessUtf16StringConverter(bounded));
        return options;
    }

    private sealed class BoundedSnapshotStream(int maximumBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        private void Check(int count)
        { if (count < 0 || Length + count > maximumBytes) throw new InvalidDataException("The persisted Runtime JSON projection exceeds its bounded source size."); }
    }

    private sealed class LosslessUtf16StringConverter(bool canonical = false) : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Decode(ref reader);

        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Decode(ref reader);

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(EfRelationalIdentity.Encode(value));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(EfRelationalIdentity.Encode(value));

        private string Decode(ref Utf8JsonReader reader)
        {
            var encoded = reader.GetString() ?? throw new JsonException("A runtime artifact string was null.");
            var decoded = EfRelationalIdentity.Decode(encoded);
            if (canonical && EfRelationalIdentity.Encode(decoded) != encoded) throw new JsonException("A runtime artifact identity encoding is not canonical.");
            return decoded;
        }
    }
}
