using System.Security.Cryptography;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using Elsa.Etl.Core;
using Elsa.Etl.Releases.Core;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Etl.Releases;

internal sealed class EtlReleaseArtifactCodec : IEtlReleaseArtifactCodec
{
    private const int ScopeEnvelopeAllowance = 8192;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public EtlEncodedReleaseArtifact Encode(EtlReleaseArtifact artifact, ReleaseBundleLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var settings = Settings(limits);
        byte[] bytes;
        try
        {
            ValidateEnvelopeBounds(artifact, settings);
            var output = new BoundedWriter(MaximumBytes(settings));
            using (var writer = new Utf8JsonWriter(output, new() { MaxDepth = settings.MaxJsonDepth }))
            {
                JsonSerializer.Serialize(writer, artifact, Options(settings));
                writer.Flush();
            }
            // Validation still inspects the original source values, so JSON encoding cannot silently repair a string.
            Validate(artifact, settings);
            bytes = output.CopyBytes();
        }
        catch (EtlReleaseArtifactException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        { throw Invalid("etl_release_encoding_invalid"); }
        if (bytes.Length > MaximumBytes(settings)) throw Invalid("etl_release_payload_too_large");
        var descriptor = new ReleaseBundleMember(EtlReleaseCodecs.MemberKind, artifact.Artifact.ArtifactId,
            EtlReleaseCodecs.Artifact, artifact.Artifact.FormatVersion, Hash(bytes), bytes.Length);
        return new(descriptor, new("integer", null, artifact.Artifact.DefinitionVersion), bytes);
    }

    public EtlReleaseArtifact Decode(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null)
    {
        var settings = Settings(limits);
        if (payload.Length > MaximumBytes(settings)) throw Invalid("etl_release_payload_too_large");
        try
        {
            using var document = JsonDocument.Parse(payload, new() { MaxDepth = settings.MaxJsonDepth });
            CheckJson(document.RootElement, settings.MaxJsonDepth);
            var artifact = document.RootElement.Deserialize<EtlReleaseArtifact>(Options(settings)) ?? throw Invalid("etl_release_envelope_invalid");
            Validate(artifact, settings);
            return artifact;
        }
        catch (EtlReleaseArtifactException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        { throw Invalid("etl_release_envelope_invalid"); }
    }

    private static void Validate(EtlReleaseArtifact release, ReleaseBundleLimits settings)
    {
        ValidateEnvelopeBounds(release, settings);
        var artifact = release.Artifact;
        CheckJson(artifact.Pipeline, settings.MaxJsonDepth);
        var hash = EtlArtifactJson.Hash(artifact.Pipeline);
        if (artifact.ContentHash != hash || artifact.ArtifactId != EtlArtifactJson.CreateArtifactId(
                artifact.FormatVersion, artifact.DefinitionId, artifact.DefinitionVersion, hash))
            throw Invalid("etl_release_owner_hash_or_id_invalid");
        if (artifact.FormatVersion == EtlArtifactV2Json.FormatVersion) RequireKnownConnectors(artifact.Pipeline);
        try { EtlArtifactSemantics.Validate(artifact); }
        catch (EtlArtifactSemanticException exception) { throw Invalid(exception.Code.Replace('.', '_').Replace('-', '_')); }
    }

    private static void ValidateEnvelopeBounds(EtlReleaseArtifact release, ReleaseBundleLimits settings)
    {
        if (release.Source is null || release.Source.Scope is null || release.Artifact is null ||
            !Text(release.Source.Authority) || !Text(release.Source.Scope.PersistencePartition) ||
            release.Source.Scope.TenantId is not null && !Text(release.Source.Scope.TenantId))
            throw Invalid("etl_release_scope_invalid");
        var artifact = release.Artifact;
        if (artifact.FormatVersion is not (EtlArtifact.CurrentFormatVersion or EtlArtifactV2Json.FormatVersion))
            throw new EtlReleaseArtifactException("etl_artifact_format_unsupported", true);
        if (artifact.Pipeline.ValueKind != JsonValueKind.Object) throw Invalid("etl_release_envelope_invalid");
        if (JsonMarshal.GetRawUtf8Value(artifact.Pipeline).Length > Math.Min(EtlArtifact.MaximumByteLength, MaximumBytes(settings)))
            throw Invalid("etl_release_payload_too_large");
        if (artifact.DefinitionVersion <= 0 || !Identifier(artifact.DefinitionId) ||
            artifact.ArtifactId is not { Length: > 0 and <= 96 } || artifact.ContentHash is not { Length: 64 } ||
            artifact.ContentHash.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw Invalid("etl_release_identity_invalid");
    }

    private static void RequireKnownConnectors(JsonElement pipeline)
    {
        if (pipeline.ValueKind != JsonValueKind.Object || !pipeline.TryGetProperty("source", out var source) ||
            !pipeline.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Object ||
            !routes.TryGetProperty("main", out var main) || !routes.TryGetProperty("errors", out var errors))
            throw Invalid("etl_release_envelope_invalid");
        RequireKind(source, ["csv", "httpJson", "jsonl", "sql", "rabbitmq"]);
        RequireKind(main, ["jsonl", "http", "csv", "sql", "rabbitmq"]);
        RequireKind(errors, ["jsonl", "http", "csv", "sql", "rabbitmq"]);
        if (source.TryGetProperty("kind", out var sourceKind) && sourceKind.GetString() == "httpJson")
        {
            if (!source.TryGetProperty("pagination", out var pagination)) throw Invalid("etl_release_envelope_invalid");
            RequireKind(pagination, ["singlePage", "token", "nextLink"]);
        }
    }

    private static void RequireKind(JsonElement element, IReadOnlyList<string> known)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("kind", out var kind) ||
            kind.ValueKind != JsonValueKind.String || !Text(kind.GetString())) throw Invalid("etl_release_envelope_invalid");
        if (!known.Contains(kind.GetString(), StringComparer.Ordinal))
            throw new EtlReleaseArtifactException("etl_connector_kind_unsupported", true);
    }

    private static void CheckJson(JsonElement value, int remainingDepth)
    {
        if (remainingDepth <= 0) throw Invalid("etl_release_json_too_deep");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    _ = StrictUtf8.GetByteCount(property.Name);
                    if (!keys.Add(property.Name)) throw Invalid("etl_release_duplicate_json_property");
                    CheckJson(property.Value, remainingDepth - 1);
                }
                break;
            case JsonValueKind.Array: foreach (var child in value.EnumerateArray()) CheckJson(child, remainingDepth - 1); break;
            case JsonValueKind.String: _ = StrictUtf8.GetByteCount(value.GetString()!); break;
            case JsonValueKind.Undefined: throw Invalid("etl_release_envelope_invalid");
        }
    }

    private static bool Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256) return false;
        try { _ = StrictUtf8.GetByteCount(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }

    private static bool Identifier(string? value) => value is { Length: > 0 and <= 256 } && char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static ReleaseBundleLimits Settings(ReleaseBundleLimits? limits)
    {
        var settings = limits ?? new();
        if (settings.MaxBlobBytes <= 0 || settings.MaxJsonDepth is <= 0 or > 128) throw new ArgumentOutOfRangeException(nameof(limits));
        return settings;
    }
    private static int MaximumBytes(ReleaseBundleLimits settings) => Math.Min(settings.MaxBlobBytes, EtlArtifact.MaximumByteLength + ScopeEnvelopeAllowance);
    private static JsonSerializerOptions Options(ReleaseBundleLimits settings)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = settings.MaxJsonDepth
        };
        options.Converters.Add(new RetainedJsonElementConverter());
        return options;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static EtlReleaseArtifactException Invalid(string code) => new(code);

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

    private sealed class BoundedWriter(int maximum) : IBufferWriter<byte>
    {
        // Utf8JsonWriter requests worst-case escaping space and at least a growth block. Hints are not emitted bytes.
        private readonly int _maximumScratch = checked(maximum * 6 + 4096);
        private byte[] _buffer = [];
        private int _written;
        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length - _written) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > maximum - _written) throw Invalid("etl_release_payload_too_large");
            _written += count;
        }
        public Memory<byte> GetMemory(int sizeHint = 0) { Ensure(sizeHint); return _buffer.AsMemory(_written); }
        public Span<byte> GetSpan(int sizeHint = 0) { Ensure(sizeHint); return _buffer.AsSpan(_written); }
        public byte[] CopyBytes() => _buffer.AsSpan(0, _written).ToArray();
        private void Ensure(int sizeHint)
        {
            if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
            sizeHint = Math.Max(1, sizeHint);
            if (sizeHint > _maximumScratch - _written) throw Invalid("etl_release_payload_too_large");
            var required = _written + sizeHint;
            if (_buffer.Length >= required) return;
            var capacity = (int)Math.Min(_maximumScratch, Math.Max(required, Math.Max(256L, (long)_buffer.Length * 2)));
            var replacement = new byte[capacity];
            _buffer.AsSpan(0, _written).CopyTo(replacement);
            _buffer = replacement;
        }
    }
}
