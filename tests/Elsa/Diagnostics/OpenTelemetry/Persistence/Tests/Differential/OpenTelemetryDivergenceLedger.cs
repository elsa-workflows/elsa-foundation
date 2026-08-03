namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests.Differential;

/// <summary>
/// The recorded surface and dispositions for the #646 EF-vs-Groundwork differential on
/// <c>IOpenTelemetryStore</c>. Executable half of
/// <c>specs/094-harden-groundwork-stores/divergence-ledger.md</c>.
/// </summary>
internal static class OpenTelemetryDivergenceLedger
{
    /// <summary>Every fact the differential compares, per dimension.</summary>
    private static readonly IReadOnlyDictionary<OpenTelemetryDimension, string[]> Compared =
        new Dictionary<OpenTelemetryDimension, string[]>
        {
            [OpenTelemetryDimension.ConcurrencyConflictShape] =
            [
                "catalog-rows-for-contested-id", "first-write", "second-write", "stale-write",
                "surviving-last-seen", "surviving-service-name"
            ],
            [OpenTelemetryDimension.ProducerOrdering] =
                ["distinct", "dropped-count", "loss", "ordered-newest-first", "readable", "written"],
            [OpenTelemetryDimension.NullAndDefaultMaterialization] =
            [
                "log-attributes", "log-null-span-id", "log-severity-number", "resource-attributes",
                "resource-status", "rich-readable", "sdk-language", "service-instance-id",
                "sparse-empty-attributes", "sparse-null-instance-id", "sparse-null-sdk-language", "sparse-readable"
            ],
            [OpenTelemetryDimension.RollbackVisibility] =
                ["flushed-write-durable", "partial-batch-visible"],
            [OpenTelemetryDimension.RestartObservation] =
            [
                "logs-preserved", "metric-points-preserved", "readable-traces-after-restart",
                "resources-preserved", "trace-detail-after-restart", "traces-preserved"
            ],
            [OpenTelemetryDimension.IdempotentReplay] =
            [
                "rows-for-replayed-trace-id", "summary-name", "summary-span-count", "summary-status",
                "summary-workflow-ids"
            ]
        };

    /// <summary>
    /// Facts whose EF and Groundwork observations legitimately differ. Each entry must have a prose row
    /// with a disposition in the markdown ledger before it is added here.
    /// </summary>
    private static readonly IReadOnlyDictionary<OpenTelemetryDimension, string[]> Divergences =
        new Dictionary<OpenTelemetryDimension, string[]>
        {
            [OpenTelemetryDimension.ConcurrencyConflictShape] = [],
            [OpenTelemetryDimension.ProducerOrdering] = [],
            [OpenTelemetryDimension.NullAndDefaultMaterialization] = [],
            // Previously recorded a divergence on readable-trace-count. Withdrawn: it was an artifact of
            // the harness disposing EF's host on abandon while Groundwork's drain kept running, and once
            // the two abandons were made symmetric the same probe returned different answers run to run.
            // The fact is no longer observed at all — see RollbackVisibilityAsync's remarks.
            [OpenTelemetryDimension.RollbackVisibility] = [],
            [OpenTelemetryDimension.RestartObservation] = [],
            [OpenTelemetryDimension.IdempotentReplay] = []
        };

    /// <summary>
    /// SHA-256 over the compared-fact and recorded-divergence names. Never covers observed values:
    /// those are the finding, and binding them would turn every real behaviour change into a digest
    /// mismatch instead of a divergence.
    /// </summary>
    public const string SurfaceDigest = "b2310b9850d716dc844e1f8c7c04fb24650aaf35d28a700eda473a18c1e0dfb2";

    public static IReadOnlyCollection<string> ComparedFactsFor(OpenTelemetryDimension dimension) =>
        Compared.TryGetValue(dimension, out var facts) ? facts : [];

    public static IReadOnlyCollection<string> RecordedDivergencesFor(OpenTelemetryDimension dimension) =>
        Divergences.TryGetValue(dimension, out var facts) ? facts : [];

    public static IEnumerable<OpenTelemetryDimension> Dimensions =>
        Enum.GetValues<OpenTelemetryDimension>().OrderBy(d => d.ToString(), StringComparer.Ordinal);
}
