using System.Text.Json.Serialization;

namespace Elsa.Etl.Runtime.Api.Models;

public sealed record EtlRunPageRequest(string? Cursor = null, int Limit = 50);
public sealed record EtlRunPartitionsRequest([property: JsonIgnore] string Id, string? Cursor = null, int Limit = 50);
public sealed record EtlRunRequest([property: JsonIgnore] string Id);
public sealed record EtlRunPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record EtlRunSummaryView(
    string RunId,
    string Selection,
    string Status,
    long? SourceRecordCount,
    long OutstandingRecords,
    long OutstandingBytes,
    long AcceptedRecords,
    long RejectedRecords,
    long FilteredRecords,
    EtlRunDiagnosticView? Diagnostic);

public sealed record EtlRunDetailView(
    string RunId,
    string Selection,
    string Status,
    long? SourceRecordCount,
    long OutstandingRecords,
    long OutstandingBytes,
    long AcceptedRecords,
    long RejectedRecords,
    long FilteredRecords,
    EtlRunBindingAliasesView? BindingAliases,
    EtlRunDiagnosticView? Diagnostic,
    EtlRunTerminalResultView? TerminalResult)
{
    public EtlRunInputIdentityView? SourceInput { get; init; }
    public EtlRunInputIdentityView? LookupInput { get; init; }
}

/// <summary>Retained input identity. Snapshot metadata describes the last verified preparation, not a fresh integrity check.</summary>
public sealed record EtlRunInputIdentityView(
    string BindingRevision,
    string? ExpectedSha256,
    string? SnapshotSha256,
    long? SnapshotByteLength);

public sealed record EtlRunBindingAliasesView(string Source, string Lookup, string Output, string Errors);

public sealed record EtlRunTerminalResultView(
    string Outcome,
    long AcceptedRecords,
    long RejectedRecords,
    long FilteredRecords,
    EtlRunDiagnosticView? Diagnostic,
    DateTimeOffset SettledAt);

public sealed record EtlRunDiagnosticView(string Code, long? RecordOrdinal, string? Field);

public sealed record EtlRunPartitionView(
    int PartitionOrdinal,
    long StartOrdinal,
    long EndOrdinalExclusive,
    long NextOrdinal,
    bool IsClaimed,
    long? ClaimFence,
    DateTimeOffset? ClaimExpiresAt,
    EtlRunOwedBatchView? OwedBatch);

public sealed record EtlRunOwedBatchView(long StartOrdinal, long EndOrdinalExclusive, long InputBytes);

public sealed record EtlRunCancellationView(bool CancellationAccepted, EtlRunTerminalResultView TerminalResult);

/// <summary>Runtime operations over a trusted request scope. Request values carry no tenant or partition authority.</summary>
public interface IEtlRuntimeOperations
{
    Task<EtlRunPage<EtlRunSummaryView>> ListRunsAsync(EtlRunPageRequest request, CancellationToken cancellationToken);
    Task<EtlRunDetailView> GetRunAsync(EtlRunRequest request, CancellationToken cancellationToken);
    Task<EtlRunPage<EtlRunPartitionView>> ListPartitionsAsync(EtlRunPartitionsRequest request, CancellationToken cancellationToken);
    Task<EtlRunCancellationView> CancelAsync(EtlRunRequest request, CancellationToken cancellationToken);
}
