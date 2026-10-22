using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Etl.Core;

namespace Elsa.Etl.Design;

internal sealed partial class EtlArtifactCompiler : IEtlArtifactCompiler, IEtlArtifactCompilerV2
{
    private const int MaximumArtifactBytes = EtlArtifact.MaximumByteLength;
    private const int MaximumDefinitionIdLength = 256;
    private const int MaximumDiagnostics = 32;
    private static readonly Regex DefinitionIdPattern = new("\\A[A-Za-z0-9][A-Za-z0-9._-]{0,255}\\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public EtlCompilationResult Compile(string definitionId, int definitionVersion, EtlPipeline pipeline)
    {
        var diagnostics = new DiagnosticCollector();

        ValidateDefinitionIdentity(definitionId, definitionVersion, diagnostics);
        if (pipeline is null)
        {
            diagnostics.Add("pipeline.required", "pipeline", "A pipeline definition is required.");
            return Failure(diagnostics);
        }

        var limits = pipeline.Limits;
        var requestedArtifactBytes = limits is { MaxArtifactBytes: > 0 } ? limits.MaxArtifactBytes : MaximumArtifactBytes;
        var effectiveArtifactBytes = Math.Min(requestedArtifactBytes, MaximumArtifactBytes);

        ValidateLimits(limits, pipeline, diagnostics);
        ValidateAliases(pipeline.Bindings, diagnostics);

        if (!CheckStructureAndStrings(pipeline, definitionId, effectiveArtifactBytes, diagnostics))
            return Failure(diagnostics);

        var source = ValidateSchema(pipeline.SourceSchema, "sourceSchema", limits, diagnostics);
        var lookup = ValidateSchema(pipeline.LookupSchema, "lookupSchema", limits, diagnostics);
        ValidateLookupKeys(pipeline, source, lookup, diagnostics);
        var mappedFields = ValidateMapping(pipeline.Mapping, source, lookup, diagnostics);
        ValidateFilters(pipeline.Filters, mappedFields, diagnostics);

        if (diagnostics.HasErrors)
            return Failure(diagnostics);

        JsonElement document;
        try
        {
            document = EtlArtifactJson.Write(pipeline);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            diagnostics.Add("pipeline.serialization-failed", "pipeline", "The pipeline could not be represented as a supported artifact.");
            return Failure(diagnostics);
        }

        if (Encoding.UTF8.GetByteCount(document.GetRawText()) > effectiveArtifactBytes)
        {
            diagnostics.Add("artifact.too-large", "artifact", "The serialized artifact exceeds the configured byte limit.");
            return Failure(diagnostics);
        }

        var formatVersion = EtlArtifact.CurrentFormatVersion;
        var contentHash = EtlArtifactJson.Hash(document);
        var artifactId = EtlArtifactJson.CreateArtifactId(formatVersion, definitionId, definitionVersion, contentHash);
        var artifact = new EtlArtifact(formatVersion, artifactId, contentHash, definitionId, definitionVersion, document);
        if (JsonSerializer.SerializeToUtf8Bytes(artifact).Length > effectiveArtifactBytes)
        {
            diagnostics.Add("artifact.too-large", "artifact", "The serialized artifact envelope exceeds the configured byte limit.");
            return Failure(diagnostics);
        }

        return new EtlCompilationResult(artifact, Array.Empty<EtlDiagnostic>());
    }

    private static void ValidateDefinitionIdentity(string? definitionId, int definitionVersion, DiagnosticCollector diagnostics)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            diagnostics.Add("definition.id.required", "definitionId", "A definition identifier is required.");
        else if (definitionId.Length > MaximumDefinitionIdLength || !DefinitionIdPattern.IsMatch(definitionId))
            diagnostics.Add("definition.id.invalid", "definitionId", "The definition identifier must be a path-safe identifier of at most 256 ASCII characters.");
        if (definitionVersion <= 0)
            diagnostics.Add("definition.version.invalid", "definitionVersion", "The definition version must be positive.");
    }

    private static void ValidateLimits(EtlLimits? limits, EtlPipeline pipeline, DiagnosticCollector diagnostics)
    {
        if (limits is null)
        {
            diagnostics.Add("limits.required", "limits", "Finite pipeline limits are required.");
            return;
        }

        if (limits.PartitionCount <= 0 ||
            limits.MaxRecordBytes <= 0 ||
            limits.MaxFields <= 0 ||
            limits.MaxBatchRecords <= 0 ||
            limits.MaxBatchBytes <= 0 ||
            limits.MaxOutstandingRecords <= 0 ||
            limits.MaxOutstandingBytes <= 0 ||
            limits.MaxLookupRecords <= 0 ||
            limits.MaxLookupBytes <= 0 ||
            limits.MaxOutputBatchBytes <= 0 ||
            limits.MaxSnapshotBytes <= 0 ||
            limits.MaxArtifactBytes <= 0)
            diagnostics.Add("limits.invalid", "limits", "Every configured limit must be positive.");

        if (limits.MaxBatchRecords > limits.MaxOutstandingRecords ||
            limits.MaxBatchBytes > limits.MaxOutstandingBytes ||
            limits.MaxRecordBytes > limits.MaxBatchBytes ||
            limits.MaxLookupBytes > limits.MaxSnapshotBytes)
            diagnostics.Add("limits.invalid", "limits", "Batch, lookup, and outstanding limits must fit within their enclosing limits.");

        if (limits.MaxArtifactBytes > MaximumArtifactBytes)
            diagnostics.Add("limits.artifact-too-large", "limits.maxArtifactBytes", "The v1 artifact limit cannot exceed 256 KiB.");

        if (limits.MaxFields > 0 &&
            (pipeline.SourceSchema?.Count > limits.MaxFields || pipeline.LookupSchema?.Count > limits.MaxFields))
            diagnostics.Add("limits.invalid", "limits.maxFields", "The field limit must cover each declared source and lookup schema.");
    }

    private static void ValidateAliases(EtlFileBindings? bindings, DiagnosticCollector diagnostics)
    {
        if (bindings is null)
        {
            diagnostics.Add("bindings.required", "bindings", "Host-configured file bindings are required.");
            return;
        }

        ValidateAlias(bindings.Source, "bindings.source", diagnostics);
        ValidateAlias(bindings.Lookup, "bindings.lookup", diagnostics);
        ValidateAlias(bindings.Output, "bindings.output", diagnostics);
        ValidateAlias(bindings.Errors, "bindings.errors", diagnostics);
    }

    private static void ValidateAlias(string? alias, string path, DiagnosticCollector diagnostics)
    {
        if (string.IsNullOrEmpty(alias) || alias.Length > 128 || !IsAsciiAlphaNumeric(alias[0]))
        {
            diagnostics.Add("binding.alias.invalid", path, "A binding alias must be 1 to 128 ASCII letters or digits, followed by letters, digits, '.', '_' or '-'.");
            return;
        }

        for (var index = 1; index < alias.Length; index++)
        {
            var character = alias[index];
            if (!IsAsciiAlphaNumeric(character) && character is not ('.' or '_' or '-'))
            {
                diagnostics.Add("binding.alias.invalid", path, "A binding alias must be 1 to 128 ASCII letters or digits, followed by letters, digits, '.', '_' or '-'.");
                return;
            }
        }
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool CheckStructureAndStrings(
        EtlPipeline pipeline,
        string? definitionId,
        int artifactLimit,
        DiagnosticCollector diagnostics)
    {
        var sourceCount = pipeline.SourceSchema?.Count ?? 0;
        var lookupCount = pipeline.LookupSchema?.Count ?? 0;
        var mappingCount = pipeline.Mapping?.Count ?? 0;
        var filterCount = pipeline.Filters?.Count ?? 0;
        var itemCount = (long)sourceCount + lookupCount + mappingCount + filterCount;
        // Every validated schema, mapping, or filter item has more than 32 bytes of fixed JSON structure
        // before its authored text. This cap is a lower-bound guard derived from the artifact byte limit;
        // the complete envelope is measured exactly after the typed representation is built.
        var maximumItems = artifactLimit / 32;
        if (itemCount > maximumItems)
        {
            diagnostics.Add("artifact.too-large", "pipeline", "The pipeline has more declared items than its artifact byte limit can hold.");
            return false;
        }

        var contentByteBudget = artifactLimit;
        if (!AddText(definitionId, ref contentByteBudget) ||
            pipeline.Bindings is { } bindings &&
            (!AddText(bindings.Source, ref contentByteBudget) ||
             !AddText(bindings.Lookup, ref contentByteBudget) ||
             !AddText(bindings.Output, ref contentByteBudget) ||
             !AddText(bindings.Errors, ref contentByteBudget)) ||
            !AddText(pipeline.SourceLookupField, ref contentByteBudget) ||
            !AddText(pipeline.LookupKeyField, ref contentByteBudget))
        {
            diagnostics.Add("artifact.too-large", "pipeline", "The pipeline text exceeds the configured artifact byte limit.");
            return false;
        }

        if (!AddSchemaText(pipeline.SourceSchema, ref contentByteBudget) ||
            !AddSchemaText(pipeline.LookupSchema, ref contentByteBudget) ||
            !AddMappingText(pipeline.Mapping, ref contentByteBudget) ||
            !AddFilterText(pipeline.Filters, ref contentByteBudget))
        {
            diagnostics.Add("artifact.too-large", "pipeline", "The pipeline text exceeds the configured artifact byte limit.");
            return false;
        }

        return true;
    }

    private static bool AddSchemaText(IReadOnlyList<EtlColumn>? schema, ref int byteBudget)
    {
        if (schema is null)
            return true;

        foreach (var column in schema)
            if (column is not null && !AddText(column.Name, ref byteBudget))
                return false;
        return true;
    }

    private static bool AddMappingText(IReadOnlyList<EtlOutputField>? mapping, ref int byteBudget)
    {
        if (mapping is null)
            return true;

        foreach (var field in mapping)
            if (field is not null &&
                (!AddText(field.Name, ref byteBudget) || !AddText(field.Field, ref byteBudget)))
                return false;
        return true;
    }

    private static bool AddFilterText(IReadOnlyList<EtlFilter>? filters, ref int byteBudget)
    {
        if (filters is null)
            return true;

        foreach (var filter in filters)
        {
            if (filter is null)
                continue;
            if (!AddText(filter.Field, ref byteBudget))
                return false;

            if (filter.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Undefined)
                continue;

            try
            {
                using var stream = new BoundedCountingStream(byteBudget);
                using (var writer = new Utf8JsonWriter(stream))
                {
                    filter.Value.WriteTo(writer);
                    writer.Flush();
                }
                byteBudget -= checked((int)stream.BytesWritten);
            }
            catch (SizeLimitExceededException)
            {
                return false;
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException)
            {
                // The validator reports an invalid JSON literal with the field path later.
            }
        }
        return true;
    }

    private static bool AddText(string? value, ref int byteBudget)
    {
        if (value is null)
            return true;
        if (value.Length > byteBudget)
            return false;

        try
        {
            using var stream = new BoundedCountingStream(byteBudget);
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStringValue(value);
                writer.Flush();
            }
            byteBudget -= checked((int)stream.BytesWritten);
            return true;
        }
        catch (SizeLimitExceededException)
        {
            return false;
        }
    }

    private static Dictionary<string, EtlColumn> ValidateSchema(
        IReadOnlyList<EtlColumn>? schema,
        string path,
        EtlLimits? limits,
        DiagnosticCollector diagnostics) => ValidateSchema(schema, path, limits?.MaxFields, diagnostics);

    private static Dictionary<string, EtlColumn> ValidateSchema(
        IReadOnlyList<EtlColumn>? schema,
        string path,
        int? maximumFields,
        DiagnosticCollector diagnostics)
    {
        var fields = new Dictionary<string, EtlColumn>(StringComparer.Ordinal);
        if (schema is null || schema.Count == 0)
        {
            diagnostics.Add("schema.required", path, "A nonempty schema is required.");
            return fields;
        }

        if (maximumFields is > 0 && schema.Count > maximumFields)
            diagnostics.Add("schema.field-count.exceeded", path, "The schema contains more fields than the configured maximum.");

        for (var index = 0; index < schema.Count; index++)
        {
            var column = schema[index];
            var columnPath = $"{path}[{index}]";
            if (column is null)
            {
                diagnostics.Add("schema.field.required", columnPath, "A schema field is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(column.Name))
                diagnostics.Add("schema.field.name.required", $"{columnPath}.name", "A schema field name is required.");
            else if (!fields.TryAdd(column.Name, column))
                diagnostics.Add("schema.field.duplicate", $"{columnPath}.name", "Schema field names must be unique using ordinal comparison.");

            if (!Enum.IsDefined(column.Type))
                diagnostics.Add("schema.field.type.invalid", $"{columnPath}.type", "The schema field type is not supported.");
        }

        return fields;
    }

    private static void ValidateLookupKeys(
        EtlPipeline pipeline,
        IReadOnlyDictionary<string, EtlColumn> source,
        IReadOnlyDictionary<string, EtlColumn> lookup,
        DiagnosticCollector diagnostics) => ValidateLookupKeys(
            pipeline.SourceLookupField, pipeline.LookupKeyField, "sourceLookupField", "lookupKeyField", source, lookup, diagnostics);

    private static void ValidateLookupKeys(
        string? sourceKey,
        string? lookupKey,
        string sourcePath,
        string lookupPath,
        IReadOnlyDictionary<string, EtlColumn> source,
        IReadOnlyDictionary<string, EtlColumn> lookup,
        DiagnosticCollector diagnostics)
    {
        var sourceField = FindField(source, sourceKey);
        var lookupField = FindField(lookup, lookupKey);
        if (sourceField is null)
            diagnostics.Add("lookup.source-key.missing", sourcePath, "The source lookup field must exist in the source schema.");
        else if (!sourceField.Required)
            diagnostics.Add("lookup.source-key.required", sourcePath, "The source lookup field must be required.");

        if (lookupField is null)
            diagnostics.Add("lookup.key.missing", lookupPath, "The lookup key field must exist in the lookup schema.");
        else if (!lookupField.Required)
            diagnostics.Add("lookup.key.required", lookupPath, "The lookup key field must be required.");

        if (sourceField is not null && lookupField is not null && sourceField.Type != lookupField.Type)
            diagnostics.Add("lookup.key.type-mismatch", lookupPath, "Source and lookup key fields must have the same type.");
    }

    private static Dictionary<string, EtlColumn> ValidateMapping(
        IReadOnlyList<EtlOutputField>? mapping,
        IReadOnlyDictionary<string, EtlColumn> source,
        IReadOnlyDictionary<string, EtlColumn> lookup,
        DiagnosticCollector diagnostics)
    {
        var outputs = new Dictionary<string, EtlColumn>(StringComparer.Ordinal);
        if (mapping is null || mapping.Count == 0)
        {
            diagnostics.Add("mapping.required", "mapping", "At least one output field mapping is required.");
            return outputs;
        }

        for (var index = 0; index < mapping.Count; index++)
        {
            var entry = mapping[index];
            var path = $"mapping[{index}]";
            if (entry is null)
            {
                diagnostics.Add("mapping.field.required", path, "An output mapping is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                diagnostics.Add("mapping.name.required", $"{path}.name", "An output field name is required.");
                continue;
            }

            var isNewName = !outputs.ContainsKey(entry.Name);
            if (!isNewName)
                diagnostics.Add("mapping.name.duplicate", $"{path}.name", "Output field names must be unique using ordinal comparison.");

            if (!Enum.IsDefined(entry.Origin))
            {
                diagnostics.Add("mapping.origin.invalid", $"{path}.origin", "The input field origin is not supported.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Field))
            {
                diagnostics.Add("mapping.field.name.required", $"{path}.field", "An input field name is required.");
                continue;
            }

            var fields = entry.Origin == EtlFieldOrigin.Source ? source : lookup;
            if (!fields.TryGetValue(entry.Field, out var column))
            {
                diagnostics.Add("mapping.field.missing", $"{path}.field", "The mapped field must exist in the selected schema.");
                continue;
            }

            if (isNewName)
                outputs.Add(entry.Name, column);
        }

        return outputs;
    }

    private static void ValidateFilters(
        IReadOnlyList<EtlFilter>? filters,
        IReadOnlyDictionary<string, EtlColumn> outputs,
        DiagnosticCollector diagnostics)
    {
        if (filters is null)
        {
            diagnostics.Add("filters.required", "filters", "A filter list is required.");
            return;
        }

        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            var path = $"filters[{index}]";
            if (filter is null)
            {
                diagnostics.Add("filter.required", path, "A filter definition is required.");
                continue;
            }

            if (!outputs.TryGetValue(filter.Field, out var column))
            {
                diagnostics.Add("filter.field.missing", $"{path}.field", "A filter must refer to a mapped output field.");
                continue;
            }

            if (!Enum.IsDefined(filter.Comparison))
                diagnostics.Add("filter.comparison.unsupported", $"{path}.comparison", "Only equality and inequality comparisons are supported.");

            if (filter.Value.ValueKind == JsonValueKind.Null)
            {
                if (column.Required)
                    diagnostics.Add("filter.value.null-required", $"{path}.value", "A null filter literal requires an optional mapped field.");
                continue;
            }

            if (!IsCompatibleLiteral(filter.Value, column.Type))
                diagnostics.Add("filter.value.incompatible", $"{path}.value", "The filter literal must match the mapped field type.");
        }
    }

    private static bool IsCompatibleLiteral(JsonElement value, EtlColumnType type) => type switch
    {
        EtlColumnType.String => value.ValueKind == JsonValueKind.String,
        EtlColumnType.Int64 => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        EtlColumnType.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
        EtlColumnType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => false
    };

    private static EtlColumn? FindField(IReadOnlyDictionary<string, EtlColumn> fields, string? name) =>
        name is not null && fields.TryGetValue(name, out var field) ? field : null;

    private static EtlCompilationResult Failure(DiagnosticCollector diagnostics) =>
        new(null, diagnostics.Items.ToArray());

    private sealed class DiagnosticCollector
    {
        private readonly List<EtlDiagnostic> _items = [];
        private bool _truncated;

        public IReadOnlyList<EtlDiagnostic> Items => _items;
        public bool HasErrors => _items.Count > 0;

        public void Add(string code, string path, string message)
        {
            if (_items.Count < MaximumDiagnostics - 1)
            {
                _items.Add(new EtlDiagnostic(code, path, message));
                return;
            }

            if (!_truncated)
            {
                _items.Add(new EtlDiagnostic("diagnostics.truncated", "pipeline", "Additional validation diagnostics were omitted."));
                _truncated = true;
            }
        }
    }

    private sealed class BoundedCountingStream(int maximumBytes) : Stream
    {
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Add(count);
        public override void Write(ReadOnlySpan<byte> buffer) => Add(buffer.Length);
        public override void WriteByte(byte value) => Add(1);
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        private void Add(int count)
        {
            if (count > maximumBytes - BytesWritten)
                throw new SizeLimitExceededException();
            BytesWritten += count;
        }
    }

    private sealed class SizeLimitExceededException : Exception;
}
