using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Groundwork.Kernel;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;

/// <summary>Groundwork stream, projected-index, and durable ledger requirements for Structured Logs.</summary>
public static class StructuredLogsGroundworkStorageSchema
{
    public const string UnitId = "elsa-structured-logs";
    public const string UnitName = "elsa_structured_logs";
    public const string SequenceField = "sequence";
    public const string LevelField = "level";
    public const string CategoryKeyField = "categoryKey";
    public const string SourceKeyField = "sourceKey";
    public const string ReplayTokenField = "replayToken";
    public const string PayloadField = "payload";
    public const string TimestampField = "timestamp";

    internal const string ReplayIndex = "elsa_structured_logs_replay";
    internal const string SequenceOrderIndex = "elsa_structured_logs_sequence_order";


    /// <summary>
    /// The v2 public storage declaration. The ProviderSequence is the sole row identity and is
    /// intentionally also the lifetime logical high-water inspected by the adapter. A zero-valued
    /// retention policy is part of the declaration so TrimAsync(0) removes retained rows without
    /// changing that high-water.
    /// </summary>
    public static StorageUnit CreateUnit(int keepNewest = 0) =>
        CreateUnitBuilder(keepNewest).Build();

    private static StorageDeclarationBuilder CreateUnitBuilder(int keepNewest) =>
        keepNewest < 0
            ? throw new ArgumentOutOfRangeException(nameof(keepNewest))
            :
        StorageUnit.Declare(UnitId, UnitName)
            .Int64(SequenceField, column => column.Required().ProviderSequence())
            .Timestamp(TimestampField, column => column.Required())
            .Int64(LevelField, column => column.Required())
            .String(CategoryKeyField, 128, column => column.Required())
            .String(SourceKeyField, 128, column => column.Required())
            .String(ReplayTokenField, 64, column => column.Required())
            .Json(PayloadField, column => column.Required())
            .Key(SequenceField)
            .Index("elsa_structured_logs_level", LevelField)
            .Index("elsa_structured_logs_category", CategoryKeyField)
            .Index("elsa_structured_logs_source", SourceKeyField)
            .Index(ReplayIndex, ReplayTokenField)
            // A single-column B-tree can be scanned in either direction by all supported
            // providers, so this descending declaration serves both recent and replay routes.
            .Index(SequenceOrderIndex, index => index.Descending(SequenceField))
            .Scoped()
            .AppendIdempotency(TimeSpan.FromHours(1), "elsa_structured_logs_append")
            .Retention(keepNewest, SequenceField)
            .RetentionIdempotency(TimeSpan.FromHours(1), "elsa_structured_logs_retention");

    public static StorageScope ScopeFor(StructuredLogStoreBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new StorageScope(CanonicalBinding(binding));
    }

    internal static string CanonicalBinding(StructuredLogStoreBinding binding) =>
        $"{binding.TenantId.Length}:{binding.TenantId}{binding.ScopeId.Length}:{binding.ScopeId}{binding.StreamId.Length}:{binding.StreamId}";
}
