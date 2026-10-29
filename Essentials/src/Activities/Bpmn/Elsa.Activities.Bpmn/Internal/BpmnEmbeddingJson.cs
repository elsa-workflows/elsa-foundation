using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Lossless text inside the new adapter's opaque activity state and invocation documents.</summary>
internal static class BpmnEmbeddingJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    internal static JsonElement Write<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static T Read<T>(JsonElement value) => Read<T>(value.GetRawText());
    internal static T Read<T>(string value)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(value) > 64 * 1024) throw Invalid();
        try { return JsonSerializer.Deserialize<T>(value, Options) ?? throw Invalid(); }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        { throw new BpmnModelValidationException("The RunBpmn invocation document is malformed or unsupported.", exception); }
    }
    internal static BpmnModelValidationException Invalid() => new("The RunBpmn invocation differs from its pinned model and actual native wait.");
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new TextConverter());
        return options;
    }
    private sealed class TextConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value is null || !value.StartsWith("u16:", StringComparison.Ordinal)) throw new JsonException("RunBpmn text encoding is unsupported.");
            var bytes = Convert.FromBase64String(value[4..]);
            if (bytes.Length % 2 != 0) throw new JsonException("RunBpmn text encoding is incomplete.");
            var chars = new char[bytes.Length / 2];
            for (var index = 0; index < chars.Length; index++) chars[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2, 2));
            return new string(chars);
        }
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            var bytes = new byte[checked(value.Length * 2)];
            for (var index = 0; index < value.Length; index++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2, 2), value[index]);
            writer.WriteStringValue("u16:" + Convert.ToBase64String(bytes));
        }
    }
}

internal sealed record BpmnEmbeddingPin(string SourceReferenceId, WorkflowExecutableIdentity Executable, string MaterialIncarnationId);
internal sealed record BpmnEmbeddedInvocation(int FormatVersion, PersistenceScope Scope, string ExecutionId, BpmnEmbeddingPin Model,
    WorkflowExecutableIdentity NativeExecutable, string NativeExecutionId, string NativeInvocationId, string NativeNodeId,
    string ResumeTargetId, string? RegistrationId, string? NativeAttemptId = null);
internal sealed record BpmnEmbeddedInput(JsonElement Invocation, string SourceOutboxItemId);
internal sealed record BpmnEmbeddedParent(JsonElement Invocation);
