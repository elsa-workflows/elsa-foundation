namespace Elsa.Workflows.Runtime.Distributed;

/// <summary>
/// Wire-safe persisted-kind identifiers owned by the distributed runtime leaf. These string values are the frozen v1
/// document-kind discriminators for the cross-node command transport (and future placement) documents. A durable
/// (Groundwork) implementation of <see cref="Contracts.IExecutionCommandTransport"/> is a named follow-up; when it
/// lands it MUST reuse this discriminator and the committed <c>Fixtures/v1</c> golden fixture wire shape unchanged.
/// </summary>
/// <remarks>
/// The names follow the constitution S=E6 rules: camelCase, no protected-term collisions, stable across the W14 type
/// renames. Do not change a literal value without a schema version bump and an upcaster, exactly as
/// <c>ElsaRuntimeStorageManifest</c> treats its runtime document kinds.
/// </remarks>
public static class DistributedRuntimeStorageManifest
{
    /// <summary>
    /// Durable cross-node command inbox. Each queued command for a workflow execution is one document so the inbox
    /// survives node death and a survivor can re-lease and re-drive it on failover.
    /// </summary>
    public const string ExecutionCommandTransportDocumentKind = "executionCommandTransport";
}
