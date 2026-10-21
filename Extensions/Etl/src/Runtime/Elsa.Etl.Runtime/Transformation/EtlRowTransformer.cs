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
/// <remarks>Connector mapping, filter, and key semantics are compiled here; source parsing and lookup snapshot bounds belong to the caller.</remarks>
internal sealed class EtlRowTransformer
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
    private static readonly IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> EmptyLookup =
        new Dictionary<EtlTypedValue, EtlTypedRow>();
    private readonly IReadOnlyList<EtlColumn> _csvSourceSchema;
    private readonly EtlCsvHeaderMap? _sourceHeader;
    private readonly IReadOnlyList<TransformField> _sourceFields;
    private readonly IReadOnlyList<TransformField> _lookupFields;
    private readonly HashSet<string> _sourceFieldNames;
    private readonly HashSet<string> _lookupFieldNames;
    private readonly bool _hasLookup;
    private readonly IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> _lookupRows;
    private readonly string? _sourceLookupField;
    private readonly string? _lookupKeyField;
    private readonly EtlColumnType? _lookupKeyType;
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

        var plan = CompilePlan(
            ToTransformFields(pipeline.SourceSchema, nameof(pipeline)),
            ToTransformFields(pipeline.LookupSchema, nameof(pipeline)),
            true,
            pipeline.SourceLookupField,
            pipeline.LookupKeyField,
            pipeline.Mapping,
            pipeline.Filters,
            rejectNullFilterForRequiredField: false,
            requireRequiredLookupKeys: false,
            parameterName: nameof(pipeline));

        _csvSourceSchema = pipeline.SourceSchema;
        _sourceHeader = sourceHeader;
        _sourceFields = plan.SourceFields;
        _lookupFields = plan.LookupFields;
        _sourceFieldNames = plan.SourceFieldNames;
        _lookupFieldNames = plan.LookupFieldNames;
        _hasLookup = true;
        _lookupRows = lookupRows;
        _sourceLookupField = plan.SourceLookupField;
        _lookupKeyField = plan.LookupKeyField;
        _lookupKeyType = plan.LookupKeyType;
        _mapping = plan.Mapping;
        _filters = plan.Filters;
        _outputFieldIndices = plan.OutputFieldIndices;
        foreach (var entry in lookupRows)
        {
            if (!_lookupKeyType.HasValue || entry.Key.Type != _lookupKeyType.Value || entry.Key.Value is null ||
                !entry.Value.Values.TryGetValue(_lookupKeyField!, out var rowKey) || rowKey != entry.Key)
                throw new ArgumentException("The prepared lookup contains an invalid typed key.", nameof(lookupRows));
        }
    }

    /// <summary>Creates a transformer for typed connector records and an optional prepared CSV lookup.</summary>
    public EtlRowTransformer(
        EtlPipelineV2 pipeline,
        IReadOnlyDictionary<EtlTypedValue, EtlTypedRow>? lookupRows = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline.Lookup is not null && lookupRows is null)
            throw new ArgumentNullException(nameof(lookupRows), "A declared CSV lookup requires its prepared typed rows.");
        var plan = CompileV2Plan(pipeline);

        _csvSourceSchema = pipeline.Source is EtlCsvSource csvSource ? csvSource.Schema : Array.Empty<EtlColumn>();
        _sourceHeader = null;
        _sourceFields = plan.SourceFields;
        _lookupFields = plan.LookupFields;
        _sourceFieldNames = plan.SourceFieldNames;
        _lookupFieldNames = plan.LookupFieldNames;
        _hasLookup = plan.HasLookup;
        _lookupRows = lookupRows ?? EmptyLookup;
        _sourceLookupField = plan.SourceLookupField;
        _lookupKeyField = plan.LookupKeyField;
        _lookupKeyType = plan.LookupKeyType;
        _mapping = plan.Mapping;
        _filters = plan.Filters;
        _outputFieldIndices = plan.OutputFieldIndices;

        ValidateLookupRows(_lookupRows, nameof(lookupRows));
    }

    /// <summary>Validates the v2 transformation contract without requiring source or lookup records.</summary>
    internal static void ValidateV2Semantics(EtlPipelineV2 pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _ = CompileV2Plan(pipeline);
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

        if (_sourceHeader is null || _csvSourceSchema.Count == 0)
            throw new InvalidOperationException("CSV record transformation requires a compiled CSV header.");

        if (sourceRecord.RecordOrdinal == 0)
            return Reject(sourceRecord.RecordOrdinal, null, "etl.header-record-not-data");

        if (!TryParseTypedRow(_csvSourceSchema, _sourceHeader, sourceRecord, out var sourceRow, out var parseError))
            return Reject(parseError!);

        return Transform(sourceRow!, remainingOutputBytes);
    }

    /// <summary>Transforms one validated typed source record without inventing a CSV header or record.</summary>
    public EtlRowTransformResult Transform(EtlTypedRow sourceRow, long remainingOutputBytes)
    {
        ArgumentNullException.ThrowIfNull(sourceRow);
        var sourceError = ValidateTypedRow(sourceRow, _sourceFields, _sourceFieldNames);
        if (sourceError is not null)
            return Reject(sourceError);

        EtlTypedRow? lookupRow = null;
        if (_hasLookup)
        {
            if (string.IsNullOrEmpty(_sourceLookupField) || string.IsNullOrEmpty(_lookupKeyField) || !_lookupKeyType.HasValue ||
                !sourceRow.Values.TryGetValue(_sourceLookupField, out var sourceKey) || sourceKey.Value is null)
                return Reject(sourceRow.RecordOrdinal, _sourceLookupField, "etl.lookup-key-missing");

            if (!_lookupRows.TryGetValue(sourceKey, out lookupRow))
                return Reject(sourceRow.RecordOrdinal, _sourceLookupField, "etl.lookup-key-not-found");
        }

        var mappedValues = new EtlTypedValue[_mapping.Count];
        for (var index = 0; index < _mapping.Count; index++)
        {
            var mapping = _mapping[index];
            var values = mapping.Origin == EtlFieldOrigin.Source ? sourceRow.Values : lookupRow!.Values;
            if (!values.TryGetValue(mapping.Field, out mappedValues[index]))
            {
                if (mapping.Required)
                    return Reject(sourceRow.RecordOrdinal, mapping.Field, "etl.mapped-field-missing");
                mappedValues[index] = new EtlTypedValue(mapping.Type, null);
            }
        }

        foreach (var filter in _filters)
        {
            var actual = mappedValues[_outputFieldIndices[filter.Field]];
            var equals = actual == filter.Value;
            if (filter.Comparison == EtlComparison.Equal ? !equals : equals)
                return new EtlRowTransformResult(sourceRow.RecordOrdinal, EtlRowDisposition.Filtered, ReadOnlyMemory<byte>.Empty, null);
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
            return new EtlRowTransformResult(sourceRow.RecordOrdinal, EtlRowDisposition.Accepted, outputStream.WrittenMemory, null);
        }
        catch (OutputLimitExceededException)
        {
            return Reject(sourceRow.RecordOrdinal, currentField, "etl.output-too-large");
        }
    }

    private void ValidateLookupRows(IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> lookupRows, string parameterName)
    {
        if (!_hasLookup)
        {
            if (lookupRows.Count != 0)
                throw new ArgumentException("A lookup snapshot was supplied for a pipeline without a lookup.", parameterName);
            return;
        }

        foreach (var entry in lookupRows)
        {
            if (!_lookupKeyType.HasValue || entry.Key.Type != _lookupKeyType.Value || entry.Key.Value is null ||
                entry.Value is null || ValidateTypedRow(entry.Value, _lookupFields, _lookupFieldNames) is not null ||
                string.IsNullOrEmpty(_lookupKeyField) || !entry.Value.Values.TryGetValue(_lookupKeyField, out var rowKey) ||
                rowKey != entry.Key)
                throw new ArgumentException("The prepared lookup contains an invalid typed key or row.", parameterName);
        }
    }

    private static EtlRowError? ValidateTypedRow(
        EtlTypedRow row,
        IReadOnlyList<TransformField> schema,
        HashSet<string> expectedNames)
    {
        if (row.Values is null)
            return new EtlRowError(row.RecordOrdinal, null, "etl.row-invalid");

        foreach (var field in schema)
        {
            if (!row.Values.TryGetValue(field.Name, out var value))
            {
                if (field.Required)
                    return new EtlRowError(row.RecordOrdinal, field.Name, "etl.required-value-missing");
                continue;
            }
            if (value.Value is null)
            {
                if (field.Required)
                    return new EtlRowError(row.RecordOrdinal, field.Name, "etl.required-value-missing");
                if (value.Type != field.Type)
                    return new EtlRowError(row.RecordOrdinal, field.Name, "etl.invalid-value");
                continue;
            }

            if (value.Type != field.Type || !HasExpectedScalarType(value))
                return new EtlRowError(row.RecordOrdinal, field.Name, "etl.invalid-value");
        }

        foreach (var name in row.Values.Keys)
            if (!expectedNames.Contains(name))
                return new EtlRowError(row.RecordOrdinal, name, "etl.field-unexpected");
        return null;
    }

    private static bool HasExpectedScalarType(EtlTypedValue value) => value.Type switch
    {
        EtlColumnType.String => value.Value is string,
        EtlColumnType.Int64 => value.Value is long,
        EtlColumnType.Decimal => value.Value is decimal,
        EtlColumnType.Boolean => value.Value is bool,
        _ => false
    };

    private static CompiledPlan CompileV2Plan(EtlPipelineV2 pipeline)
    {
        var sourceFields = pipeline.Source switch
        {
            EtlCsvSource csv => ToTransformFields(csv.Schema, nameof(pipeline)),
            EtlHttpJsonSource http => ToTransformFields(http.Fields, nameof(pipeline)),
            _ => throw new ArgumentException("The connector source kind is not supported by the transformer.", nameof(pipeline))
        };
        var lookupFields = pipeline.Lookup is null
            ? Array.Empty<TransformField>()
            : ToTransformFields(pipeline.Lookup.Schema, nameof(pipeline));

        return CompilePlan(sourceFields, lookupFields, pipeline.Lookup is not null,
            pipeline.Lookup?.SourceKeyField, pipeline.Lookup?.LookupKeyField,
            pipeline.Mapping, pipeline.Filters, rejectNullFilterForRequiredField: true,
            requireRequiredLookupKeys: true, parameterName: nameof(pipeline));
    }

    private static CompiledPlan CompilePlan(
        IReadOnlyList<TransformField> sourceFields,
        IReadOnlyList<TransformField> lookupFields,
        bool hasLookup,
        string? sourceLookupField,
        string? lookupKeyField,
        IReadOnlyList<EtlOutputField> mappingFields,
        IReadOnlyList<EtlFilter> filters,
        bool rejectNullFilterForRequiredField,
        bool requireRequiredLookupKeys,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(sourceFields);
        ArgumentNullException.ThrowIfNull(lookupFields);
        ArgumentNullException.ThrowIfNull(mappingFields);
        ArgumentNullException.ThrowIfNull(filters);
        var sourceFieldNames = ValidateSchema(sourceFields, parameterName);
        var lookupFieldNames = ValidateSchema(lookupFields, parameterName);

        EtlColumnType? lookupKeyType = null;
        if (hasLookup)
        {
            if (string.IsNullOrWhiteSpace(sourceLookupField) || string.IsNullOrWhiteSpace(lookupKeyField))
                throw new ArgumentException("A lookup requires source and lookup key fields.", parameterName);
            var sourceKey = FindField(sourceFields, sourceLookupField, parameterName);
            var lookupKey = FindField(lookupFields, lookupKeyField, parameterName);
            if (sourceKey.Type != lookupKey.Type)
                throw new ArgumentException("Source and lookup key types must match.", parameterName);
            if (requireRequiredLookupKeys && (!sourceKey.Required || !lookupKey.Required))
                throw new ArgumentException("Source and lookup key fields must both be required.", parameterName);
            lookupKeyType = lookupKey.Type;
        }
        else if (lookupFields.Count != 0 || sourceLookupField is not null || lookupKeyField is not null)
        {
            throw new ArgumentException("A pipeline without a lookup cannot declare lookup fields.", parameterName);
        }

        var outputNames = new HashSet<string>(StringComparer.Ordinal);
        var mappings = new List<CompiledMapping>(mappingFields.Count);
        foreach (var field in mappingFields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name) || string.IsNullOrWhiteSpace(field.Field))
                throw new ArgumentException("The mapping contains an invalid field declaration.", parameterName);
            if (!outputNames.Add(field.Name))
                throw new ArgumentException("Output field names must be unique.", parameterName);

            var schema = field.Origin switch
            {
                EtlFieldOrigin.Source => sourceFields,
                EtlFieldOrigin.Lookup when hasLookup => lookupFields,
                EtlFieldOrigin.Lookup => throw new ArgumentException("A lookup mapping requires a declared lookup.", parameterName),
                _ => throw new ArgumentException("The mapping contains an unsupported field origin.", parameterName)
            };
            var column = FindField(schema, field.Field, parameterName);
            mappings.Add(new CompiledMapping(field.Name, field.Origin, field.Field, column.Type, column.Required));
        }

        var outputFieldIndices = mappings
            .Select((mapping, index) => (mapping.Name, index))
            .ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.Ordinal);
        var compiledFilters = new List<CompiledFilter>(filters.Count);
        foreach (var filter in filters)
        {
            if (filter is null || string.IsNullOrWhiteSpace(filter.Field) || !outputFieldIndices.TryGetValue(filter.Field, out var index))
                throw new ArgumentException("Filters must reference mapped output fields.", parameterName);

            if (filter.Comparison is not EtlComparison.Equal and not EtlComparison.NotEqual)
                throw new ArgumentException("The filter comparison is not supported.", parameterName);

            var type = mappings[index].Type;
            if (rejectNullFilterForRequiredField && mappings[index].Required && filter.Value.ValueKind == JsonValueKind.Null)
                throw new ArgumentException("A null filter literal requires an optional mapped field.", parameterName);
            if (!TryConvertFilterValue(filter.Value, type, out var value))
                throw new ArgumentException("The filter value must match the mapped output type.", parameterName);

            compiledFilters.Add(new CompiledFilter(filter.Field, filter.Comparison, value));
        }

        return new(sourceFields, lookupFields, sourceFieldNames, lookupFieldNames, hasLookup,
            sourceLookupField, lookupKeyField, lookupKeyType, mappings, compiledFilters, outputFieldIndices);
    }

    private static IReadOnlyList<TransformField> ToTransformFields(IReadOnlyList<EtlColumn> schema, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return schema.Select(column => column is null
                ? throw new ArgumentException("The schema contains a null field.", parameterName)
                : new TransformField(column.Name, column.Type, column.Required))
            .ToArray();
    }

    private static IReadOnlyList<TransformField> ToTransformFields(IReadOnlyList<EtlJsonField> schema, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return schema.Select(field => field is null
                ? throw new ArgumentException("The schema contains a null field.", parameterName)
                : new TransformField(field.Name, field.Type, field.Required))
            .ToArray();
    }

    private static HashSet<string> ValidateSchema(IReadOnlyList<TransformField> fields, string parameterName)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || !Enum.IsDefined(field.Type) || !names.Add(field.Name))
                throw new ArgumentException("The source or lookup schema contains an invalid field.", parameterName);
        }
        return names;
    }

    private static TransformField FindField(IReadOnlyList<TransformField> schema, string fieldName, string parameterName)
    {
        var found = schema.FirstOrDefault(field => string.Equals(field.Name, fieldName, StringComparison.Ordinal));
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

    private sealed record TransformField(string Name, EtlColumnType Type, bool Required);

    private sealed record CompiledPlan(
        IReadOnlyList<TransformField> SourceFields,
        IReadOnlyList<TransformField> LookupFields,
        HashSet<string> SourceFieldNames,
        HashSet<string> LookupFieldNames,
        bool HasLookup,
        string? SourceLookupField,
        string? LookupKeyField,
        EtlColumnType? LookupKeyType,
        IReadOnlyList<CompiledMapping> Mapping,
        IReadOnlyList<CompiledFilter> Filters,
        Dictionary<string, int> OutputFieldIndices);

    private sealed record CompiledMapping(string Name, EtlFieldOrigin Origin, string Field, EtlColumnType Type, bool Required);

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
