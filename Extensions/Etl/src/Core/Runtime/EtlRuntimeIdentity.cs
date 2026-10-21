using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Etl.Core.Runtime;

/// <summary>Versioned mechanical identities shared by ETL producers and persistence providers.</summary>
/// <remarks>Values are length-framed, lossless little-endian UTF-16 code units with a distinct null marker. No worker, attempt, lease or fence is included.</remarks>
public static class EtlRuntimeIdentity
{
    /// <summary>Stable digest of a request within its pinned shard, independent of worker or attempt.</summary>
    public static string HttpRequestHash(EtlHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ShardId);
        return Hash("etl-http-request-v2", request.ShardId, request.Continuation);
    }

    public static string ScopeKey(EtlScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Hash("etl-scope-v1", scope.TenantId, scope.PersistencePartition);
    }

    /// <summary>Stable connector input identity bound to all admission pins and prepared inputs.</summary>
    public static string ConnectorBatchId(EtlConnectorRunAdmission admission, EtlConnectorPreparedRun prepared,
        int partitionOrdinal, EtlConnectorBatchRange range, long inputBytes, string inputHash)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(admission.Bindings);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(range);
        var bindings = admission.Bindings;
        var input = prepared.File is { } file
            ? Hash("etl-connector-file-v2", ObjectReference(file.Source), ObjectReference(file.Index), Number(file.RecordCount))
            : null;
        var coordinates = range switch
        {
            EtlConnectorFileBatchRange fileRange => Hash("etl-file-range-v2", Number(fileRange.StartOrdinal), Number(fileRange.EndOrdinalExclusive)),
            EtlConnectorHttpBatchRange http => Hash("etl-http-range-v2", Number(http.PageSequence), http.RequestHash, http.PageContentHash,
                Number(http.StartItem), Number(http.EndItemExclusive)),
            _ => throw new ArgumentException("The connector batch range is unsupported.", nameof(range))
        };
        // Explicit versioned fields keep mechanical identity independent of future snapshot DTO additions.
        return Hash("etl-connector-batch-v2", InvocationFingerprint(admission.Invocation),
            ConnectorBinding(bindings.Source), ConnectorBinding(bindings.Lookup), ConnectorBinding(bindings.Main), ConnectorBinding(bindings.Errors),
            Hash(["etl-http-shards-v2", .. bindings.HttpShardIds]), input, ObjectReference(prepared.Lookup), Number(partitionOrdinal),
            coordinates, Number(inputBytes), inputHash);
    }

    private static string? ConnectorBinding(EtlConnectorBindingRevision? binding) => binding is null
        ? null : Hash("etl-connector-binding-v2", Number((int)binding.Kind), Binding(binding.Revision));

    /// <summary>Stable route effect bound to its input batch, pinned destination and exact body content.</summary>
    /// <remarks>Independent of worker, attempt and staging location; pass unchanged as the delivery identity.</remarks>
    public static string ConnectorEffectId(EtlConnectorRunAdmission admission, string batchId, EtlOutputRoute route,
        string contentHash, long byteLength, long recordCount)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(admission.Bindings);
        if (!Enum.IsDefined(route)) throw new ArgumentOutOfRangeException(nameof(route));
        var destination = route == EtlOutputRoute.Main ? admission.Bindings.Main : admission.Bindings.Errors;
        return Hash("etl-connector-effect-v2", InvocationFingerprint(admission.Invocation), batchId, Number((int)route),
            ConnectorBinding(destination), contentHash, Number(byteLength), Number(recordCount));
    }

    private static string? ObjectReference(EtlObjectReference? reference) => reference is null
        ? null : Hash("etl-object-v2", reference.Key, reference.ContentHash, Number(reference.ByteLength));

    public static string InvocationFingerprint(EtlInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(invocation.Scope);
        ArgumentNullException.ThrowIfNull(invocation.Artifact);
        ArgumentNullException.ThrowIfNull(invocation.ResultAddress);
        var artifact = invocation.Artifact;
        if (JsonMarshal.GetRawUtf8Value(artifact.Pipeline).Length > EtlArtifact.MaximumByteLength)
            throw new ArgumentException("The ETL invocation artifact exceeds the supported byte limit.", nameof(invocation));
        var address = invocation.ResultAddress;
        return Hash("etl-invocation-v1", invocation.Scope.TenantId, invocation.Scope.PersistencePartition,
            invocation.RunId, Number(artifact.FormatVersion), artifact.ArtifactId, artifact.ContentHash,
            artifact.DefinitionId, Number(artifact.DefinitionVersion), artifact.Pipeline.GetRawText(),
            Number(address.FormatVersion), address.WorkflowExecutionId, address.ActivityExecutionId,
            address.ExecutableId, address.ExecutableHash, address.NodeId, address.BookmarkId,
            address.RegistrationId, address.ResumeTargetId, address.StimulusType, address.StimulusHash,
            address.PayloadTypeAlias, Number(address.PayloadSchemaVersion), address.DeduplicationPolicy, address.ProviderId);
    }

    public static string BatchId(EtlRunAdmission admission, EtlPreparedRun prepared, int partitionOrdinal,
        long startOrdinal, long endOrdinalExclusive, long inputBytes, string inputHash)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(admission.Bindings);
        return Hash("etl-batch-v1", InvocationFingerprint(admission.Invocation),
            Number(prepared.FormatVersion), prepared.Source.Key, prepared.Source.ContentHash,
            Number(prepared.Source.ByteLength), prepared.Lookup.Key, prepared.Lookup.ContentHash,
            Number(prepared.Lookup.ByteLength), prepared.SourceIndex.Key, prepared.SourceIndex.ContentHash,
            Number(prepared.SourceIndex.ByteLength), Number(prepared.SourceRecordCount),
            Binding(admission.Bindings.Source), Binding(admission.Bindings.Lookup),
            Binding(admission.Bindings.Output), Binding(admission.Bindings.Errors),
            Number(partitionOrdinal), Number(startOrdinal), Number(endOrdinalExclusive), Number(inputBytes), inputHash);
    }

    /// <summary>Pass this identity unchanged to the file publisher; its physical key is <see cref="OutputKey"/>.</summary>
    public static string OutputIdentity(EtlRunAdmission admission, string batchId, EtlOutputRoute route)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(admission.Bindings);
        if (!Enum.IsDefined(route))
            throw new ArgumentOutOfRangeException(nameof(route));
        var binding = route == EtlOutputRoute.Main ? admission.Bindings.Output : admission.Bindings.Errors;
        return Hash("etl-output-v1", InvocationFingerprint(admission.Invocation), batchId, Number((int)route), Binding(binding));
    }

    public static string OutputKey(EtlRunAdmission admission, string batchId, EtlOutputRoute route) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OutputIdentity(admission, batchId, route)))).ToLowerInvariant() + ".jsonl";

    private static string Binding(EtlBindingRevision binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Hash("etl-binding-v1", binding.Alias, binding.Revision, binding.Handle, binding.ExpectedSha256);
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Hash(params string?[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> size = stackalloc byte[sizeof(int)];
        foreach (var value in values)
        {
            // Preserve every opaque .NET identity, including distinct unpaired surrogate code units.
            if (value is { Length: > EtlArtifact.MaximumByteLength })
                throw new ArgumentException("An ETL identity component exceeds the supported limit.", nameof(values));
            byte[]? bytes = null;
            if (value is not null)
            {
                bytes = new byte[checked(value.Length * sizeof(char))];
                for (var index = 0; index < value.Length; index++)
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
            }
            BinaryPrimitives.WriteInt32LittleEndian(size, bytes?.Length ?? -1);
            hash.AppendData(size);
            if (bytes is not null)
                hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
