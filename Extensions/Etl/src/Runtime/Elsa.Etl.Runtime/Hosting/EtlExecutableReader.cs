using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Csv;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Hosting;

/// <summary>Validates one stored executable artifact before the runtime starts file work.</summary>
internal sealed class EtlExecutableReader(EtlRuntimeOptions options)
{
    private const int MaximumFieldCount = 1024;
    private const int MaximumDefinitionIdLength = 256;

    public EtlPipeline Read(EtlArtifact artifact)
    {
        if (artifact is null)
            throw Refused("etl.executable.invalid-envelope");

        try
        {
            ValidateEnvelopeHeader(artifact);
            if (artifact.Pipeline.ValueKind != JsonValueKind.Object)
                throw Refused("etl.executable.invalid-pipeline");

            if (JsonMarshal.GetRawUtf8Value(artifact.Pipeline).Length > EtlArtifact.MaximumByteLength)
                throw Refused("etl.executable.too-large");
            var rawPipeline = artifact.Pipeline.GetRawText();

            // This envelope measurement is done before typed deserialization. The raw pipeline has already
            // been bounded, and the scalar identity fields were bounded above.
            var envelopeSize = JsonSerializer.SerializeToUtf8Bytes(artifact).Length;
            if (envelopeSize > EtlArtifact.MaximumByteLength)
                throw Refused("etl.executable.too-large");

            var pipeline = EtlArtifactJson.Read(artifact.Pipeline);
            ValidatePipeline(pipeline);

            var effectiveLimit = Math.Min(EtlArtifact.MaximumByteLength, pipeline.Limits.MaxArtifactBytes);
            if (Encoding.UTF8.GetByteCount(rawPipeline) > effectiveLimit || envelopeSize > effectiveLimit)
                throw Refused("etl.executable.too-large");

            var canonicalPipeline = EtlArtifactJson.Write(pipeline);
            if (!string.Equals(rawPipeline, canonicalPipeline.GetRawText(), StringComparison.Ordinal))
                throw Refused("etl.executable.noncanonical-pipeline");

            var hash = EtlArtifactJson.Hash(canonicalPipeline);
            if (!string.Equals(artifact.ContentHash, hash, StringComparison.Ordinal) ||
                !string.Equals(artifact.ArtifactId, EtlArtifactJson.CreateArtifactId(artifact.FormatVersion, artifact.DefinitionId, artifact.DefinitionVersion, hash), StringComparison.Ordinal))
                throw Refused("etl.executable.provenance-invalid");

            return pipeline;
        }
        catch (EtlExecutableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or
                                          NullReferenceException or KeyNotFoundException or OverflowException)
        {
            throw Refused("etl.executable.invalid-pipeline");
        }
    }

    private static void ValidateEnvelopeHeader(EtlArtifact artifact)
    {
        if (artifact.FormatVersion != EtlArtifact.CurrentFormatVersion || artifact.DefinitionVersion <= 0 ||
            !ValidDefinitionId(artifact.DefinitionId) || artifact.ArtifactId.Length > 96 || artifact.ContentHash.Length != 64)
            throw Refused("etl.executable.invalid-envelope");
    }

    private void ValidatePipeline(EtlPipeline pipeline)
    {
        if (pipeline is null || pipeline.Bindings is null || pipeline.SourceSchema is null || pipeline.LookupSchema is null ||
            pipeline.Mapping is null || pipeline.Filters is null || pipeline.Limits is null ||
            !ValidAlias(pipeline.Bindings.Source) || !ValidAlias(pipeline.Bindings.Lookup) ||
            !ValidAlias(pipeline.Bindings.Output) || !ValidAlias(pipeline.Bindings.Errors))
            throw Refused("etl.executable.invalid-pipeline");

        var limits = pipeline.Limits;
        if (limits.PartitionCount <= 0 || limits.PartitionCount > options.MaxPartitions ||
            limits.MaxRecordBytes <= 0 || limits.MaxFields <= 0 || limits.MaxFields > MaximumFieldCount ||
            limits.MaxBatchRecords <= 0 || limits.MaxBatchBytes <= 0 || limits.MaxOutstandingRecords <= 0 ||
            limits.MaxOutstandingBytes <= 0 || limits.MaxLookupRecords <= 0 || limits.MaxLookupBytes <= 0 ||
            limits.MaxOutputBatchBytes <= 0 || limits.MaxSnapshotBytes <= 0 || limits.MaxArtifactBytes <= 0 ||
            limits.MaxArtifactBytes > EtlArtifact.MaximumByteLength ||
            limits.MaxBatchRecords > limits.MaxOutstandingRecords || limits.MaxBatchBytes > limits.MaxOutstandingBytes ||
            limits.MaxRecordBytes > limits.MaxBatchBytes || limits.MaxLookupBytes > limits.MaxSnapshotBytes ||
            limits.MaxBatchRecords > options.Capacity.Records || limits.MaxBatchBytes > options.Capacity.InputBytes ||
            limits.MaxRecordBytes > options.Capacity.InputBytes ||
            limits.MaxLookupRecords > options.MaxLookupRecords ||
            pipeline.SourceSchema.Count == 0 || pipeline.LookupSchema.Count == 0 ||
            limits.MaxLookupRecords > options.MaxLookupCells / pipeline.LookupSchema.Count ||
            limits.MaxLookupBytes > options.Capacity.LookupBytes || limits.MaxOutputBatchBytes > options.Capacity.OutputBytes ||
            limits.MaxSnapshotBytes > options.MaxSnapshotBytes ||
            pipeline.SourceSchema.Count > limits.MaxFields || pipeline.LookupSchema.Count > limits.MaxFields ||
            pipeline.Mapping.Count == 0 || pipeline.Mapping.Count > 2 * MaximumFieldCount || pipeline.Filters.Count > 2 * MaximumFieldCount)
            throw Refused("etl.executable.limits-exceeded");

        ValidateSchema(pipeline.SourceSchema);
        ValidateSchema(pipeline.LookupSchema);
        if (pipeline.Mapping.Any(field => field is null || string.IsNullOrWhiteSpace(field.Name) || string.IsNullOrWhiteSpace(field.Field)) ||
            pipeline.Filters.Any(filter => filter is null || string.IsNullOrWhiteSpace(filter.Field) || filter.Value.ValueKind == JsonValueKind.Undefined))
            throw Refused("etl.executable.invalid-pipeline");

        var sourceKey = pipeline.SourceSchema.FirstOrDefault(column => column.Name == pipeline.SourceLookupField);
        var lookupKey = pipeline.LookupSchema.FirstOrDefault(column => column.Name == pipeline.LookupKeyField);
        if (sourceKey is null || lookupKey is null || !sourceKey.Required || !lookupKey.Required || sourceKey.Type != lookupKey.Type)
            throw Refused("etl.executable.invalid-pipeline");

        if (!EtlRowTransformer.TryCompileHeader(pipeline.SourceSchema,
                new CsvRecord(0, pipeline.SourceSchema.Select(column => column.Name).ToArray()), out var sourceHeader, out _) ||
            !EtlRowTransformer.TryCompileHeader(pipeline.LookupSchema,
                new CsvRecord(0, pipeline.LookupSchema.Select(column => column.Name).ToArray()), out _, out _) ||
            sourceHeader is null)
            throw Refused("etl.executable.invalid-schema");

        // The transformer is the runtime's actual mapping/filter compiler. An empty lookup is sufficient to
        // validate every field reference and typed filter without opening a host file.
        _ = new EtlRowTransformer(pipeline, sourceHeader, new Dictionary<EtlTypedValue, EtlTypedRow>());
    }

    private static void ValidateSchema(IReadOnlyList<EtlColumn> schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in schema)
        {
            if (column is null || string.IsNullOrWhiteSpace(column.Name) || !Enum.IsDefined(column.Type) || !names.Add(column.Name))
                throw Refused("etl.executable.invalid-schema");
        }
    }

    private static bool ValidAlias(string? alias)
    {
        if (alias is not { Length: > 0 and <= 128 } || !char.IsAsciiLetterOrDigit(alias[0]))
            return false;
        return alias.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static bool ValidDefinitionId(string? value)
    {
        if (value is not { Length: > 0 and <= MaximumDefinitionIdLength } || !char.IsAsciiLetterOrDigit(value[0]))
            return false;
        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static EtlExecutableException Refused(string code) => new(code);
}

internal sealed class EtlExecutableException(string code)
    : Exception("The published ETL executable is invalid or exceeds host runtime limits.")
{
    public string Code { get; } = code;
}
