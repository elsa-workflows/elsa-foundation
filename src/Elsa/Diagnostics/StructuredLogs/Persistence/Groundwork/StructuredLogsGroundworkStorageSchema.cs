using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Groundwork.DiagnosticRecords;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;

/// <summary>Groundwork stream, projected-index, and durable ledger requirements for Structured Logs.</summary>
public static class StructuredLogsGroundworkStorageSchema
{
    public static IReadOnlySet<string> RequiredIndexFields { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "sequence",
            "level",
            "categoryKey",
            "sourceKey",
            "replayToken"
        };

    public static IReadOnlySet<string> RequiredOperationLedgers { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "diagnostic-append-idempotency",
            "diagnostic-trim-idempotency"
        };

    public static DiagnosticRecordStreamDefinition CreateStream(StructuredLogStoreBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.ScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.StreamId);
        return GroundworkStructuredLogStore.CreateStreamDefinition(binding.StreamId);
    }
}
