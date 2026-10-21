using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Etl.Core;

/// <summary>Serializes and reads the strict canonical JSON representation of a v2 ETL pipeline document.</summary>
/// <remarks>
/// The writer preserves declared typed-property order; it does not sort arbitrary JSON properties. Readers reject
/// duplicate object properties recursively before typed deserialization. Artifact identity and content hashing are
/// owned by the artifact compiler.
/// </remarks>
public static class EtlArtifactV2Json
{
    /// <summary>The runnable artifact envelope format version for this pipeline document.</summary>
    public const int FormatVersion = 2;

    /// <summary>The maximum serialized size of a v2 pipeline document.</summary>
    public const int MaximumByteLength = EtlArtifact.MaximumByteLength;

    private const int MaximumJsonDepth = 64;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth
    };

    /// <summary>Serializes one typed pipeline in declared property order within the v2 byte and depth bounds.</summary>
    /// <exception cref="ArgumentNullException">The pipeline is null.</exception>
    /// <exception cref="JsonException">The pipeline cannot be serialized within the v2 limits.</exception>
    public static JsonElement Write(EtlPipelineV2 pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ValidateCollectionEntries(pipeline);

        using var output = new BoundedMemoryStream(MaximumByteLength);
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { MaxDepth = MaximumJsonDepth }))
        {
            JsonSerializer.Serialize(writer, pipeline, SerializerOptions);
            writer.Flush();
        }

        using var document = JsonDocument.Parse(output.WrittenMemory, DocumentOptions);
        ValidateDocument(document.RootElement);
        return document.RootElement.Clone();
    }

    /// <summary>Reads a bounded UTF-8 pipeline document after recursively checking its JSON structure.</summary>
    /// <exception cref="JsonException">The input is oversized, malformed, duplicated, too deep, or not a v2 pipeline.</exception>
    public static EtlPipelineV2 Read(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length > MaximumByteLength)
            throw TooLarge();

        using var document = JsonDocument.Parse(utf8Json, DocumentOptions);
        return Read(document.RootElement);
    }

    /// <summary>Reads a pipeline from an existing JSON element after checking its raw size and structure.</summary>
    /// <exception cref="JsonException">The element is oversized, duplicated, too deep, or not a v2 pipeline.</exception>
    public static EtlPipelineV2 Read(JsonElement document)
    {
        try
        {
            if (JsonMarshal.GetRawUtf8Value(document).Length > MaximumByteLength)
                throw TooLarge();
        }
        catch (InvalidOperationException exception)
        {
            throw new JsonException("The ETL v2 pipeline document is undefined.", exception);
        }

        ValidateDocument(document);
        try
        {
            var pipeline = document.Deserialize<EtlPipelineV2>(SerializerOptions)
                           ?? throw new JsonException("The ETL v2 pipeline document resolved to null.");
            ValidateCollectionEntries(pipeline);
            return pipeline;
        }
        catch (NotSupportedException exception)
        {
            throw new JsonException("The ETL v2 pipeline document uses an unsupported JSON type or discriminator.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("The ETL v2 pipeline document contains an invalid value.", exception);
        }
    }

    private static void ValidateCollectionEntries(EtlPipelineV2 pipeline)
    {
        // Serializer nullable annotations do not cover generic collection elements.
        if (HasNullEntries(pipeline.Mapping) || HasNullEntries(pipeline.Filters) ||
            HasNullEntries(pipeline.Lookup?.Schema) ||
            pipeline.Source is EtlCsvSource csv && HasNullEntries(csv.Schema) ||
            pipeline.Source is EtlHttpJsonSource http && HasNullEntries(http.Fields))
            throw new JsonException("ETL v2 declaration collections must not contain null entries.");
    }

    private static bool HasNullEntries<T>(IReadOnlyList<T>? values) where T : class =>
        values?.Any(value => value is null) == true;

    private static void ValidateDocument(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object)
            throw new JsonException("The ETL v2 pipeline document must be a JSON object.");

        var pending = new Stack<(JsonElement Element, int Depth)>();
        pending.Push((document, 1));
        while (pending.TryPop(out var entry))
        {
            if (entry.Depth > MaximumJsonDepth)
                throw new JsonException("The ETL v2 pipeline document exceeds the maximum JSON depth.");

            if (entry.Element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in entry.Element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new JsonException("The ETL v2 pipeline document contains a duplicate JSON property.");
                    pending.Push((property.Value, entry.Depth + 1));
                }
            }
            else if (entry.Element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in entry.Element.EnumerateArray())
                    pending.Push((item, entry.Depth + 1));
            }
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = false,
            AllowOutOfOrderMetadataProperties = true,
            MaxDepth = MaximumJsonDepth,
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { MarkSharedModelPropertiesRequired }
        };
        options.Converters.Add(new StrictEtlFilterConverter());
        options.Converters.Add(new StrictJsonStringEnumConverter<EtlColumnType>());
        options.Converters.Add(new StrictJsonStringEnumConverter<EtlComparison>());
        options.Converters.Add(new StrictJsonStringEnumConverter<EtlFieldOrigin>());
        return options;
    }

    private static void MarkSharedModelPropertiesRequired(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(EtlConnectorLimits))
        {
            var retainedOutputBudgetName = JsonNamingPolicy.CamelCase.ConvertName(nameof(EtlConnectorLimits.MaxRetainedOutputBytes));
            var retainedOutputBudget = typeInfo.Properties.Single(property => property.Name == retainedOutputBudgetName);
            retainedOutputBudget.ShouldSerialize = static (_, value) => (long)value! != EtlConnectorLimits.DefaultMaxRetainedOutputBytes;
        }

        if (typeInfo.Type == typeof(EtlHttpPolicy))
        {
            var metadataBudgetName = JsonNamingPolicy.CamelCase.ConvertName(nameof(EtlHttpPolicy.MaxRetainedMetadataBytes));
            var metadataBudget = typeInfo.Properties.Single(property => property.Name == metadataBudgetName);
            metadataBudget.ShouldSerialize = static (_, value) => (long)value! != EtlHttpPolicy.DefaultMaxRetainedMetadataBytes;
        }

        if (typeInfo.Type != typeof(EtlColumn) && typeInfo.Type != typeof(EtlOutputField))
            return;

        foreach (var property in typeInfo.Properties)
            property.IsRequired = true;
    }

    private static JsonException TooLarge() => new("The ETL v2 pipeline document exceeds 256 KiB.");

    private sealed class StrictEtlFilterConverter : JsonConverter<EtlFilter>
    {
        public override EtlFilter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("An ETL filter must be a JSON object.");

            string? field = null;
            var hasField = false;
            var hasComparison = false;
            var hasValue = false;
            var comparison = default(EtlComparison);
            var value = default(JsonElement);

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("An ETL filter contains an invalid member.");

                var propertyName = reader.GetString();
                if (!reader.Read())
                    throw new JsonException("An ETL filter member has no value.");

                switch (propertyName)
                {
                    case "field":
                        if (hasField || reader.TokenType != JsonTokenType.String)
                            throw new JsonException("An ETL filter field must be one string value.");
                        field = reader.GetString();
                        hasField = true;
                        break;
                    case "comparison":
                        if (hasComparison)
                            throw new JsonException("An ETL filter comparison must occur once.");
                        comparison = JsonSerializer.Deserialize<EtlComparison>(ref reader, options);
                        hasComparison = true;
                        break;
                    case "value":
                        if (hasValue)
                            throw new JsonException("An ETL filter value must occur once.");
                        using (var valueDocument = JsonDocument.ParseValue(ref reader))
                            value = valueDocument.RootElement.Clone();
                        hasValue = true;
                        break;
                    default:
                        throw new JsonException("The ETL filter contains an unrecognized member.");
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || !hasField || !hasComparison || !hasValue)
                throw new JsonException("An ETL filter requires field, comparison, and value members.");

            try
            {
                return new EtlFilter(field!, comparison, value);
            }
            catch (ArgumentException exception)
            {
                throw new JsonException("The ETL filter field must not be null or blank.", exception);
            }
        }

        public override void Write(Utf8JsonWriter writer, EtlFilter value, JsonSerializerOptions options)
        {
            if (string.IsNullOrWhiteSpace(value.Field))
                throw new JsonException("The ETL filter field must not be null or blank.");
            if (value.Value.ValueKind == JsonValueKind.Undefined)
                throw new JsonException("The ETL filter value is undefined.");

            writer.WriteStartObject();
            writer.WriteString("field", value.Field);
            writer.WritePropertyName("comparison");
            JsonSerializer.Serialize(writer, value.Comparison, options);
            writer.WritePropertyName("value");
            value.Value.WriteTo(writer);
            writer.WriteEndObject();
        }
    }

    private sealed class StrictJsonStringEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected a string value for {typeof(TEnum).Name}.");

            var text = reader.GetString();
            foreach (var value in Enum.GetValues<TEnum>())
            {
                var canonical = JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
                if (string.Equals(text, canonical, StringComparison.Ordinal))
                    return value;
            }

            throw new JsonException($"Unrecognized {typeof(TEnum).Name} value.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
                throw new JsonException($"Unrecognized {typeof(TEnum).Name} value.");

            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
        }
    }

    private sealed class BoundedMemoryStream(int maximumLength) : Stream
    {
        private byte[] _buffer = new byte[Math.Min(256, maximumLength)];
        private int _written;
        private bool _isPooled;

        public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > maximumLength - _written)
                throw TooLarge();

            EnsureCapacity(_written + buffer.Length);
            buffer.CopyTo(_buffer.AsSpan(_written));
            _written += buffer.Length;
        }

        protected override void Dispose(bool disposing)
        {
            if (_isPooled)
                ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);

            _buffer = [];
            _written = 0;
            _isPooled = false;
            base.Dispose(disposing);
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _buffer.Length)
                return;

            var capacity = _buffer.Length;
            while (capacity < required)
                capacity = Math.Min(maximumLength, checked(capacity * 2));

            var replacement = ArrayPool<byte>.Shared.Rent(capacity);
            _buffer.AsSpan(0, _written).CopyTo(replacement);
            if (_isPooled)
                ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);

            _buffer = replacement;
            _isPooled = true;
        }
    }
}
