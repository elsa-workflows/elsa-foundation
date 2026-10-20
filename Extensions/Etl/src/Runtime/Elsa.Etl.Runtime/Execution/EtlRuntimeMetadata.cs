using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;

namespace Elsa.Etl.Runtime.Execution;

/// <summary>Maps verified runtime-owned descriptors to the stable Core identities used by persistence.</summary>
internal static class EtlRuntimeMetadata
{
    public static EtlPreparedRun Prepared(EtlPreparedDescriptor prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return new EtlPreparedRun(
            EtlPreparedRun.CurrentFormatVersion,
            ToReference(prepared.Inputs.SourceChunk),
            ToReference(prepared.Inputs.LookupChunk),
            ToReference(prepared.SourceIndex),
            prepared.SourceRecordCount);
    }

    /// <summary>Returns the deterministic descriptor the coordinator must reserve before processing.</summary>
    public static EtlBatchDescriptor Batch(EtlRunAdmission admission, EtlPreparedDescriptor prepared, EtlInputBatch batch)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(batch);
        var preparedRun = Prepared(prepared);
        var id = EtlRuntimeIdentity.BatchId(admission, preparedRun, batch.PartitionIndex,
            batch.StartRecordOrdinal, batch.EndRecordOrdinalExclusive, batch.CanonicalInputBytes, batch.ContentHash);
        return new EtlBatchDescriptor(id, batch.StartRecordOrdinal, batch.EndRecordOrdinalExclusive,
            batch.CanonicalInputBytes, batch.ContentHash);
    }

    public static PinnedFileBinding PinnedBinding(EtlScope scope, EtlBindingRevision revision, EtlFileRole role) =>
        new(scope, revision.Alias, role, revision.Revision, revision.Handle, revision.ExpectedSha256);

    public static EtlBindingRevision Binding(PinnedFileBinding pin) =>
        new(pin.Alias, pin.Revision, pin.Handle, pin.ExpectedSha256);

    public static bool Matches(PinnedFileBinding pin, EtlBindingRevision revision, EtlScope scope, EtlFileRole role) =>
        pin.Scope == scope && pin.Role == role &&
        string.Equals(pin.Alias, revision.Alias, StringComparison.Ordinal) &&
        string.Equals(pin.Revision, revision.Revision, StringComparison.Ordinal) &&
        string.Equals(pin.Handle, revision.Handle, StringComparison.Ordinal) &&
        string.Equals(pin.ExpectedSha256, revision.ExpectedSha256, StringComparison.Ordinal);

    public static EtlObjectReference ToReference(EtlChunk chunk) =>
        new(chunk.Key, chunk.ContentHash, chunk.ByteLength);
}
