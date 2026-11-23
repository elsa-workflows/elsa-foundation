using System.Collections;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Preparation;

/// <summary>The immutable input snapshots retained for one admitted ETL invocation.</summary>
internal sealed record EtlInputSnapshots(
    EtlScope Scope,
    string RunId,
    PinnedFileBinding SourcePin,
    PinnedFileBinding LookupPin,
    EtlChunk SourceChunk,
    EtlChunk LookupChunk);

internal sealed record EtlSnapshotResult(EtlInputSnapshots? Snapshots, EtlPreparationDiagnostic? Diagnostic)
{
    public bool Succeeded => Snapshots is not null && Diagnostic is null;
}

/// <summary>Validated input, lookup, and source-index descriptors ready for bounded execution.</summary>
internal sealed record EtlPreparedDescriptor(
    string PipelineHash,
    EtlInputSnapshots Inputs,
    EtlChunk SourceIndex,
    EtlCsvHeaderMap SourceHeader,
    EtlCsvHeaderMap LookupHeader,
    IReadOnlyDictionary<EtlTypedValue, EtlTypedRow> LookupRows,
    long SourceRecordCount,
    EtlPartitionPlan Partitions);

internal sealed record EtlPreparationResult(EtlPreparedDescriptor? Prepared, EtlPreparationDiagnostic? Diagnostic)
{
    public bool Succeeded => Prepared is not null && Diagnostic is null;
}

internal enum EtlPreparationDiagnosticKind
{
    BusinessError,
    Fault
}

/// <summary>A safe preparation diagnostic with no path or input value.</summary>
internal sealed record EtlPreparationDiagnostic(string Code, int RecordOrdinal = 0, string? Field = null)
{
    public EtlPreparationDiagnosticKind Kind { get; init; } = EtlPreparationDiagnosticKind.BusinessError;

    /// <summary>Infrastructure cause retained for internal fault logging; never part of the safe diagnostic payload.</summary>
    internal Exception? Cause { get; init; }

    public static EtlPreparationDiagnostic Fault(string code, Exception cause) =>
        new(code) { Kind = EtlPreparationDiagnosticKind.Fault, Cause = cause };

    public override string ToString() => Field is null
        ? $"{Kind}: {Code} at record {RecordOrdinal}"
        : $"{Kind}: {Code} at record {RecordOrdinal}, field {Field}";
}

internal readonly record struct EtlPartitionRange(long StartRecordOrdinal, long EndRecordOrdinalExclusive);

/// <summary>Computes balanced, exhaustive data-record ranges without allocating a range per partition.</summary>
internal sealed class EtlPartitionPlan : IReadOnlyList<EtlPartitionRange>
{
    public EtlPartitionPlan(long sourceRecordCount, int requestedPartitionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRecordCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedPartitionCount);
        SourceRecordCount = sourceRecordCount;
        Count = (int)Math.Min(sourceRecordCount, requestedPartitionCount);
    }

    public long SourceRecordCount { get; }
    public int Count { get; }

    public EtlPartitionRange this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var baseSize = SourceRecordCount / Count;
            var remainder = SourceRecordCount % Count;
            var startOffset = index * baseSize + Math.Min(index, remainder);
            var rangeSize = baseSize + (index < remainder ? 1 : 0);
            return new EtlPartitionRange(startOffset + 1, startOffset + rangeSize + 1);
        }
    }

    public IEnumerator<EtlPartitionRange> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
            yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
