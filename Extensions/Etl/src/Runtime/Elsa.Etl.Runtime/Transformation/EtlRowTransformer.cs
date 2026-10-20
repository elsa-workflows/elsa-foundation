using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Csv;

namespace Elsa.Etl.Runtime.Transformation;

/// <summary>A typed scalar retained while validating, looking up, and mapping one logical record.</summary>
internal readonly record struct EtlTypedValue(EtlColumnType Type, object? Value);

/// <summary>A validated logical record keyed by its exact, case-sensitive schema names.</summary>
internal sealed record EtlTypedRow(int RecordOrdinal, IReadOnlyDictionary<string, EtlTypedValue> Values);

/// <summary>A safe row diagnostic that contains no input values.</summary>
internal sealed record EtlRowError(int RecordOrdinal, string? Field, string Code);

internal enum EtlRowDisposition
{
    Accepted,
    Filtered,
    Rejected
}

/// <summary>One row's transformation result. Accepted output includes its JSONL line feed.</summary>
internal sealed record EtlRowTransformResult(
    int RecordOrdinal,
    EtlRowDisposition Disposition,
    ReadOnlyMemory<byte> JsonlBytes,
    EtlRowError? Error);

/// <summary>Validates, joins, maps, filters, and serializes one finite ETL source record.</summary>
/// <remarks>Pipeline shape and lookup snapshot bounds are owned and validated by the caller.</remarks>
internal sealed class EtlRowTransformer
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
    private readonly EtlPipeline _pipeline;
    private readonly EtlCsvHeaderMap _sourceHeader;
    private readonly IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> _lookupRows;
    private readonly string _sourceLookupField;
    private readonly string _lookupKeyField;
    private readonly EtlColumnType _lookupKeyType;
    private readonly IReadOnlyList<CompiledMapping> _mapping;
    private readonly IReadOnlyList<CompiledFilter> _filters;
    private readonly Dictionary<string, int> _outputFieldIndices;

    public EtlRowTransformer(
        EtlPipeline pipeline,
        EtlCsvHeaderMap sourceHeader,
        IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> lookupRows)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(sourceHeader);
        ArgumentNullException.ThrowIfNull(lookupRows);

        if (!sourceHeader.MatchesSchema(pipeline.SourceSchema))
            throw new ArgumentException("The compiled source header does not match the pipeline schema.", nameof(sourceHeader));

        var sourceKey = FindColumn(pipeline.SourceSchema, pipeline.SourceLookupField, nameof(pipeline.SourceLookupField));
        var lookupKey = FindColumn(pipeline.LookupSchema, pipeline.LookupKeyField, nameof(pipeline.LookupKeyField));
        if (sourceKey.Type != lookupKey.Type)
            throw new ArgumentException("Source and lookup key types must match.", nameof(pipeline));

        _pipeline = pipeline;
        _sourceHeader = sourceHeader;
        _lookupRows = lookupRows;
        _sourceLookupField = pipeline.SourceLookupField;
        _lookupKeyField = pipeline.LookupKeyField;
        _lookupKeyType = lookupKey.Type;
        _mapping = CompileMapping(pipeline);
        _outputFieldIndices = _mapping
            .Select((mapping, index) => (mapping.Name, index))
            .ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.Ordinal);
        _filters = CompileFilters(pipeline.Filters);

        foreach (var entry in lookupRows)
        {
            if (entry.Key.Type != _lookupKeyType || entry.Key.Value is null ||
                !entry.Value.Values.TryGetValue(_lookupKeyField, out var rowKey) || rowKey != entry.Key)
            {
                throw new ArgumentException("The prepared lookup contains an invalid typed key.", nameof(lookupRows));
            }
        }
    }

    /// <summary>Compiles a CSV header into exact schema-name-to-column positions.</summary>
    public static bool TryCompileHeader(
        IReadOnlyList<EtlColumn> schema,
        CsvRecord headerRecord,
        out EtlCsvHeaderMap? header,
        out EtlHeaderError? error) =>
        EtlCsvHeaderMap.TryCreate(schema, headerRecord, out header, out error);

    /// <summary>Validates one source or lookup record using a previously validated header map.</summary>
    public static bool TryParseTypedRow(
        IReadOnlyList<EtlColumn> schema,
        EtlCsvHeaderMap header,
        CsvRecord record,
        out EtlTypedRow? typedRow,
        out EtlRowError? error)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(record);

        typedRow = null;
        error = null;
        if (!header.MatchesSchema(schema))
            throw new ArgumentException("The compiled CSV header does not match the supplied schema.", nameof(header));

        if (record.Fields.Length != schema.Count)
        {
            error = new EtlRowError(record.RecordOrdinal, null, "etl.field-count-mismatch");
            return false;
        }

        var values = new Dictionary<string, EtlTypedValue>(schema.Count, StringComparer.Ordinal);
        foreach (var column in schema)
        {
            var rawValue = record.Fields[header.GetFieldIndex(column.Name)];
            if (rawValue.Length == 0)
            {
                if (column.Required)
                {
                    error = new EtlRowError(record.RecordOrdinal, column.Name, "etl.required-value-missing");
                    return false;
                }

                values.Add(column.Name, new EtlTypedValue(column.Type, null));
                continue;
            }

            if (!TryConvert(column.Type, rawValue, out var value))
            {
                error = new EtlRowError(record.RecordOrdinal, column.Name, "etl.invalid-value");
                return false;
            }

            values.Add(column.Name, new EtlTypedValue(column.Type, value));
        }

        typedRow = new EtlTypedRow(record.RecordOrdinal, values);
        return true;
    }

    /// <summary>Transforms one data record with the caller's remaining JSONL byte budget.</summary>
    public EtlRowTransformResult Transform(CsvRecord sourceRecord, long remainingOutputBytes)
    {
        ArgumentNullException.ThrowIfNull(sourceRecord);

        if (sourceRecord.RecordOrdinal == 0)
            return Reject(sourceRecord.RecordOrdinal, null, "etl.header-record-not-data");

        if (!TryParseTypedRow(_pipeline.SourceSchema, _sourceHeader, sourceRecord, out var sourceRow, out var parseError))
            return Reject(parseError!);

        if (!sourceRow!.Values.TryGetValue(_sourceLookupField, out var sourceKey) || sourceKey.Value is null)
        {
            return Reject(sourceRecord.RecordOrdinal, _sourceLookupField, "etl.lookup-key-missing");
        }

        if (!_lookupRows.TryGetValue(sourceKey, out var lookupRow))
            return Reject(sourceRecord.RecordOrdinal, _sourceLookupField, "etl.lookup-key-not-found");

        var mappedValues = new EtlTypedValue[_mapping.Count];
        for (var index = 0; index < _mapping.Count; index++)
        {
            var mapping = _mapping[index];
            var values = mapping.Origin == EtlFieldOrigin.Source ? sourceRow.Values : lookupRow.Values;
            if (!values.TryGetValue(mapping.Field, out mappedValues[index]))
            {
                return Reject(sourceRecord.RecordOrdinal, mapping.Field, "etl.mapped-field-missing");
            }
        }

        foreach (var filter in _filters)
        {
            var actual = mappedValues[_outputFieldIndices[filter.Field]];
            var equals = actual == filter.Value;
            if (filter.Comparison == EtlComparison.Equal ? !equals : equals)
                return new EtlRowTransformResult(sourceRecord.RecordOrdinal, EtlRowDisposition.Filtered, ReadOnlyMemory<byte>.Empty, null);
        }

        using var outputStream = new BoundedOutputStream(remainingOutputBytes);
        string? currentField = null;
        try
        {
            using (var writer = new Utf8JsonWriter(outputStream, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                for (var index = 0; index < _mapping.Count; index++)
                {
                    currentField = _mapping[index].Name;
                    writer.WritePropertyName(currentField);
                    WriteJsonValue(writer, mappedValues[index]);
                }

                writer.WriteEndObject();
                writer.Flush();
            }

            outputStream.AppendLineFeed();
            return new EtlRowTransformResult(sourceRecord.RecordOrdinal, EtlRowDisposition.Accepted, outputStream.WrittenMemory, null);
        }
        catch (OutputLimitExceededException)
        {
            return Reject(sourceRecord.RecordOrdinal, currentField, "etl.output-too-large");
        }
    }

    private IReadOnlyList<CompiledMapping> CompileMapping(EtlPipeline pipeline)
    {
        var outputNames = new HashSet<string>(StringComparer.Ordinal);
        var mappings = new List<CompiledMapping>(pipeline.Mapping.Count);
        foreach (var field in pipeline.Mapping)
        {
            if (!outputNames.Add(field.Name))
                throw new ArgumentException("Output field names must be unique.", nameof(pipeline));

            var schema = field.Origin switch
            {
                EtlFieldOrigin.Source => pipeline.SourceSchema,
                EtlFieldOrigin.Lookup => pipeline.LookupSchema,
                _ => throw new ArgumentException("The mapping contains an unsupported field origin.", nameof(pipeline))
            };
            var column = FindColumn(schema, field.Field, nameof(pipeline.Mapping));
            mappings.Add(new CompiledMapping(field.Name, field.Origin, field.Field, column.Type));
        }

        return mappings;
    }

    private IReadOnlyList<CompiledFilter> CompileFilters(IReadOnlyList<EtlFilter> filters)
    {
        var compiled = new List<CompiledFilter>(filters.Count);
        foreach (var filter in filters)
        {
            if (!_outputFieldIndices.TryGetValue(filter.Field, out var index))
                throw new ArgumentException("Filters must reference mapped output fields.", nameof(_pipeline));

            if (filter.Comparison is not EtlComparison.Equal and not EtlComparison.NotEqual)
                throw new ArgumentException("The filter comparison is not supported.", nameof(_pipeline));

            var type = _mapping[index].Type;
            if (!TryConvertFilterValue(filter.Value, type, out var value))
                throw new ArgumentException("The filter value must match the mapped output type.", nameof(_pipeline));

            compiled.Add(new CompiledFilter(filter.Field, filter.Comparison, value));
        }

        return compiled;
    }

    private static EtlColumn FindColumn(IReadOnlyList<EtlColumn> schema, string fieldName, string parameterName)
    {
        var found = schema.FirstOrDefault(column => string.Equals(column.Name, fieldName, StringComparison.Ordinal));
        return found ?? throw new ArgumentException("The pipeline references an unknown schema field.", parameterName);
    }

    private static bool TryConvert(EtlColumnType type, string rawValue, out object? value)
    {
        value = null;
        switch (type)
        {
            case EtlColumnType.String:
                value = rawValue;
                return true;
            case EtlColumnType.Int64:
                if (long.TryParse(rawValue, NumberStyles.AllowLeadingSign, InvariantCulture, out var integer))
                {
                    value = integer;
                    return true;
                }

                return false;
            case EtlColumnType.Decimal:
                const NumberStyles decimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
                if (decimal.TryParse(rawValue, decimalStyles, InvariantCulture, out var decimalValue))
                {
                    value = decimalValue;
                    return true;
                }

                return false;
            case EtlColumnType.Boolean:
                if (string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                    return true;
                }

                if (string.Equals(rawValue, "false", StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryConvertFilterValue(JsonElement jsonValue, EtlColumnType type, out EtlTypedValue typedValue)
    {
        if (jsonValue.ValueKind == JsonValueKind.Null)
        {
            typedValue = new EtlTypedValue(type, null);
            return true;
        }

        switch (type)
        {
            case EtlColumnType.String when jsonValue.ValueKind == JsonValueKind.String:
                typedValue = new EtlTypedValue(type, jsonValue.GetString());
                return true;
            case EtlColumnType.Int64 when jsonValue.ValueKind == JsonValueKind.Number && jsonValue.TryGetInt64(out var integer):
                typedValue = new EtlTypedValue(type, integer);
                return true;
            case EtlColumnType.Decimal when jsonValue.ValueKind == JsonValueKind.Number && jsonValue.TryGetDecimal(out var decimalValue):
                typedValue = new EtlTypedValue(type, decimalValue);
                return true;
            case EtlColumnType.Boolean when jsonValue.ValueKind == JsonValueKind.True:
                typedValue = new EtlTypedValue(type, true);
                return true;
            case EtlColumnType.Boolean when jsonValue.ValueKind == JsonValueKind.False:
                typedValue = new EtlTypedValue(type, false);
                return true;
            default:
                typedValue = default;
                return false;
        }
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, EtlTypedValue value)
    {
        if (value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value.Type)
        {
            case EtlColumnType.String:
                writer.WriteStringValue((string)value.Value);
                break;
            case EtlColumnType.Int64:
                writer.WriteNumberValue((long)value.Value);
                break;
            case EtlColumnType.Decimal:
                writer.WriteNumberValue((decimal)value.Value);
                break;
            case EtlColumnType.Boolean:
                writer.WriteBooleanValue((bool)value.Value);
                break;
            default:
                throw new InvalidOperationException("The typed row contains an unsupported scalar.");
        }
    }

    private static EtlRowTransformResult Reject(int recordOrdinal, string? field, string code) =>
        new(recordOrdinal, EtlRowDisposition.Rejected, ReadOnlyMemory<byte>.Empty, new EtlRowError(recordOrdinal, field, code));

    private static EtlRowTransformResult Reject(EtlRowError error) =>
        new(error.RecordOrdinal, EtlRowDisposition.Rejected, ReadOnlyMemory<byte>.Empty, error);

    private sealed record CompiledMapping(string Name, EtlFieldOrigin Origin, string Field, EtlColumnType Type);

    private sealed record CompiledFilter(string Field, EtlComparison Comparison, EtlTypedValue Value);

    private sealed class BoundedOutputStream : Stream
    {
        private readonly int _limit;
        private byte[] _buffer = [];
        private int _written;

        public BoundedOutputStream(long byteLimit)
        {
            _limit = byteLimit <= 0 ? 0 : (int)Math.Min(byteLimit, Array.MaxLength);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

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
            EnsureWithinLimit(buffer.Length);
            EnsureCapacity(buffer.Length);
            buffer.CopyTo(_buffer.AsSpan(_written));
            _written += buffer.Length;
        }

        public void AppendLineFeed()
        {
            WriteByte((byte)'\n');
        }

        public override void WriteByte(byte value)
        {
            EnsureWithinLimit(1);
            EnsureCapacity(1);
            _buffer[_written++] = value;
        }

        private void EnsureCapacity(int additionalBytes)
        {
            var minimumCapacity = checked(_written + additionalBytes);
            if (_buffer.Length >= minimumCapacity)
                return;

            var growthCapacity = _buffer.Length == 0
                ? Math.Min(_limit, 256)
                : (int)Math.Min(_limit, (long)_buffer.Length * 2);
            var newCapacity = Math.Max(minimumCapacity, growthCapacity);
            if (newCapacity > _limit)
                throw new OutputLimitExceededException();

            Array.Resize(ref _buffer, newCapacity);
        }

        private void EnsureWithinLimit(int count)
        {
            if (count > _limit - _written)
                throw new OutputLimitExceededException();
        }
    }

    private sealed class OutputLimitExceededException : Exception
    {
    }
}
