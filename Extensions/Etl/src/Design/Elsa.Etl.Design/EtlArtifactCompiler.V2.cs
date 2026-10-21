using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;

namespace Elsa.Etl.Design;

internal sealed partial class EtlArtifactCompiler
{
    public EtlCompilationResult Compile(string definitionId, int definitionVersion, EtlPipelineV2 pipeline)
    {
        var diagnostics = new DiagnosticCollector();
        ValidateDefinitionIdentity(definitionId, definitionVersion, diagnostics);
        if (pipeline is null)
        {
            diagnostics.Add("pipeline.required", "pipeline", "A pipeline definition is required.");
            return Failure(diagnostics);
        }

        ValidateConnectorLimits(pipeline.Limits, pipeline.Http, diagnostics);
        var artifactLimit = pipeline.Limits is { MaxArtifactBytes: > 0 } limits
            ? Math.Min(limits.MaxArtifactBytes, MaximumArtifactBytes)
            : MaximumArtifactBytes;
        if (!CheckConnectorStructureAndStrings(pipeline, definitionId, artifactLimit))
        {
            diagnostics.Add("artifact.too-large", "pipeline", "The pipeline declarations exceed the artifact byte limit.");
            return Failure(diagnostics);
        }

        var source = ValidateConnectorSource(pipeline.Source, pipeline.Limits?.MaxFields, diagnostics);
        var lookup = new Dictionary<string, EtlColumn>(StringComparer.Ordinal);
        if (pipeline.Lookup is { } declaration)
        {
            ValidateAlias(declaration.Binding, "lookup.binding", diagnostics);
            lookup = ValidateSchema(declaration.Schema, "lookup.schema", pipeline.Limits?.MaxFields, diagnostics);
            ValidateLookupKeys(declaration.SourceKeyField, declaration.LookupKeyField,
                "lookup.sourceKeyField", "lookup.lookupKeyField", source, lookup, diagnostics);
        }
        if (pipeline.Routes is null)
            diagnostics.Add("routes.required", "routes", "Both output routes must be declared.");
        else
        {
            ValidateDestination(pipeline.Routes.Main, "routes.main", diagnostics);
            ValidateDestination(pipeline.Routes.Errors, "routes.errors", diagnostics);
        }
        var mapped = ValidateMapping(pipeline.Mapping, source, lookup, diagnostics);
        ValidateFilters(pipeline.Filters, mapped, diagnostics);
        if (diagnostics.HasErrors)
            return Failure(diagnostics);

        JsonElement document;
        try
        {
            document = EtlArtifactV2Json.Write(pipeline);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
        {
            diagnostics.Add("pipeline.serialization-failed", "pipeline", "The pipeline cannot be represented as a supported v2 artifact.");
            return Failure(diagnostics);
        }
        if (Encoding.UTF8.GetByteCount(document.GetRawText()) > artifactLimit)
        {
            diagnostics.Add("artifact.too-large", "artifact", "The serialized artifact exceeds the configured byte limit.");
            return Failure(diagnostics);
        }

        var hash = EtlArtifactJson.Hash(document);
        var artifact = new EtlArtifact(EtlArtifactV2Json.FormatVersion,
            EtlArtifactJson.CreateArtifactId(EtlArtifactV2Json.FormatVersion, definitionId, definitionVersion, hash),
            hash, definitionId, definitionVersion, document);
        if (JsonSerializer.SerializeToUtf8Bytes(artifact).Length > artifactLimit)
        {
            diagnostics.Add("artifact.too-large", "artifact", "The serialized artifact envelope exceeds the configured byte limit.");
            return Failure(diagnostics);
        }
        return new EtlCompilationResult(artifact, Array.Empty<EtlDiagnostic>());
    }

    private static void ValidateConnectorLimits(EtlConnectorLimits? limits, EtlHttpPolicy? http, DiagnosticCollector diagnostics)
    {
        if (limits is null)
            diagnostics.Add("limits.required", "limits", "Finite pipeline limits are required.");
        else
        {
            if (limits.FilePartitionCount <= 0 || limits.MaxPartitions <= 0 || limits.FilePartitionCount > limits.MaxPartitions ||
                limits.MaxRecordBytes <= 0 || limits.MaxFields <= 0 || limits.MaxBatchRecords <= 0 ||
                limits.MaxBatchBytes <= 0 || limits.MaxOutstandingRecords <= 0 || limits.MaxOutstandingBytes <= 0 ||
                limits.MaxLookupRecords <= 0 || limits.MaxLookupBytes <= 0 || limits.MaxOutputBatchBytes <= 0 ||
                limits.MaxSnapshotBytes <= 0 || limits.MaxArtifactBytes <= 0 ||
                limits.MaxBatchRecords > limits.MaxOutstandingRecords || limits.MaxBatchBytes > limits.MaxOutstandingBytes ||
                limits.MaxRecordBytes > limits.MaxBatchBytes || limits.MaxLookupBytes > limits.MaxSnapshotBytes)
                diagnostics.Add("limits.invalid", "limits", "Positive partition, batch, lookup and outstanding limits must fit their enclosing limits.");
            if (limits.MaxArtifactBytes > MaximumArtifactBytes)
                diagnostics.Add("limits.artifact-too-large", "limits.maxArtifactBytes", "The artifact limit cannot exceed 256 KiB.");
        }
        if (http is null)
            diagnostics.Add("http.required", "http", "Finite HTTP page and request policy must be declared.");
        else if (http.MaxPageBytes <= 0 || http.MaxPageRecords <= 0 || http.MaxJsonDepth is <= 0 or > 64 ||
                 http.MaxContinuationBytes <= 0 || http.MaxContinuationBytes > EtlHttpPolicy.MaximumSupportedContinuationBytes ||
                 http.MaxPagesPerPartition <= 0 || http.MaxRetainedInputBytes <= 0 ||
                 http.MaxAttempts <= 0 || http.DeadlineSeconds <= 0 || http.RequestTimeoutSeconds <= 0 ||
                 http.InitialDelaySeconds <= 0 || http.MaxDelaySeconds <= 0 ||
                 http.MaxPageBytes > http.MaxRetainedInputBytes || http.MaxContinuationBytes > http.MaxPageBytes ||
                 http.RequestTimeoutSeconds > http.DeadlineSeconds || http.InitialDelaySeconds > http.MaxDelaySeconds ||
                 http.MaxDelaySeconds > http.DeadlineSeconds)
            diagnostics.Add("http.limits.invalid", "http", "Positive HTTP bounds must fit their page, retention and deadline limits; JSON depth cannot exceed 64.");
    }

    private static Dictionary<string, EtlColumn> ValidateConnectorSource(EtlSource? source, int? maximumFields, DiagnosticCollector diagnostics)
    {
        switch (source)
        {
            case EtlCsvSource csv:
                ValidateAlias(csv.Binding, "source.binding", diagnostics);
                return ValidateSchema(csv.Schema, "source.schema", maximumFields, diagnostics);
            case EtlHttpJsonSource http:
                ValidateAlias(http.Binding, "source.binding", diagnostics);
                ValidatePointer(http.RecordsPointer, "source.recordsPointer", diagnostics);
                ValidatePagination(http.Pagination, diagnostics);
                if (http.Fields is null)
                    return ValidateSchema(null, "source.fields", maximumFields, diagnostics);
                var columns = new EtlColumn[http.Fields.Count];
                for (var index = 0; index < http.Fields.Count; index++)
                {
                    var field = http.Fields[index];
                    if (field is null)
                    {
                        columns[index] = null!;
                        continue;
                    }
                    ValidatePointer(field.Pointer, $"source.fields[{index}].pointer", diagnostics);
                    columns[index] = new EtlColumn(field.Name, field.Type, field.Required);
                }
                // Share scalar declaration rules without treating JSON records as CSV at execution time.
                return ValidateSchema(columns, "source.fields", maximumFields, diagnostics);
            default:
                diagnostics.Add(source is null ? "source.required" : "source.unsupported", "source", "A supported input source is required.");
                return new Dictionary<string, EtlColumn>(StringComparer.Ordinal);
        }
    }

    private static void ValidateDestination(EtlDestination? destination, string path, DiagnosticCollector diagnostics)
    {
        switch (destination)
        {
            case EtlJsonlDestination file: ValidateAlias(file.Binding, path + ".binding", diagnostics); break;
            case EtlHttpDestination http: ValidateAlias(http.Binding, path + ".binding", diagnostics); break;
            default:
                diagnostics.Add(destination is null ? "destination.required" : "destination.unsupported", path, "A supported output destination is required.");
                break;
        }
    }

    private static void ValidatePagination(EtlHttpPagination? pagination, DiagnosticCollector diagnostics)
    {
        switch (pagination)
        {
            case EtlSinglePagePagination: break;
            case EtlNextLinkPagination link:
                ValidatePointer(link.ContinuationPointer, "source.pagination.continuationPointer", diagnostics);
                break;
            case EtlTokenPagination token:
                ValidatePointer(token.ContinuationPointer, "source.pagination.continuationPointer", diagnostics);
                if (token.QueryParameter is not { Length: > 0 and <= 128 } ||
                    token.QueryParameter.Any(char.IsControl) || Encoding.UTF8.GetByteCount(token.QueryParameter) > 128)
                    diagnostics.Add("pagination.parameter.invalid", "source.pagination.queryParameter", "The query parameter must be nonempty, contain no control characters and fit within 128 UTF-8 bytes.");
                break;
            default:
                diagnostics.Add(pagination is null ? "pagination.required" : "pagination.unsupported", "source.pagination", "A supported pagination declaration is required.");
                break;
        }
    }

    private static void ValidatePointer(string? pointer, string path, DiagnosticCollector diagnostics)
    {
        var valid = pointer is not null && (pointer.Length == 0 || pointer[0] == '/');
        if (valid)
            for (var index = 0; index < pointer!.Length; index++)
                if (pointer[index] == '~' && (++index >= pointer.Length || pointer[index] is not ('0' or '1')))
                {
                    valid = false;
                    break;
                }
        if (!valid)
            diagnostics.Add("json.pointer.invalid", path, "A JSON Pointer must be empty or start with '/', using only '~0' and '~1' escapes.");
    }

    private static bool CheckConnectorStructureAndStrings(EtlPipelineV2 pipeline, string definitionId, int budget)
    {
        var sourceCount = pipeline.Source switch { EtlCsvSource csv => csv.Schema?.Count ?? 0, EtlHttpJsonSource http => http.Fields?.Count ?? 0, _ => 0 };
        var itemCount = (long)sourceCount + (pipeline.Lookup?.Schema?.Count ?? 0) + (pipeline.Mapping?.Count ?? 0) + (pipeline.Filters?.Count ?? 0);
        if (itemCount > budget / 32 || !AddText(definitionId, ref budget))
            return false;
        switch (pipeline.Source)
        {
            case EtlCsvSource csv:
                if (!AddText(csv.Binding, ref budget) || !AddSchemaText(csv.Schema, ref budget)) return false;
                break;
            case EtlHttpJsonSource http:
                if (!AddText(http.Binding, ref budget) || !AddText(http.RecordsPointer, ref budget)) return false;
                if (http.Fields is not null)
                    foreach (var field in http.Fields)
                        if (field is not null && (!AddText(field.Name, ref budget) || !AddText(field.Pointer, ref budget))) return false;
                if (http.Pagination is EtlTokenPagination token &&
                    (!AddText(token.ContinuationPointer, ref budget) || !AddText(token.QueryParameter, ref budget))) return false;
                if (http.Pagination is EtlNextLinkPagination link && !AddText(link.ContinuationPointer, ref budget)) return false;
                break;
        }
        if (pipeline.Lookup is { } lookup && (!AddText(lookup.Binding, ref budget) || !AddSchemaText(lookup.Schema, ref budget) ||
            !AddText(lookup.SourceKeyField, ref budget) || !AddText(lookup.LookupKeyField, ref budget))) return false;
        if (!AddDestinationText(pipeline.Routes?.Main, ref budget) || !AddDestinationText(pipeline.Routes?.Errors, ref budget) ||
            !AddMappingText(pipeline.Mapping, ref budget)) return false;
        if (pipeline.Filters is not null)
            foreach (var filter in pipeline.Filters)
                if (filter is not null && filter.Value.ValueKind != JsonValueKind.Undefined && JsonMarshal.GetRawUtf8Value(filter.Value).Length > budget)
                    return false;
        return AddFilterText(pipeline.Filters, ref budget);
    }

    private static bool AddDestinationText(EtlDestination? destination, ref int budget) => destination switch
    {
        EtlJsonlDestination file => AddText(file.Binding, ref budget),
        EtlHttpDestination http => AddText(http.Binding, ref budget),
        _ => true
    };
}
