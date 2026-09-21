namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// Authored per-workflow checkpoint-persistence cadence (ADR 0032 R5). This is a design-time authoring selection
/// that flows design → publish → executable and is resolved by the Runtime off the pinned artifact; it is never
/// read from a Design store by the Runtime (§E2.2).
/// </summary>
/// <remarks>
/// <see cref="Mode"/> follows the same stable-alias convention as <see cref="WorkflowStrategyOptions.CommitStrategyType"/>:
/// it is a string alias (<c>"Immediate"</c> or <c>"Coalesced"</c>), never an assembly-qualified name, validated at
/// publish. A <see langword="null"/> or empty <see cref="Mode"/> means the workflow authors no cadence and the host
/// default applies. Cadence never relaxes a mandatory checkpoint boundary; it only decides whether the relaxable
/// intra-segment progress checkpoints flush immediately or coalesce.
/// </remarks>
public sealed record WorkflowCheckpointCadenceOptions
{
    /// <summary>The stable cadence alias: <c>"Immediate"</c> or <c>"Coalesced"</c>. Null/empty = host default.</summary>
    public string? Mode { get; init; }

    /// <summary>
    /// The bounded number of replayable checkpoints a coalesced segment folds before an intermediate flush. Only
    /// meaningful when <see cref="Mode"/> is <c>"Coalesced"</c>; when null the host's configured cap applies.
    /// </summary>
    public int? MaxSegmentCheckpoints { get; init; }
}
