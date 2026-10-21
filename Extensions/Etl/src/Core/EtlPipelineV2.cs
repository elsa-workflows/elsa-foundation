using System.Text.Json.Serialization;

namespace Elsa.Etl.Core;

/// <summary>A connector-capable ETL pipeline document using artifact format version 2.</summary>
/// <remarks>
/// Bindings are host-configured aliases. This document contains no endpoint URL, credential, or secret value.
/// Its serializer and wire shape are independent of the retained v1 file pipeline.
/// </remarks>
/// <param name="Source">The declared source connector.</param>
/// <param name="Lookup">The optional CSV lookup source.</param>
/// <param name="Routes">The main and error destinations.</param>
/// <param name="Mapping">The typed output field mappings.</param>
/// <param name="Filters">The deterministic record filters.</param>
/// <param name="Limits">The finite record, partition, batch, and retention limits.</param>
/// <param name="Http">The finite HTTP page, request, and retry limits.</param>
public sealed record EtlPipelineV2(
    [property: JsonRequired] EtlSource Source,
    EtlCsvLookup? Lookup,
    [property: JsonRequired] EtlRoutes Routes,
    [property: JsonRequired] IReadOnlyList<EtlOutputField> Mapping,
    [property: JsonRequired] IReadOnlyList<EtlFilter> Filters,
    [property: JsonRequired] EtlConnectorLimits Limits,
    [property: JsonRequired] EtlHttpPolicy Http);

/// <summary>A closed set of supported ETL input connector declarations.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(EtlCsvSource), "csv")]
[JsonDerivedType(typeof(EtlHttpJsonSource), "httpJson")]
public abstract record EtlSource;

/// <summary>A host-bound CSV input source with a declared CSV schema.</summary>
/// <param name="Binding">The host-approved source alias.</param>
/// <param name="Schema">The ordered input columns and scalar types.</param>
public sealed record EtlCsvSource(
    [property: JsonRequired] string Binding,
    [property: JsonRequired] IReadOnlyList<EtlColumn> Schema) : EtlSource;

/// <summary>A paginated HTTP JSON input source with an array selector and declared fields.</summary>
/// <remarks>The binding is a host-approved alias; the source URL and credentials are never authored here.</remarks>
/// <param name="Binding">The host-approved source alias.</param>
/// <param name="RecordsPointer">The JSON Pointer selecting the page's record array.</param>
/// <param name="Fields">The declared fields extracted from each record.</param>
/// <param name="Pagination">The supported continuation strategy.</param>
public sealed record EtlHttpJsonSource(
    [property: JsonRequired] string Binding,
    [property: JsonRequired] string RecordsPointer,
    [property: JsonRequired] IReadOnlyList<EtlJsonField> Fields,
    [property: JsonRequired] EtlHttpPagination Pagination) : EtlSource;

/// <summary>Selects one named scalar value by JSON Pointer within a source record.</summary>
/// <param name="Name">The field name used by mappings and filters.</param>
/// <param name="Pointer">The JSON Pointer selecting the value in one record.</param>
/// <param name="Type">The scalar type required for the selected value.</param>
/// <param name="Required">Whether a missing or null selected value rejects the record.</param>
public sealed record EtlJsonField(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Pointer,
    [property: JsonRequired] EtlColumnType Type,
    [property: JsonRequired] bool Required);

/// <summary>An optional CSV lookup and the source and lookup keys used to join it.</summary>
/// <param name="Binding">The host-approved lookup alias.</param>
/// <param name="Schema">The ordered lookup columns and scalar types.</param>
/// <param name="SourceKeyField">The source field used for the join.</param>
/// <param name="LookupKeyField">The lookup field used for the join.</param>
public sealed record EtlCsvLookup(
    [property: JsonRequired] string Binding,
    [property: JsonRequired] IReadOnlyList<EtlColumn> Schema,
    [property: JsonRequired] string SourceKeyField,
    [property: JsonRequired] string LookupKeyField);

/// <summary>The independently selected main and error output routes.</summary>
/// <param name="Main">The destination for successfully mapped records.</param>
/// <param name="Errors">The destination for rejected records.</param>
public sealed record EtlRoutes(
    [property: JsonRequired] EtlDestination Main,
    [property: JsonRequired] EtlDestination Errors);

/// <summary>A closed set of supported ETL output destinations.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(EtlJsonlDestination), "jsonl")]
[JsonDerivedType(typeof(EtlHttpDestination), "http")]
public abstract record EtlDestination;

/// <summary>A host-bound JSON Lines file destination.</summary>
/// <param name="Binding">The host-approved destination alias.</param>
public sealed record EtlJsonlDestination([property: JsonRequired] string Binding) : EtlDestination;

/// <summary>A host-bound HTTP JSON destination.</summary>
/// <remarks>The binding is a host-approved alias; the destination URL and credentials are never authored here.</remarks>
/// <param name="Binding">The host-approved destination alias.</param>
public sealed record EtlHttpDestination([property: JsonRequired] string Binding) : EtlDestination;

/// <summary>A closed set of supported HTTP JSON pagination selectors.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(EtlSinglePagePagination), "singlePage")]
[JsonDerivedType(typeof(EtlTokenPagination), "token")]
[JsonDerivedType(typeof(EtlNextLinkPagination), "nextLink")]
public abstract record EtlHttpPagination;

/// <summary>Declares that an HTTP JSON source has exactly one page.</summary>
public sealed record EtlSinglePagePagination : EtlHttpPagination;

/// <summary>Reads a continuation token and supplies it as a query parameter on the next request.</summary>
/// <param name="ContinuationPointer">The JSON Pointer selecting the opaque continuation token.</param>
/// <param name="QueryParameter">The query parameter name receiving the token.</param>
public sealed record EtlTokenPagination(
    [property: JsonRequired] string ContinuationPointer,
    [property: JsonRequired] string QueryParameter) : EtlHttpPagination;

/// <summary>Reads a continuation link from the current HTTP JSON page.</summary>
/// <param name="ContinuationPointer">The JSON Pointer selecting the opaque continuation link.</param>
public sealed record EtlNextLinkPagination(
    [property: JsonRequired] string ContinuationPointer) : EtlHttpPagination;

/// <summary>Finite payload, partition, batching, and retained-input limits for a v2 pipeline.</summary>
/// <remarks>Byte totals use 64-bit values. These are logical limits, not a process-memory guarantee.</remarks>
public sealed record EtlConnectorLimits
{
    /// <summary>The requested number of file input partitions.</summary>
    public int FilePartitionCount { get; init; } = 2;

    /// <summary>The maximum partition count accepted from this pipeline definition.</summary>
    public int MaxPartitions { get; init; } = 32;

    /// <summary>The maximum UTF-8 size of one logical input record.</summary>
    public int MaxRecordBytes { get; init; } = 64 * 1024;

    /// <summary>The maximum number of declared input fields.</summary>
    public int MaxFields { get; init; } = 256;

    /// <summary>The maximum records in one admitted batch.</summary>
    public int MaxBatchRecords { get; init; } = 1_000;

    /// <summary>The maximum canonical input bytes in one admitted batch.</summary>
    public long MaxBatchBytes { get; init; } = 4L * 1024 * 1024;

    /// <summary>The maximum records admitted but not yet durably settled.</summary>
    public long MaxOutstandingRecords { get; init; } = 10_000;

    /// <summary>The maximum input bytes admitted but not yet durably settled.</summary>
    public long MaxOutstandingBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>The maximum logical records in the optional CSV lookup.</summary>
    public long MaxLookupRecords { get; init; } = 100_000;

    /// <summary>The maximum bytes in the optional CSV lookup.</summary>
    public long MaxLookupBytes { get; init; } = 16L * 1024 * 1024;

    /// <summary>The maximum serialized output bytes emitted by one batch across its routes.</summary>
    public long MaxOutputBatchBytes { get; init; } = 8L * 1024 * 1024;

    /// <summary>The maximum bytes in a file-backed source or lookup snapshot.</summary>
    public long MaxSnapshotBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>The maximum serialized runnable artifact size.</summary>
    public int MaxArtifactBytes { get; init; } = EtlArtifact.MaximumByteLength;
}

/// <summary>Finite HTTP page, cursor, retention, retry, and request-time bounds for a v2 pipeline.</summary>
public sealed record EtlHttpPolicy
{
    /// <summary>The largest continuation supported by the persisted HTTP page and cursor metadata.</summary>
    /// <remarks>Runtime metadata uses a lossless UTF-16 representation and is bounded to 512 KiB; this ceiling leaves room for both request and response continuations and their surrounding identities.</remarks>
    public const int MaximumSupportedContinuationBytes = 64 * 1024;

    internal const long DefaultMaxRetainedMetadataBytes = 64L * 1024 * 1024;

    /// <summary>The maximum raw response bytes retained for one HTTP page.</summary>
    public long MaxPageBytes { get; init; } = 8L * 1024 * 1024;

    /// <summary>The maximum records accepted from one HTTP page.</summary>
    public int MaxPageRecords { get; init; } = 10_000;

    /// <summary>The maximum JSON nesting depth accepted from an HTTP page.</summary>
    public int MaxJsonDepth { get; init; } = 32;

    /// <summary>The maximum UTF-8 bytes in one opaque continuation value, up to <see cref="MaximumSupportedContinuationBytes"/>.</summary>
    public int MaxContinuationBytes { get; init; } = 8 * 1024;

    /// <summary>The maximum pages allowed for one source partition.</summary>
    public int MaxPagesPerPartition { get; init; } = 10_000;

    /// <summary>The maximum retained source input bytes for the run.</summary>
    public long MaxRetainedInputBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>The maximum logical UTF-8 bytes per run across retained connector position, request-ledger, and page-history JSON.</summary>
    /// <remarks>The budget counts canonical <c>PositionJson</c>, <c>RequestJson</c>, and <c>PageJson</c> bytes. It excludes admission, prepared, and native-terminal envelopes and database or index overhead; it does not promise an aggregate physical database size. The default value is omitted from the v2 JSON representation to preserve existing artifact bytes and hashes.</remarks>
    public long MaxRetainedMetadataBytes { get; init; } = DefaultMaxRetainedMetadataBytes;

    /// <summary>The maximum physical attempts for one HTTP route delivery.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>The route-delivery deadline in seconds.</summary>
    public int DeadlineSeconds { get; init; } = 900;

    /// <summary>The per-request timeout in seconds, clamped by the remaining delivery deadline.</summary>
    public int RequestTimeoutSeconds { get; init; } = 30;

    /// <summary>The initial retry delay in seconds.</summary>
    public int InitialDelaySeconds { get; init; } = 1;

    /// <summary>The maximum retry delay in seconds.</summary>
    public int MaxDelaySeconds { get; init; } = 60;
}
