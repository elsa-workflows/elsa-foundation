using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Hosting;

/// <summary>Validates connector artifacts using runtime semantics and independent host ceilings.</summary>
/// <remarks>Reading an artifact does not admit work or establish connector-store/transport availability.</remarks>
internal sealed class EtlConnectorExecutableReader(EtlRuntimeOptions options)
{
    public EtlPipelineV2 Read(EtlArtifact artifact)
    {
        try
        {
            if (artifact is null || artifact.FormatVersion != EtlArtifactV2Json.FormatVersion || artifact.DefinitionVersion <= 0 ||
                !ValidIdentifier(artifact.DefinitionId, 256) || artifact.ArtifactId is not { Length: > 0 and <= 96 } ||
                artifact.ContentHash is not { Length: 64 })
                throw Refused("etl.executable.invalid-envelope");
            if (artifact.Pipeline.ValueKind != JsonValueKind.Object)
                throw Refused("etl.executable.invalid-pipeline");
            if (JsonMarshal.GetRawUtf8Value(artifact.Pipeline).Length > EtlArtifact.MaximumByteLength)
                throw Refused("etl.executable.too-large");
            var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(artifact).Length;
            if (envelopeBytes > EtlArtifact.MaximumByteLength)
                throw Refused("etl.executable.too-large");

            var pipeline = EtlArtifactV2Json.Read(artifact.Pipeline);
            ValidateLimits(pipeline);
            if (envelopeBytes > pipeline.Limits.MaxArtifactBytes)
                throw Refused("etl.executable.too-large");
            var canonical = EtlArtifactV2Json.Write(pipeline);
            if (!StringComparer.Ordinal.Equals(artifact.Pipeline.GetRawText(), canonical.GetRawText()))
                throw Refused("etl.executable.noncanonical-pipeline");
            var hash = EtlArtifactJson.Hash(canonical);
            if (!StringComparer.Ordinal.Equals(artifact.ContentHash, hash) ||
                !StringComparer.Ordinal.Equals(artifact.ArtifactId,
                    EtlArtifactJson.CreateArtifactId(artifact.FormatVersion, artifact.DefinitionId, artifact.DefinitionVersion, hash)))
                throw Refused("etl.executable.provenance-invalid");
            ValidateConnectors(pipeline);
            EtlRowTransformer.ValidateV2Semantics(pipeline);
            return pipeline;
        }
        catch (EtlExecutableException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or
                                          NullReferenceException or KeyNotFoundException or OverflowException or NotSupportedException)
        {
            // The outer diagnostic deliberately retains no raw authored value or parser exception.
            throw Refused("etl.executable.invalid-pipeline");
        }
    }

    private void ValidateLimits(EtlPipelineV2 pipeline)
    {
        var limits = pipeline.Limits;
        var http = pipeline.Http;
        var host = options.HttpLimits;
        if (limits.FilePartitionCount <= 0 || limits.MaxPartitions <= 0 || limits.FilePartitionCount > limits.MaxPartitions ||
            limits.MaxPartitions > options.MaxPartitions || limits.MaxRecordBytes <= 0 || limits.MaxFields is <= 0 or > 1024 ||
            limits.MaxBatchRecords <= 0 || limits.MaxBatchBytes <= 0 || limits.MaxOutstandingRecords <= 0 || limits.MaxOutstandingBytes <= 0 ||
            limits.MaxLookupRecords <= 0 || limits.MaxLookupBytes <= 0 || limits.MaxOutputBatchBytes <= 0 ||
            limits.MaxSnapshotBytes <= 0 || limits.MaxArtifactBytes is <= 0 or > EtlArtifact.MaximumByteLength ||
            limits.MaxBatchRecords > limits.MaxOutstandingRecords || limits.MaxBatchBytes > limits.MaxOutstandingBytes ||
            limits.MaxRecordBytes > limits.MaxBatchBytes || limits.MaxLookupBytes > limits.MaxSnapshotBytes ||
            limits.MaxBatchRecords > options.Capacity.Records || limits.MaxBatchBytes > options.Capacity.InputBytes ||
            limits.MaxOutputBatchBytes > options.Capacity.OutputBytes || limits.MaxSnapshotBytes > options.MaxSnapshotBytes)
            throw Refused("etl.executable.limits-exceeded");
        var sourceFields = pipeline.Source switch { EtlCsvSource csv => csv.Schema.Count, EtlHttpJsonSource json => json.Fields.Count, _ => 0 };
        if (sourceFields <= 0 || sourceFields > limits.MaxFields || pipeline.Mapping.Count is <= 0 or > 2048 || pipeline.Filters.Count > 2048)
            throw Refused("etl.executable.limits-exceeded");
        if (pipeline.Lookup is { } lookup &&
            (lookup.Schema.Count <= 0 || lookup.Schema.Count > limits.MaxFields ||
             limits.MaxLookupRecords > options.MaxLookupRecords || limits.MaxLookupRecords > options.MaxLookupCells / lookup.Schema.Count ||
             limits.MaxLookupBytes > options.Capacity.LookupBytes))
            throw Refused("etl.executable.limits-exceeded");
        if (http.MaxPageBytes <= 0 || http.MaxPageRecords <= 0 || http.MaxJsonDepth is <= 0 or > 64 || http.MaxContinuationBytes <= 0 ||
            http.MaxContinuationBytes > EtlHttpPolicy.MaximumSupportedContinuationBytes ||
            http.MaxPagesPerPartition <= 0 || http.MaxRetainedInputBytes <= 0 || http.MaxAttempts <= 0 || http.DeadlineSeconds <= 0 ||
            http.RequestTimeoutSeconds <= 0 || http.InitialDelaySeconds <= 0 || http.MaxDelaySeconds <= 0 ||
            http.MaxPageBytes > http.MaxRetainedInputBytes || http.MaxContinuationBytes > http.MaxPageBytes ||
            http.RequestTimeoutSeconds > http.DeadlineSeconds || http.InitialDelaySeconds > http.MaxDelaySeconds || http.MaxDelaySeconds > http.DeadlineSeconds ||
            http.MaxPageBytes > host.MaxPageBytes || http.MaxPageRecords > host.MaxPageRecords || http.MaxJsonDepth > host.MaxJsonDepth ||
            http.MaxContinuationBytes > host.MaxContinuationBytes || http.MaxPagesPerPartition > host.MaxPagesPerPartition ||
            http.MaxRetainedInputBytes > host.MaxRetainedInputBytes || http.MaxAttempts > host.MaxAttempts ||
            http.DeadlineSeconds > host.MaxDeadlineSeconds || http.RequestTimeoutSeconds > host.MaxRequestTimeoutSeconds || http.MaxDelaySeconds > host.MaxDelaySeconds)
            throw Refused("etl.executable.limits-exceeded");
        if (pipeline.Source is EtlHttpJsonSource && (http.MaxPageBytes > options.Capacity.InputBytes || http.MaxPageRecords > options.Capacity.Records))
            throw Refused("etl.executable.limits-exceeded");
    }

    private static void ValidateConnectors(EtlPipelineV2 pipeline)
    {
        switch (pipeline.Source)
        {
            case EtlCsvSource csv: RequireAlias(csv.Binding); break;
            case EtlHttpJsonSource json:
                RequireAlias(json.Binding);
                _ = new EtlJsonPointer(json.RecordsPointer);
                foreach (var field in json.Fields) _ = new EtlJsonPointer(field.Pointer);
                switch (json.Pagination)
                {
                    case EtlSinglePagePagination: break;
                    case EtlNextLinkPagination link: _ = new EtlJsonPointer(link.ContinuationPointer); break;
                    case EtlTokenPagination token:
                        _ = new EtlJsonPointer(token.ContinuationPointer);
                        if (token.QueryParameter is not { Length: > 0 and <= 128 } || token.QueryParameter.Any(char.IsControl) ||
                            Encoding.UTF8.GetByteCount(token.QueryParameter) > 128)
                            throw Refused("etl.executable.invalid-pipeline");
                        break;
                    default: throw Refused("etl.executable.invalid-pipeline");
                }
                break;
            default: throw Refused("etl.executable.invalid-pipeline");
        }
        if (pipeline.Lookup is { } lookup) RequireAlias(lookup.Binding);
        foreach (var destination in new[] { pipeline.Routes.Main, pipeline.Routes.Errors })
            RequireAlias(destination switch { EtlJsonlDestination file => file.Binding, EtlHttpDestination http => http.Binding, _ => null });
    }

    private static void RequireAlias(string? value)
    {
        if (!ValidIdentifier(value, 128)) throw Refused("etl.executable.invalid-pipeline");
    }

    private static bool ValidIdentifier(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum &&
        char.IsAsciiLetterOrDigit(value[0]) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static EtlExecutableException Refused(string code) => new(code);
}
