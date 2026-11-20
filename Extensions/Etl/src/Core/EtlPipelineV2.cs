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
/// <param name="Limits">The finite record, partition, batch, and input/output retention limits.</param>
/// <param name="Http">The finite HTTP page, request, and retry limits.</param>
public sealed record EtlPipelineV2(
    [property: JsonRequired] EtlSource Source,
    EtlCsvLookup? Lookup,
    [property: JsonRequired] EtlRoutes Routes,
    [property: JsonRequired] IReadOnlyList<EtlOutputField> Mapping,
    [property: JsonRequired] IReadOnlyList<EtlFilter> Filters,
    [property: JsonRequired] EtlConnectorLimits Limits,
    [property: JsonRequired] EtlHttpPolicy Http)
{
    /// <summary>The optional continuous run policy; null runs the pipeline finitely.</summary>
    /// <remarks>A null policy is omitted from the v2 JSON representation to preserve existing artifact bytes and hashes.</remarks>
    public EtlContinuousRunPolicy? Continuous { get; init; }
}

/// <summary>Runs a v2 HTTP pipeline continuously: caught-up shards poll again until completion or cancellation.</summary>
/// <param name="PollIntervalSeconds">The wait before a caught-up shard polls its cursor again, from 1 to 86,400 seconds.</param>
public sealed record EtlContinuousRunPolicy([property: JsonRequired] int PollIntervalSeconds)
{
    /// <summary>The shortest supported poll interval in seconds.</summary>
    public const int MinimumPollIntervalSeconds = 1;

    /// <summary>The longest supported poll interval in seconds.</summary>
    public const int MaximumPollIntervalSeconds = 86_400;

    /// <summary>Returns the refusal code for an unsupported continuous pipeline, or null when the pipeline is finite or supported.</summary>
    /// <remarks>Only HTTP sources with a resumable cursor (token or next-link pagination) can run continuously.</remarks>
    public static string? FindViolation(EtlPipelineV2 pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline.Continuous is not { } policy)
            return null;
        if (pipeline.Source is not EtlHttpJsonSource source)
            return "etl.continuous-source-unsupported";
        if (source.Pagination is not (EtlTokenPagination or EtlNextLinkPagination))
            return "etl.continuous-pagination-unsupported";
        return policy.PollIntervalSeconds is < MinimumPollIntervalSeconds or > MaximumPollIntervalSeconds
            ? "etl.continuous-poll-interval-invalid"
            : null;
    }
}

/// <summary>A closed set of supported ETL input connector declarations.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(EtlCsvSource), "csv")]
[JsonDerivedType(typeof(EtlHttpJsonSource), "httpJson")]
[JsonDerivedType(typeof(EtlJsonlSource), "jsonl")]
[JsonDerivedType(typeof(EtlSqlSource), "sql")]
[JsonDerivedType(typeof(EtlRabbitMqSource), "rabbitmq")]
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

/// <summary>A host-bound JSON Lines input file holding one JSON object per line, with declared fields.</summary>
/// <remarks>The binding is a host-approved file alias; paths are never authored here. Blank lines are not records.</remarks>
/// <param name="Binding">The host-approved source alias.</param>
/// <param name="Fields">The declared fields extracted from each line's object.</param>
public sealed record EtlJsonlSource(
    [property: JsonRequired] string Binding,
    [property: JsonRequired] IReadOnlyList<EtlJsonField> Fields) : EtlSource;

/// <summary>A host-approved SQL table read as of one point in time (spec 1198).</summary>
/// <remarks>
/// Preparation reads the declared columns in one snapshot transaction, ordered by the key, and retains them as a JSON Lines
/// input snapshot; the run then processes that snapshot. The connection string is never authored here.
/// </remarks>
/// <param name="Binding">The host-approved SQL source alias.</param>
/// <param name="Table">The table, as <c>name</c> or <c>schema.name</c>; the binding must allow it.</param>
/// <param name="Columns">The ordered columns read and their scalar types.</param>
/// <param name="Key">One to eight required columns ordering the snapshot.</param>
public sealed record EtlSqlSource(
    [property: JsonRequired] string Binding,
    [property: JsonRequired] string Table,
    [property: JsonRequired] IReadOnlyList<EtlColumn> Columns,
    [property: JsonRequired] IReadOnlyList<string> Key) : EtlSource
{
    /// <summary>
    /// The optional required <see cref="EtlColumnType.Int64"/> column whose high watermark a completed run records, so the
    /// next run reads only rows above it, and rows whose watermark is NULL, which it rejects as record errors (spec 1199); null
    /// reads every row.
    /// </summary>
    /// <remarks>A null watermark is omitted from the v2 JSON representation to preserve existing artifact bytes and hashes.</remarks>
    public string? Watermark { get; init; }
}

/// <summary>A host-approved RabbitMQ queue drained up to a bound when a run prepares (spec 1201).</summary>
/// <remarks>
/// Preparation receives up to <see cref="MaxMessages"/> messages without acknowledging them, retains their JSON bodies as a
/// JSON Lines input snapshot, and acknowledges them only once that snapshot is recorded as the run's prepared input; the run
/// then processes the snapshot as a JSON Lines source with <see cref="Fields"/>. Ingestion is at least once. The AMQP URI is
/// never authored here.
/// </remarks>
/// <param name="Binding">The host-approved RabbitMQ source alias.</param>
/// <param name="Queue">The queue; the binding must allow it.</param>
/// <param name="Fields">The JSON fields read from each message body.</param>
/// <param name="MaxMessages">The most messages one run takes, 1 to 100,000; the rest stay queued.</param>
public sealed record EtlRabbitMqSource(
    [property: JsonRequired] string Binding,
    [property: JsonRequired] string Queue,
    [property: JsonRequired] IReadOnlyList<EtlJsonField> Fields,
    [property: JsonRequired] int MaxMessages) : EtlSource
{
    /// <summary>The largest <see cref="MaxMessages"/> a source may declare.</summary>
    public const int MaxMessagesLimit = 100_000;
}

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
[JsonDerivedType(typeof(EtlCsvDestination), "csv")]
[JsonDerivedType(typeof(EtlSqlDestination), "sql")]
[JsonDerivedType(typeof(EtlRabbitMqDestination), "rabbitmq")]
public abstract record EtlDestination;

/// <summary>A host-bound JSON Lines file destination.</summary>
/// <param name="Binding">The host-approved destination alias.</param>
public sealed record EtlJsonlDestination([property: JsonRequired] string Binding) : EtlDestination;

/// <summary>A host-bound CSV file destination: one RFC 4180 file with a header row per published batch.</summary>
/// <param name="Binding">The host-approved destination alias.</param>
public sealed record EtlCsvDestination([property: JsonRequired] string Binding) : EtlDestination;

/// <summary>A host-bound HTTP JSON destination.</summary>
/// <remarks>The binding is a host-approved alias; the destination URL and credentials are never authored here.</remarks>
/// <param name="Binding">The host-approved destination alias.</param>
public sealed record EtlHttpDestination([property: JsonRequired] string Binding) : EtlDestination;

/// <summary>
/// A host-bound PostgreSQL or SQL Server table that each batch upserts by key in one transaction, together with a ledger
/// row that makes every batch's effect apply exactly once (spec 1197). Main route only.
/// </summary>
/// <remarks>The binding is a host-approved alias; the connection string is never authored here.</remarks>
/// <param name="Binding">The host-approved destination alias.</param>
/// <param name="Table">The target table, <c>name</c> or <c>schema.name</c>; its columns are the output field names.</param>
/// <param name="Key">One to eight output fields, mapped from required source fields, that identify a row.</param>
public sealed record EtlSqlDestination(
    [property: JsonRequired] string Binding,
    [property: JsonRequired] string Table,
    [property: JsonRequired] IReadOnlyList<string> Key) : EtlDestination
{
    /// <inheritdoc />
    public bool Equals(EtlSqlDestination? other) => other is not null && Binding == other.Binding && Table == other.Table &&
        (ReferenceEquals(Key, other.Key) || Key is not null && other.Key is not null && Key.SequenceEqual(other.Key));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Binding);
        hash.Add(Table);
        foreach (var field in Key ?? []) hash.Add(field);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A host-bound RabbitMQ exchange to which each record of a batch is published as one persistent JSON message, recorded only
/// once the broker has confirmed every message of the batch (spec 1200). Delivery is at least once: a replayed batch is
/// published again with the same message ids. Main route only.
/// </summary>
/// <remarks>The binding is a host-approved alias; the AMQP URI and credentials are never authored here.</remarks>
/// <param name="Binding">The host-approved destination alias.</param>
/// <param name="Exchange">The exchange, or empty for the default exchange.</param>
/// <param name="RoutingKey">The routing key of every message.</param>
public sealed record EtlRabbitMqDestination(
    [property: JsonRequired] string Binding,
    [property: JsonRequired] string Exchange,
    [property: JsonRequired] string RoutingKey) : EtlDestination;

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

/// <summary>Finite payload, partition, batching, and retained-input and output limits for a v2 pipeline.</summary>
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

    internal const long DefaultMaxRetainedOutputBytes = 1024L * 1024 * 1024;

    /// <summary>The maximum route-body bytes retained across all output plans for one run.</summary>
    /// <remarks>Counts the logical bytes of retained main and error route bodies. Output-plan and batch-descriptor JSON is charged separately to <see cref="EtlHttpPolicy.MaxRetainedMetadataBytes"/>. Database and index overhead is excluded.</remarks>
    public long MaxRetainedOutputBytes { get; init; } = DefaultMaxRetainedOutputBytes;

    /// <summary>The maximum bytes in a file-backed source or lookup snapshot.</summary>
    public long MaxSnapshotBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>The maximum serialized runnable artifact size.</summary>
    public int MaxArtifactBytes { get; init; } = EtlArtifact.MaximumByteLength;
}

/// <summary>Finite HTTP page, cursor, retention, retry, and request-time bounds for a v2 pipeline.</summary>
public sealed record EtlHttpPolicy
{
    /// <summary>The largest continuation supported by the persisted HTTP page and cursor metadata.</summary>
    /// <remarks>Runtime metadata uses a lossless UTF-16 representation and is bounded to 1 MiB including JSON escaping; this ceiling leaves room for both request and response continuations and their surrounding identities.</remarks>
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

    /// <summary>The maximum logical UTF-8 bytes per run across retained positions, request and page history, batch descriptors, output plans, and HTTP delivery journals.</summary>
    /// <remarks>The budget counts canonical <c>PositionJson</c>, <c>RequestJson</c>, <c>PageJson</c>, batch-descriptor, output-plan, and HTTP delivery journal JSON bytes, plus bounded transition headroom for nonterminal HTTP deliveries. It excludes admission, prepared, and native-terminal envelopes and database or index overhead; it does not promise an aggregate physical database size. Retained route-body bytes are charged separately to <see cref="EtlConnectorLimits.MaxRetainedOutputBytes"/>. The default value is omitted from the v2 JSON representation to preserve existing artifact bytes and hashes.</remarks>
    public long MaxRetainedMetadataBytes { get; init; } = DefaultMaxRetainedMetadataBytes;

    /// <summary>The maximum durably allocated attempts for one HTTP route delivery.</summary>
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
