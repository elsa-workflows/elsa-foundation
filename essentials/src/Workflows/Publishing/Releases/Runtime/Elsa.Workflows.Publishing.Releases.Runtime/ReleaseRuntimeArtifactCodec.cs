using System.Collections;
using System.Buffers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

internal sealed class ReleaseRuntimeArtifactCodec : IReleaseRuntimeArtifactCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public ReleaseEncodedRuntimeMember EncodeWorkflow(ReleaseRuntimeWorkflow value, ReleaseBundleLimits? limits = null) =>
        Encode(value, ReleaseRuntimeCodecs.WorkflowKind, value.Executable.Identity.ArtifactId, ReleaseRuntimeCodecs.Workflow,
            new("semver", value.Executable.Identity.ArtifactVersion, null), limits);
    public ReleaseEncodedRuntimeMember EncodeTemplate(ReleaseRuntimeTemplate value, ReleaseBundleLimits? limits = null) =>
        Encode(value, ReleaseRuntimeCodecs.TemplateKind, value.Template.TemplateId, ReleaseRuntimeCodecs.Template, null, limits);
    public ReleaseEncodedRuntimeMember EncodeModel(ReleaseRuntimeModel value, ReleaseBundleLimits? limits = null) =>
        Encode(value, ReleaseRuntimeCodecs.ModelKind, value.Model.Identity.ArtifactId, ReleaseRuntimeCodecs.Model,
            new("opaque", value.Model.Identity.ArtifactVersion, null), limits);
    public ReleaseEncodedRuntimeMember EncodeWorkflowSelection(ReleaseWorkflowSelection value, ReleaseBundleLimits? limits = null) =>
        Encode(value, ReleaseRuntimeCodecs.SelectionKind, value.DefinitionVersionId, ReleaseRuntimeCodecs.Selection, new("semver", value.Version, null), limits);
    public ReleaseEncodedRuntimeMember EncodeActivityVersion(ReleaseActivityVersion value, ReleaseBundleLimits? limits = null) =>
        Encode(value, ReleaseRuntimeCodecs.ActivityVersionKind, value.VersionId, ReleaseRuntimeCodecs.ActivityVersion, new("semver", value.Version, null), limits,
            value.Publication is null ? 1 : 2);
    public ReleaseRuntimeWorkflow DecodeWorkflow(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) => Decode<ReleaseRuntimeWorkflow>(payload, limits);
    public ReleaseRuntimeTemplate DecodeTemplate(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) => Decode<ReleaseRuntimeTemplate>(payload, limits);
    public ReleaseRuntimeModel DecodeModel(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) => Decode<ReleaseRuntimeModel>(payload, limits);
    public ReleaseWorkflowSelection DecodeWorkflowSelection(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) => Decode<ReleaseWorkflowSelection>(payload, limits);
    public ReleaseActivityVersion DecodeActivityVersion(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null) => Decode<ReleaseActivityVersion>(payload, limits);

    private static ReleaseEncodedRuntimeMember Encode<T>(T value, string kind, string identity, string codec, ReleaseOwnerVersion? version, ReleaseBundleLimits? limits, int codecVersion = 1)
    {
        var bounds = Bounds(limits);
        try
        {
            CheckClr(value, bounds);
            var output = new BoundedRuntimeJsonBuffer(bounds.MaxBlobBytes, "runtime_payload_byte_limit");
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { MaxDepth = bounds.MaxJsonDepth }))
            { JsonSerializer.Serialize(writer, new { formatVersion = 1, content = value }, Options(bounds.MaxJsonDepth)); writer.Flush(); }
            var bytes = output.Written.ToArray();
            CheckJson(bytes, bounds);
            return new(new(kind, identity, codec, codecVersion, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength), version, bytes);
        }
        catch (ReleaseRuntimeArtifactException) { throw; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or TargetInvocationException)
        { throw Refuse("runtime_encode_unsupported"); }
    }

    private static T Decode<T>(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits)
    {
        var bounds = Bounds(limits);
        try
        {
            if (payload.Length == 0 || payload.Length > bounds.MaxBlobBytes) throw Refuse("runtime_payload_byte_limit");
            var bytes = payload.ToArray(); CheckJson(bytes, bounds);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = bounds.MaxJsonDepth });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("formatVersion", out var version) || version.GetRawText() != "1" || !root.TryGetProperty("content", out var content))
                throw Refuse("unsupported_runtime_envelope");
            var options = Options(bounds.MaxJsonDepth);
            var result = content.Deserialize<T>(options) ?? throw Refuse("invalid_runtime_payload");
            // Detect ignored/contradictory computed aliases and unsupported default flattening without replacing retained bytes.
            var reconstructed = JsonSerializer.SerializeToElement(result, options);
            if (ActivityTemplateHasher.ComputeCanonicalValueHash(content) != ActivityTemplateHasher.ComputeCanonicalValueHash(reconstructed))
                throw Refuse("runtime_payload_not_lossless");
            CheckClr(result, bounds);
            return result;
        }
        catch (ReleaseRuntimeArtifactException) { throw; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or TargetInvocationException)
        { throw Refuse("runtime_decode_unsupported"); }
    }

    private static JsonSerializerOptions Options(int depth)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(WorkflowExecutable) && info.Type != typeof(ExecutableActivityTemplate)) return;
            for (var index = info.Properties.Count - 1; index >= 0; index--)
                if (info.Properties[index].Name == "nodesById" || info.Type == typeof(WorkflowExecutable) && info.Properties[index].Name == "nodes") info.Properties.RemoveAt(index);
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false, MaxDepth = depth, TypeInfoResolver = resolver,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new RetainedJsonElementConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    internal static void CheckJson(ReadOnlySpan<byte> bytes, ReleaseBundleLimits bounds)
    {
        if (bytes.Length == 0 || bytes.Length > bounds.MaxBlobBytes) throw Refuse("runtime_payload_byte_limit");
        try
        {
            _ = Utf8.GetCharCount(bytes);
            if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) throw Refuse("invalid_runtime_json");
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = bounds.MaxJsonDepth });
            var scopes = new Stack<HashSet<string>?>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.StartArray) scopes.Push(null);
                else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) scopes.Pop();
                else if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var key = reader.GetString()!; Unicode(key);
                    if (!scopes.Peek()!.Add(key)) throw Refuse("duplicate_runtime_property");
                }
                else if (reader.TokenType == JsonTokenType.String) Unicode(reader.GetString()!);
            }
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException)
        { throw Refuse("invalid_runtime_json"); }
    }

    private static void CheckClr(object? root, ReleaseBundleLimits bounds)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var remaining = (long)bounds.MaxBlobBytes;
        Visit(root, 0);
        return;

        void Visit(object? value, int depth)
        {
            if (--remaining < 0 || depth > bounds.MaxJsonDepth) throw Refuse("runtime_object_limit");
            if (value is null) return;
            if (value is string text)
            {
                Unicode(text); ChargeString(text);
                return;
            }
            if (value is JsonElement json)
            {
                // Read the original fragment without GetRawText's allocating copy or WriteTo's reformatting.
                var bytes = JsonMarshal.GetRawUtf8Value(json);
                if (bytes.Length > remaining) throw Refuse("runtime_object_limit");
                CheckJson(bytes, bounds); remaining -= bytes.Length;
                return;
            }
            if (value is DateTimeOffset or DateTime or decimal || value.GetType().IsPrimitive || value.GetType().IsEnum) return;
            if (!visited.Add(value)) return;
            if (value is ValueEnvelope { TransientResource: not null }) throw Refuse("transient_runtime_value_unsupported");
            if (value is IEnumerable sequence)
            {
                // Inspect each contribution before advancing the enumerator: never queue an unbounded collection.
                foreach (var child in sequence) Visit(child, depth + 1);
                return;
            }
            var type = value.GetType();
            if (type == typeof(Type) || type.Namespace is null ||
                !type.Namespace.StartsWith("Elsa.", StringComparison.Ordinal) && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)))
                throw Refuse("runtime_type_unsupported");
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0 || property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;
                if (value is WorkflowExecutable && property.Name is nameof(WorkflowExecutable.Nodes) or nameof(WorkflowExecutable.NodesById)) continue;
                if (value is ExecutableActivityTemplate && property.Name == nameof(ExecutableActivityTemplate.NodesById)) continue;
                Visit(property.GetValue(value), depth + 1);
            }
        }

        void ChargeString(ReadOnlySpan<char> text)
        {
            Span<char> scratch = stackalloc char[1024];
            do
            {
                var status = JavaScriptEncoder.Default.Encode(text, scratch, out var consumed, out var written, isFinalBlock: true);
                remaining -= Utf8.GetByteCount(scratch[..written]);
                if (remaining < 0) throw Refuse("runtime_object_limit");
                if (status == OperationStatus.Done) return;
                if (status != OperationStatus.DestinationTooSmall || consumed == 0) throw Refuse("invalid_runtime_unicode");
                text = text[consumed..];
            } while (true);
        }
    }

    private sealed class RetainedJsonElementConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.Clone();
        }
        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) =>
            writer.WriteRawValue(JsonMarshal.GetRawUtf8Value(value), skipInputValidation: true);
    }

    private static void Unicode(string text)
    {
        for (var index = 0; index < text.Length; index++)
            if (char.IsHighSurrogate(text[index]))
            { if (++index >= text.Length || !char.IsLowSurrogate(text[index])) throw Refuse("invalid_runtime_unicode"); }
            else if (char.IsLowSurrogate(text[index])) throw Refuse("invalid_runtime_unicode");
    }
    private static ReleaseBundleLimits Bounds(ReleaseBundleLimits? limits)
    {
        var value = limits ?? new();
        if (value.MaxBlobBytes <= 0 || value.MaxJsonDepth is <= 0 or > 256) throw new ArgumentOutOfRangeException(nameof(limits));
        return value;
    }
    internal static ReleaseRuntimeArtifactException Refuse(string code) => new(code);
}
