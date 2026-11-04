using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

/// <summary>Bounded private connector snapshots; never used as public inspection documents.</summary>
internal static class EtlConnectorRecordCodec
{
    // Two opaque 64 KiB continuations need room for lossless UTF-16 Base64 and JSON escapes.
    // Per-run metadata quotas still bound aggregate retention independently.
    internal const int MaximumDocumentBytes = 1024 * 1024;

    internal static string Write<T>(T value)
    {
        var json = EtlRuntimeJson.Write(value);
        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
            throw new ArgumentException("The connector state exceeds its persisted size bound.");
        return json;
    }

    internal static T Read<T>(string json)
    {
        try
        {
            var value = EtlRuntimeJson.Read<T>(json, MaximumDocumentBytes);
            // We only read our own canonical documents. This also rejects duplicate/missing properties,
            // unknown members and alternate representations before they can authorize progression.
            if (!StringComparer.Ordinal.Equals(Write(value), json))
                throw new InvalidDataException("The connector state is not canonical.");
            return value;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted connector state is invalid or unsupported.", exception);
        }
    }

    internal static EtlPipelineV2 Pipeline(EtlInvocation invocation)
    {
        EtlRuntimeRecordCodec.ValidateInvocation(invocation);
        if (invocation.Artifact.FormatVersion != EtlArtifactV2Json.FormatVersion)
            throw new ArgumentException("Connector storage requires a connector-format artifact.");
        var pipeline = EtlArtifactV2Json.Read(invocation.Artifact.Pipeline);
        // A continuous shard's retained window bounds its pages instead (spec 1196), so every page-sequence check in
        // this store sees no authored limit. The pinned artifact is unchanged; this copy is never written back.
        return pipeline.Continuous is null
            ? pipeline
            : pipeline with { Http = pipeline.Http with { MaxPagesPerPartition = ContinuousPageSequenceBound } };
    }

    /// <summary>The page-sequence bound of a continuous shard: its lifetime page count, not its retained pages.</summary>
    internal const int ContinuousPageSequenceBound = int.MaxValue;

    internal static string ValidateBindings(EtlPipelineV2 pipeline, EtlConnectorRunBindings bindings, int maximumPartitions)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(bindings.HttpShardIds);
        var limits = pipeline.Limits;
        if (limits.FilePartitionCount <= 0 || limits.MaxPartitions <= 0 || limits.FilePartitionCount > limits.MaxPartitions ||
            limits.MaxPartitions > maximumPartitions || limits.MaxSnapshotBytes <= 0 || limits.MaxLookupBytes <= 0 ||
            limits.MaxLookupBytes > limits.MaxSnapshotBytes || limits.MaxBatchRecords <= 0 || limits.MaxBatchBytes <= 0 ||
            limits.MaxOutputBatchBytes <= 0 || limits.MaxRetainedOutputBytes <= 0 ||
            limits.MaxOutstandingRecords < limits.MaxBatchRecords || limits.MaxOutstandingBytes < limits.MaxBatchBytes ||
            pipeline.Http.MaxRetainedInputBytes <= 0 || pipeline.Http.MaxRetainedMetadataBytes <= 0 ||
            pipeline.Http.MaxPagesPerPartition <= 0 || pipeline.Http.MaxPageBytes <= 0 || pipeline.Http.MaxPageRecords <= 0 ||
            pipeline.Http.MaxContinuationBytes <= 0 ||
            pipeline.Http.MaxContinuationBytes > EtlHttpPolicy.MaximumSupportedContinuationBytes ||
            pipeline.Http.MaxPageBytes > pipeline.Http.MaxRetainedInputBytes ||
            pipeline.Http.MaxContinuationBytes > pipeline.Http.MaxPageBytes || !EtlHttpRetryPolicy.IsValid(pipeline.Http))
            throw new ArgumentException("The connector storage limits are invalid.");
        if (EtlContinuousRunPolicy.FindViolation(pipeline) is not null)
            throw new ArgumentException("The continuous run policy is unsupported.");
        switch (pipeline.Source)
        {
            case EtlCsvSource csv:
                Binding(bindings.Source, EtlConnectorKind.Csv, csv.Binding, isInput: true);
                if (bindings.HttpShardIds.Count != 0) throw new ArgumentException("A CSV source cannot have HTTP shards.");
                break;
            case EtlJsonlSource jsonl:
                Binding(bindings.Source, EtlConnectorKind.Jsonl, jsonl.Binding, isInput: true);
                if (bindings.HttpShardIds.Count != 0) throw new ArgumentException("A JSON Lines source cannot have HTTP shards.");
                break;
            case EtlSqlSource sql:
                // Its content is unknown until preparation reads it, so the pin carries no digest (spec 1198).
                Binding(bindings.Source, EtlConnectorKind.Sql, sql.Binding, isInput: false);
                if (bindings.HttpShardIds.Count != 0) throw new ArgumentException("A SQL source cannot have HTTP shards.");
                break;
            case EtlRabbitMqSource queue:
                // A queue's messages are unknown until preparation receives them, so the pin carries no digest (spec 1201).
                Binding(bindings.Source, EtlConnectorKind.RabbitMq, queue.Binding, isInput: false);
                if (bindings.HttpShardIds.Count != 0) throw new ArgumentException("A RabbitMQ source cannot have HTTP shards.");
                break;
            case EtlHttpJsonSource http:
                Binding(bindings.Source, EtlConnectorKind.Http, http.Binding, isInput: false);
                if (bindings.HttpShardIds.Count == 0 || bindings.HttpShardIds.Count > limits.MaxPartitions ||
                    bindings.HttpShardIds.Distinct(StringComparer.Ordinal).Count() != bindings.HttpShardIds.Count)
                    throw new ArgumentException("HTTP shard identities must be nonempty, bounded and unique.");
                foreach (var shard in bindings.HttpShardIds) EtlRuntimeRecordCodec.ValidateIdentity(shard, "shard id");
                break;
            default: throw new ArgumentException("The connector source is unsupported.");
        }
        if (pipeline.Lookup is { } lookup) Binding(bindings.Lookup, EtlConnectorKind.Csv, lookup.Binding, isInput: true);
        else if (bindings.Lookup is not null) throw new ArgumentException("The connector has no declared lookup.");
        Destination(bindings.Main, pipeline.Routes.Main);
        Destination(bindings.Errors, pipeline.Routes.Errors);
        return Write(bindings);
    }

    internal static string ValidatePrepared(EtlPipelineV2 pipeline, EtlConnectorRunBindings bindings, EtlConnectorPreparedRun prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        long retained = 0;
        if (pipeline.Source is EtlCsvSource or EtlJsonlSource or EtlSqlSource or EtlRabbitMqSource)
        {
            var file = prepared.File ?? throw new ArgumentException("The file snapshot is missing.");
            EtlRuntimeRecordCodec.ValidateObject(file.Source, "source");
            EtlRuntimeRecordCodec.ValidateObject(file.Index, "index");
            // A CSV index entry 0 is its header; a JSON Lines entry 0 is a sentinel, so an empty file still has one entry.
            // A SQL or RabbitMQ snapshot is JSON Lines.
            var indexBoundBytes = pipeline.Source is EtlCsvSource ? file.Source.ByteLength : checked(file.Source.ByteLength + 1);
            if (file.RecordCount < 0 || file.RecordCount == long.MaxValue ||
                file.Source.ByteLength > pipeline.Limits.MaxSnapshotBytes ||
                file.Index.ByteLength != checked((file.RecordCount + 1) * 24) ||
                file.Index.ByteLength > checked(indexBoundBytes * 24) ||
                // A SQL or RabbitMQ snapshot is anchored on its own recorded hash, which the batch builder verifies.
                pipeline.Source is not (EtlSqlSource or EtlRabbitMqSource) && file.Source.ContentHash != bindings.Source.Revision.ExpectedSha256)
                throw new ArgumentException("The file preparation does not match its pinned input and limits.");
            retained = checked(file.Source.ByteLength + file.Index.ByteLength);
        }
        else if (prepared.File is not null) throw new ArgumentException("An HTTP source cannot have a file snapshot.");
        if (pipeline.Lookup is not null)
        {
            var lookup = prepared.Lookup ?? throw new ArgumentException("The lookup snapshot is missing.");
            EtlRuntimeRecordCodec.ValidateObject(lookup, "lookup");
            if (lookup.ByteLength > pipeline.Limits.MaxLookupBytes || lookup.ByteLength > pipeline.Limits.MaxSnapshotBytes ||
                lookup.ContentHash != bindings.Lookup!.Revision.ExpectedSha256)
                throw new ArgumentException("The lookup preparation does not match its pinned input and limits.");
            retained = checked(retained + lookup.ByteLength);
        }
        else if (prepared.Lookup is not null) throw new ArgumentException("The connector has no declared lookup.");
        if (retained > pipeline.Http.MaxRetainedInputBytes)
            throw new ArgumentException("The prepared input exceeds the retained-input limit.");
        return Write(prepared);
    }

    internal static long RetainedBytes(EtlConnectorPreparedRun prepared) =>
        checked((prepared.File?.Source.ByteLength ?? 0) + (prepared.File?.Index.ByteLength ?? 0) + (prepared.Lookup?.ByteLength ?? 0));

    internal static bool Complete(EtlConnectorPosition position)
    {
        try
        {
            return position switch
            {
                EtlConnectorFilePosition file when file.StartOrdinal >= 1 && file.EndOrdinalExclusive >= file.StartOrdinal &&
                    file.NextOrdinal >= file.StartOrdinal && file.NextOrdinal <= file.EndOrdinalExclusive => file.NextOrdinal == file.EndOrdinalExclusive,
                EtlConnectorHttpPosition http when http.PageSequence >= 0 && http.NextItem >= 0 && http.Request is not null => HttpComplete(http),
                _ => throw new InvalidDataException("The connector position is invalid or unsupported.")
            };
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The connector position contains invalid references or identities.", exception);
        }
    }

    private static bool HttpComplete(EtlConnectorHttpPosition position)
    {
        EtlRuntimeRecordCodec.ValidateIdentity(position.Request.ShardId, "shard id");
        if (position.Page is null && position.NextItem != 0 || position.Exhausted && position.Page is not null)
            throw new InvalidDataException("The HTTP position has inconsistent page state.");
        if (position.Page is { } page)
        {
            EtlRuntimeRecordCodec.ValidateObject(page.Body, "page");
            if (page.PageSequence != position.PageSequence || page.RecordCount < 0 || position.NextItem > page.RecordCount ||
                !EtlRuntimeRecordCodec.IsSha256(page.RequestHash))
                throw new InvalidDataException("The HTTP page does not match its position.");
        }
        return position.Exhausted;
    }

    internal static void ValidateHttpPosition(EtlConnectorHttpPosition position, EtlHttpPolicy policy)
    {
        if (position.PageSequence >= policy.MaxPagesPerPartition ||
            position.Request.Continuation is { } continuation && Encoding.UTF8.GetByteCount(continuation) > policy.MaxContinuationBytes)
            throw new InvalidDataException("The HTTP position exceeds its finite limits.");
        if (position.Page is { } page && (page.Body.ByteLength > policy.MaxPageBytes || page.RecordCount > policy.MaxPageRecords ||
            page.NextContinuation is { } next && Encoding.UTF8.GetByteCount(next) > policy.MaxContinuationBytes ||
            page.RequestHash != EtlRuntimeIdentity.HttpRequestHash(position.Request)))
            throw new InvalidDataException("The HTTP page does not match its request or finite limits.");
    }

    private static void Destination(EtlConnectorBindingRevision binding, EtlDestination destination)
    {
        switch (destination)
        {
            case EtlJsonlDestination file: Binding(binding, EtlConnectorKind.Jsonl, file.Binding, isInput: false); break;
            case EtlHttpDestination http: Binding(binding, EtlConnectorKind.Http, http.Binding, isInput: false); break;
            case EtlCsvDestination csv: Binding(binding, EtlConnectorKind.Csv, csv.Binding, isInput: false); break;
            case EtlSqlDestination sql: Binding(binding, EtlConnectorKind.Sql, sql.Binding, isInput: false); break;
            case EtlRabbitMqDestination rabbitMq: Binding(binding, EtlConnectorKind.RabbitMq, rabbitMq.Binding, isInput: false); break;
            default: throw new ArgumentException("The connector destination is unsupported.");
        }
    }

    /// <remarks>File inputs are digest-pinned whatever their format; outputs, HTTP and SQL sources carry no digest.</remarks>
    private static void Binding(EtlConnectorBindingRevision? binding, EtlConnectorKind kind, string alias, bool isInput)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(binding.Revision);
        var revision = binding.Revision;
        EtlRuntimeRecordCodec.ValidateIdentity(revision.Revision, "binding revision");
        EtlRuntimeRecordCodec.ValidateIdentity(revision.Handle, "binding handle");
        if (binding.Kind != kind || revision.Alias != alias ||
            (isInput && kind != EtlConnectorKind.Http ? !EtlRuntimeRecordCodec.IsSha256(revision.ExpectedSha256) : revision.ExpectedSha256 is not null))
            throw new ArgumentException("The connector binding does not match the published role and alias.");
    }
}
