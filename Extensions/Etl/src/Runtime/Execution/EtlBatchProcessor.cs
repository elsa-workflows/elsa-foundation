using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Execution;

internal sealed record EtlBatchProcessResult(EtlBatchAcceptance? Acceptance, EtlPreparationDiagnostic? Diagnostic)
{
    public bool Succeeded => Acceptance is not null && Diagnostic is null;
}

/// <summary>Transforms one already-reserved input batch and publishes its complete output routes.</summary>
/// <remarks>
/// This component does not reserve or accept work. Its caller owns the durable cursor transition and must pass the
/// same deterministic batch descriptor to reservation and acceptance. Physical chunks remain unaccepted until then.
/// </remarks>
internal static class EtlBatchProcessor
{
    private static readonly FileChunkStore ChunkStore = new();

    public static async ValueTask<EtlBatchProcessResult> ProcessAsync(
        EtlRunAdmission admission,
        EtlPipeline pipeline,
        EtlPreparedDescriptor prepared,
        EtlInputBatch inputBatch,
        HostFileBindings bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(inputBatch);
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        var invalid = Validate(admission, pipeline, prepared, inputBatch);
        if (invalid is not null)
            return Failed(invalid);

        string mainRoot;
        string errorRoot;
        try
        {
            mainRoot = bindings.GetOutputRoot(EtlRuntimeMetadata.PinnedBinding(
                admission.Invocation.Scope, admission.Bindings.Output, EtlFileRole.MainOutput));
            errorRoot = bindings.GetOutputRoot(EtlRuntimeMetadata.PinnedBinding(
                admission.Invocation.Scope, admission.Bindings.Errors, EtlFileRole.ErrorOutput));
        }
        catch (EtlBindingException exception)
        {
            return Failed(new EtlPreparationDiagnostic(exception.Code));
        }

        EtlRowTransformer transformer;
        try
        {
            transformer = new EtlRowTransformer(pipeline, prepared.SourceHeader, prepared.LookupRows);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Failed(new EtlPreparationDiagnostic("etl.pipeline-invalid"));
        }

        using var mainBytes = new MemoryStream();
        using var errorBytes = new MemoryStream();
        var accepted = 0L;
        var rejected = 0L;
        var filtered = 0L;
        var totalOutputBytes = 0L;

        foreach (var record in inputBatch.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = pipeline.Limits.MaxOutputBatchBytes - totalOutputBytes;
            var transformed = transformer.Transform(record, remaining);
            switch (transformed.Disposition)
            {
                case EtlRowDisposition.Accepted:
                    AppendBounded(mainBytes, transformed.JsonlBytes, remaining);
                    totalOutputBytes += transformed.JsonlBytes.Length;
                    accepted++;
                    break;
                case EtlRowDisposition.Filtered:
                    filtered++;
                    break;
                case EtlRowDisposition.Rejected:
                    try
                    {
                        var before = errorBytes.Length;
                        WriteError(errorBytes, transformed.Error!, remaining, cancellationToken);
                        var appended = errorBytes.Length - before;
                        totalOutputBytes += appended;
                        rejected++;
                    }
                    catch (EtlEffectException exception) when (exception.Code == "etl.output-too-large")
                    {
                        return Failed(new EtlPreparationDiagnostic("etl.error-output-too-large", transformed.RecordOrdinal,
                            transformed.Error?.Field));
                    }
                    break;
                default:
                    return Failed(new EtlPreparationDiagnostic("etl.row-disposition-invalid", transformed.RecordOrdinal));
            }
        }

        if (totalOutputBytes > pipeline.Limits.MaxOutputBatchBytes)
            return Failed(new EtlPreparationDiagnostic("etl.output-batch-too-large"));

        var descriptor = EtlRuntimeMetadata.Batch(admission, prepared, inputBatch);
        var effects = new List<EtlOutputEffect>(2);
        try
        {
            if (mainBytes.Length > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = await PublishRouteAsync(mainRoot, admission, descriptor.BatchId, EtlOutputRoute.Main,
                    mainBytes, pipeline.Limits.MaxOutputBatchBytes, cancellationToken);
                effects.Add(new EtlOutputEffect(EtlOutputRoute.Main, EtlRuntimeMetadata.ToReference(chunk), accepted));
            }

            if (errorBytes.Length > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = await PublishRouteAsync(errorRoot, admission, descriptor.BatchId, EtlOutputRoute.Error,
                    errorBytes, pipeline.Limits.MaxOutputBatchBytes, cancellationToken);
                effects.Add(new EtlOutputEffect(EtlOutputRoute.Error, EtlRuntimeMetadata.ToReference(chunk), rejected));
            }
        }
        catch (EtlEffectException exception)
        {
            return Failed(EtlPreparationDiagnostic.Fault(exception.Code, exception));
        }
        catch (IOException exception)
        {
            return Failed(EtlPreparationDiagnostic.Fault("etl.output-write-failed", exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failed(EtlPreparationDiagnostic.Fault("etl.output-write-failed", exception));
        }

        return new EtlBatchProcessResult(
            new EtlBatchAcceptance(descriptor, accepted, rejected, filtered, effects.AsReadOnly()), null);
    }

    private static EtlPreparationDiagnostic? Validate(
        EtlRunAdmission admission,
        EtlPipeline pipeline,
        EtlPreparedDescriptor prepared,
        EtlInputBatch batch)
    {
        try
        {
            var invocation = admission.Invocation;
            if (invocation is null)
                return new EtlPreparationDiagnostic("etl.batch-descriptor-invalid");
            var scope = invocation.Scope;
            if (scope is null || invocation.Artifact is null || invocation.ResultAddress is null ||
                admission.Bindings is null || admission.Bindings.Source is null || admission.Bindings.Lookup is null ||
                admission.Bindings.Output is null || admission.Bindings.Errors is null ||
                string.IsNullOrWhiteSpace(invocation.RunId))
                return new EtlPreparationDiagnostic("etl.batch-descriptor-invalid");

            if (prepared.Inputs is null || prepared.Partitions is null || pipeline.Limits is null ||
                prepared.Inputs.SourcePin is null || prepared.Inputs.LookupPin is null ||
                prepared.SourceHeader is null || prepared.LookupHeader is null)
                return new EtlPreparationDiagnostic("etl.prepared-run-mismatch");

            var artifactPipeline = EtlArtifactJson.Read(invocation.Artifact.Pipeline);
            var artifactDocument = EtlArtifactJson.Write(artifactPipeline);
            var suppliedDocument = EtlArtifactJson.Write(pipeline);
            var artifactHash = EtlArtifactJson.Hash(artifactDocument);
            var suppliedHash = EtlArtifactJson.Hash(suppliedDocument);
            if (invocation.Artifact.FormatVersion != EtlArtifact.CurrentFormatVersion ||
                artifactHash != invocation.Artifact.ContentHash || artifactHash != suppliedHash ||
                prepared.PipelineHash != artifactHash ||
                suppliedDocument.GetRawText() != artifactDocument.GetRawText())
                return new EtlPreparationDiagnostic("etl.pipeline-artifact-mismatch");

            if (prepared.Inputs.Scope != scope || prepared.Inputs.RunId != invocation.RunId ||
                prepared.SourceRecordCount != prepared.Partitions.SourceRecordCount ||
                !prepared.SourceHeader.MatchesSchema(pipeline.SourceSchema) ||
                !prepared.LookupHeader.MatchesSchema(pipeline.LookupSchema) ||
                !EtlRuntimeMetadata.Matches(prepared.Inputs.SourcePin, admission.Bindings.Source, scope, EtlFileRole.Source) ||
                !EtlRuntimeMetadata.Matches(prepared.Inputs.LookupPin, admission.Bindings.Lookup, scope, EtlFileRole.Lookup) ||
                !BindingMatchesPipeline(pipeline, admission.Bindings))
                return new EtlPreparationDiagnostic("etl.prepared-run-mismatch");

            if (!IsValidBatch(pipeline, prepared, batch))
                return new EtlPreparationDiagnostic("etl.batch-identity-invalid");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or OverflowException)
        {
            return new EtlPreparationDiagnostic("etl.batch-descriptor-invalid");
        }

        return null;
    }

    private static bool BindingMatchesPipeline(EtlPipeline pipeline, EtlRunBindings bindings) =>
        pipeline.Bindings is not null &&
        bindings.Source.Alias == pipeline.Bindings.Source &&
        bindings.Lookup.Alias == pipeline.Bindings.Lookup &&
        bindings.Output.Alias == pipeline.Bindings.Output &&
        bindings.Errors.Alias == pipeline.Bindings.Errors &&
        bindings.Output.ExpectedSha256 is null && bindings.Errors.ExpectedSha256 is null;

    private static bool IsValidBatch(EtlPipeline pipeline, EtlPreparedDescriptor prepared, EtlInputBatch batch)
    {
        if (batch.PartitionIndex < 0 || batch.PartitionIndex >= prepared.Partitions.Count ||
            batch.StartRecordOrdinal < 1 || batch.EndRecordOrdinalExclusive <= batch.StartRecordOrdinal ||
            batch.EndRecordOrdinalExclusive - batch.StartRecordOrdinal != batch.RecordCount ||
            batch.RecordCount <= 0 || batch.RecordCount > pipeline.Limits.MaxBatchRecords ||
            batch.CanonicalInputBytes <= 0 || batch.CanonicalInputBytes > pipeline.Limits.MaxBatchBytes ||
            batch.Records is null || batch.Records.Count != batch.RecordCount || !IsHash(batch.ContentHash))
            return false;

        var range = prepared.Partitions[batch.PartitionIndex];
        if (batch.StartRecordOrdinal < range.StartRecordOrdinal || batch.EndRecordOrdinalExclusive > range.EndRecordOrdinalExclusive)
            return false;

        for (var index = 0; index < batch.Records.Count; index++)
        {
            var record = batch.Records[index];
            if (record is null || record.Fields is null || record.Fields.Any(field => field is null) ||
                record.RecordOrdinal != batch.StartRecordOrdinal + index)
                return false;
        }

        return true;
    }

    private static async ValueTask<EtlChunk> PublishRouteAsync(
        string outputRoot,
        EtlRunAdmission admission,
        string batchId,
        EtlOutputRoute route,
        MemoryStream buffer,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var identity = EtlRuntimeIdentity.OutputIdentity(admission, batchId, route);
        var chunk = await ChunkStore.PublishAsync(outputRoot, identity, maximumBytes,
            async (destination, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (!buffer.TryGetBuffer(out var bytes))
                    throw new IOException("The bounded ETL route buffer is unavailable.");
                await destination.WriteAsync(bytes.AsMemory(0, checked((int)buffer.Length)), ct);
            }, cancellationToken);

        if (!string.Equals(chunk.Key, EtlRuntimeIdentity.OutputKey(admission, batchId, route), StringComparison.Ordinal))
            throw new EtlEffectException("etl.output-identity-invalid", "The ETL output key does not match its effect identity.");
        return chunk;
    }

    private static void AppendBounded(MemoryStream destination, ReadOnlyMemory<byte> bytes, long remaining)
    {
        if (bytes.Length > remaining)
            throw new InvalidDataException("The ETL transformer exceeded the remaining route budget.");
        destination.Write(bytes.Span);
    }

    private static void WriteError(MemoryStream destination, EtlRowError error, long remaining, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bounded = new BoundedWriteStream(destination, remaining);
        using var writer = new Utf8JsonWriter(bounded, new JsonWriterOptions { Indented = false });
        writer.WriteStartObject();
        writer.WriteNumber("recordOrdinal", error.RecordOrdinal);
        writer.WriteString("code", error.Code);
        if (error.Field is not null)
            writer.WriteString("field", error.Field);
        writer.WriteEndObject();
        writer.Flush();
        bounded.WriteByte((byte)'\n');
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    private static EtlBatchProcessResult Failed(EtlPreparationDiagnostic diagnostic) => new(null, diagnostic);
}
