using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Etl.Core.Runtime;

/// <summary>Version-one mechanical identities shared by ETL producers and persistence providers.</summary>
/// <remarks>Values are length-framed, lossless little-endian UTF-16 code units with a distinct null marker. No worker, attempt, lease or fence is included.</remarks>
public static class EtlRuntimeIdentity
{
    public static string ScopeKey(EtlScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Hash("etl-scope-v1", scope.TenantId, scope.PersistencePartition);
    }

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
