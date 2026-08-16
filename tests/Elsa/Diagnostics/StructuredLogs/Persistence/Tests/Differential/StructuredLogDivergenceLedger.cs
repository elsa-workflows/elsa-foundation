namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests.Differential;

/// <summary>
/// The recorded surface and dispositions for the #646 EF-vs-Groundwork differential on
/// <c>IStructuredLogStore</c>. This is the executable half of
/// <c>specs/094-harden-groundwork-stores/divergence-ledger.md</c>; the two must agree, and the markdown
/// carries the prose rationale and the ratifying authority.
/// </summary>
/// <remarks>
/// A divergence row is a decision, not an excuse. Adding one asserts that a human compared the two
/// behaviours and concluded which is the contract. Spec 144's T011 consumes these dispositions, and an
/// <c>Undecided</c> row is a deliberate blocker: EF Core is not deletable while one stands.
/// </remarks>
internal static class StructuredLogDivergenceLedger
{
    /// <summary>
    /// Every fact the differential compares, per dimension. Pinning the full key set — not only the
    /// divergent subset — is what stops a probe edit from silently narrowing the comparison while the
    /// suite stays green.
    /// </summary>
    private static readonly IReadOnlyDictionary<StructuredLogDimension, string[]> Compared =
        new Dictionary<StructuredLogDimension, string[]>
        {
            [StructuredLogDimension.ConcurrencyConflictShape] =
                ["default-cursor", "foreign-cursor", "revision-token", "trimmed-cursor"],
            [StructuredLogDimension.ProducerOrdering] =
                ["appended", "distinct", "high-water-vs-max-logical-sequence", "loss", "per-producer-order", "readable"],
            [StructuredLogDimension.NullAndDefaultMaterialization] =
            [
                "event-id", "event-name", "exception-stack", "exception-type", "level", "message-template",
                "properties", "rich-readable", "scopes", "sparse-empty-message", "sparse-empty-properties",
                "sparse-null-event-id", "sparse-readable"
            ],
            [StructuredLogDimension.RollbackVisibility] =
                ["acknowledged", "acknowledged-writes-durable", "durable-after-abandon", "high-water-preserved", "torn-batch"],
            [StructuredLogDimension.RestartObservation] =
            [
                "high-water-preserved", "high-water-rewound", "pre-restart-cursor", "readable-after-restart",
                "tail-cursor-available", "written-before-restart"
            ],
            [StructuredLogDimension.IdempotentReplay] =
            [
                "committed-cursor-still-resolves", "records-for-resubmitted-instance", "repeated-read-is-stable",
                "resubmitted-instance-deduplicated"
            ]
        };

    /// <summary>
    /// Facts whose EF and Groundwork observations legitimately differ. A fact listed here is exempt from
    /// the equality assertion; every other divergence fails the differential.
    /// </summary>
    /// <remarks>
    /// The provider-generated sequence is the v2 Structured Logs identity and lifetime high-water, so
    /// it intentionally diverges from EF's caller-assigned display sequence in the producer probe.
    /// </remarks>
    private static readonly IReadOnlyDictionary<StructuredLogDimension, string[]> Divergences =
        new Dictionary<StructuredLogDimension, string[]>
        {
            [StructuredLogDimension.ConcurrencyConflictShape] = [],
            [StructuredLogDimension.ProducerOrdering] = ["high-water-vs-max-logical-sequence"],
            [StructuredLogDimension.NullAndDefaultMaterialization] = [],
            [StructuredLogDimension.RollbackVisibility] = [],
            [StructuredLogDimension.RestartObservation] = [],
            [StructuredLogDimension.IdempotentReplay] = []
        };

    /// <summary>
    /// SHA-256 over the compared-fact and recorded-divergence names, so a probe or ledger edit cannot
    /// silently restate what the differential claims to have compared. Never covers observed values:
    /// those are the finding, and binding them would turn every real behaviour change into a digest
    /// mismatch instead of a divergence.
    /// </summary>
    public const string SurfaceDigest = "059d367a79cd2381abd484b665f4afa8eb8968350c1270d82cb426dfb4bf876d";

    public static IReadOnlyCollection<string> ComparedFactsFor(StructuredLogDimension dimension) =>
        Compared.TryGetValue(dimension, out var facts) ? facts : [];

    public static IReadOnlyCollection<string> RecordedDivergencesFor(StructuredLogDimension dimension) =>
        Divergences.TryGetValue(dimension, out var facts) ? facts : [];

    public static IEnumerable<StructuredLogDimension> Dimensions =>
        Enum.GetValues<StructuredLogDimension>().OrderBy(d => d.ToString(), StringComparer.Ordinal);
}
