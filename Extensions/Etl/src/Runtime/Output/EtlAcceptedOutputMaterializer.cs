using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;

namespace Elsa.Etl.Runtime.Output;

/// <summary>Builds terminal JSONL routes only from the frozen accepted manifest, never physical directory contents.</summary>
internal sealed class EtlAcceptedOutputMaterializer(
    IEtlRuntimeStore store,
    HostFileBindings bindings,
    IFileChunkPublisherFactory publisherFactory)
{
    private const int PageSize = 64;
    private readonly IFileChunkPublisher _jsonlPublisher = publisherFactory.Create(EtlFileChunkFormat.Jsonl);

    public async ValueTask<EtlMaterializedRun> MaterializeAsync(EtlScope scope, string runId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var run = await store.FindRunAsync(scope, runId, cancellationToken)
            ?? throw new EtlEffectException("etl.run-not-found", "The scoped ETL run is unavailable.");
        if (run.TerminalResult is not { } terminal || run.Status is EtlRunStatus.Preparing or EtlRunStatus.Running)
            throw new EtlEffectException("etl.output-not-terminal", "ETL output materialization requires a terminal run.");

        var expectedOutcome = run.Status switch
        {
            EtlRunStatus.Succeeded => EtlTerminalOutcome.Succeeded,
            EtlRunStatus.BusinessError => EtlTerminalOutcome.BusinessError,
            EtlRunStatus.Faulted => EtlTerminalOutcome.Faulted,
            EtlRunStatus.Cancelled => EtlTerminalOutcome.Cancelled,
            _ => throw Invalid()
        };
        if (terminal.Outcome != expectedOutcome || run.AcceptedRecords != terminal.AcceptedRecords ||
            run.RejectedRecords != terminal.RejectedRecords || run.FilteredRecords != terminal.FilteredRecords ||
            run.AcceptedRecords < 0 || run.RejectedRecords < 0 || run.FilteredRecords < 0 ||
            run.Bindings is null && (run.AcceptedRecords != 0 || run.RejectedRecords != 0 || run.FilteredRecords != 0))
            throw Invalid();

        EtlObjectReference? main = null;
        EtlObjectReference? errors = null;
        if (run.Bindings is not null)
        {
            var admission = new EtlRunAdmission(run.Invocation, run.Bindings);
            main = await MaterializeRouteAsync(admission, terminal, EtlOutputRoute.Main, run.AcceptedRecords, cancellationToken);
            errors = await MaterializeRouteAsync(admission, terminal, EtlOutputRoute.Error, run.RejectedRecords, cancellationToken);
        }
        return new(runId, terminal.Outcome, terminal.Outcome is EtlTerminalOutcome.Cancelled or EtlTerminalOutcome.Faulted,
            main, errors, run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords);
    }

    private async ValueTask<EtlObjectReference?> MaterializeRouteAsync(EtlRunAdmission admission, EtlTerminalResult terminal,
        EtlOutputRoute route, long expectedRecords, CancellationToken cancellationToken)
    {
        var revision = route == EtlOutputRoute.Main ? admission.Bindings.Output : admission.Bindings.Errors;
        var role = route == EtlOutputRoute.Main ? EtlFileRole.MainOutput : EtlFileRole.ErrorOutput;
        var outputRoot = bindings.GetOutputRoot(EtlRuntimeMetadata.PinnedBinding(admission.Invocation.Scope, revision, role));
        long bytes = 0;
        long records = 0;
        try
        {
            await foreach (var entry in EntriesAsync(admission, route, cancellationToken))
            {
                bytes = checked(bytes + entry.Effect.Chunk.ByteLength);
                records = checked(records + entry.Effect.RecordCount);
            }
        }
        catch (OverflowException exception)
        {
            throw Invalid(exception);
        }
        if (records != expectedRecords || (records == 0) != (bytes == 0))
            throw Invalid();
        if (records == 0)
            return null;

        // Batch IDs are fixed hashes; this versioned namespace cannot collide with a batch effect.
        // Reuse the lossless, length-framed identity routine rather than serializing opaque identifiers again.
        var identity = EtlRuntimeIdentity.OutputIdentity(admission, $"etl-materialized-route-v1:{terminal.ResultId}", route);
        var chunk = await _jsonlPublisher.PublishAsync(outputRoot, identity, bytes, async (output, token) =>
        {
            var buffer = new byte[4096];
            long copied = 0;
            await foreach (var entry in EntriesAsync(admission, route, token))
            {
                await CopyVerifiedAsync(Path.Combine(outputRoot, entry.Effect.Chunk.Key), entry.Effect.Chunk, output, buffer, token);
                copied = checked(copied + entry.Effect.Chunk.ByteLength);
            }
            if (copied != bytes)
                throw Invalid();
        }, cancellationToken);
        return EtlRuntimeMetadata.ToReference(chunk);
    }

    private async IAsyncEnumerable<EtlManifestEntry> EntriesAsync(EtlRunAdmission admission, EtlOutputRoute route,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EtlManifestPosition? after = null;
        var pageSize = PageSize;
        while (true)
        {
            IReadOnlyList<EtlManifestEntry> page;
            try
            {
                page = await store.ListManifestAsync(admission.Invocation.Scope, admission.Invocation.RunId, after, pageSize, cancellationToken);
            }
            catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "take" && pageSize > 1)
            {
                // A host may cap inspection below our preferred bound. Adapt without an EF-specific dependency.
                pageSize /= 2;
                continue;
            }
            if (page.Count == 0)
                yield break;
            foreach (var entry in page)
            {
                var position = entry.Position;
                var effect = entry.Effect;
                if (position.PartitionOrdinal < 0 || position.BatchStartOrdinal < 0 || !Enum.IsDefined(position.Route) ||
                    position.Route != effect.Route || after is not null && Compare(position, after) <= 0 ||
                    effect.RecordCount <= 0 || effect.Chunk.ByteLength <= 0 ||
                    effect.Chunk.ContentHash is not { Length: 64 } hash || !hash.All(char.IsAsciiHexDigit) ||
                    effect.Chunk.Key != EtlRuntimeIdentity.OutputKey(admission, entry.BatchId, effect.Route))
                    throw Invalid();
                after = position;
                if (effect.Route == route)
                    yield return entry;
            }
        }
    }

    private static int Compare(EtlManifestPosition left, EtlManifestPosition right)
    {
        var partition = left.PartitionOrdinal.CompareTo(right.PartitionOrdinal);
        if (partition != 0) return partition;
        var batch = left.BatchStartOrdinal.CompareTo(right.BatchStartOrdinal);
        return batch != 0 ? batch : left.Route.CompareTo(right.Route);
    }

    private static async ValueTask CopyVerifiedAsync(string path, EtlObjectReference expected, Stream output, byte[] buffer, CancellationToken cancellationToken)
    {
        await using var input = OpenAcceptedChunk(path, buffer.Length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long copied = 0;
        while (true)
        {
            int count;
            try { count = await input.ReadAsync(buffer, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { throw Invalid(exception); }
            if (count == 0) break;
            if (count > expected.ByteLength - copied)
                throw Invalid();
            hash.AppendData(buffer.AsSpan(0, count));
            // Destination errors belong to the publisher's output-write diagnostic, not source-integrity failure.
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            copied += count;
        }
        if (copied != expected.ByteLength ||
            !StringComparer.OrdinalIgnoreCase.Equals(expected.ContentHash, Convert.ToHexString(hash.GetHashAndReset())))
            throw Invalid();
    }

    private static FileStream OpenAcceptedChunk(string path, int bufferSize)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { throw Invalid(exception); }
    }

    private static EtlEffectException Invalid(Exception? inner = null) =>
        new("etl.accepted-output-invalid", "An accepted ETL output is unavailable or inconsistent.", inner);
}

internal sealed record EtlMaterializedRun(string RunId, EtlTerminalOutcome Outcome, bool IsPartial,
    EtlObjectReference? Main, EtlObjectReference? Errors, long AcceptedRecords, long RejectedRecords, long FilteredRecords);
