namespace Elsa.Workflows.Runtime.Distributed;

/// <summary>
/// Wire-safe persisted-kind identifiers owned by the distributed runtime leaf. These string values are the frozen v1
/// document-kind discriminators for cross-node command transport and placement documents. The opt-in Groundwork
/// persistence feature consumes these identifiers and MUST preserve the committed <c>Fixtures/v1</c> golden fixture
/// wire shapes unchanged.
/// </summary>
/// <remarks>
/// The names follow the constitution S=E6 rules: camelCase, no protected-term collisions, stable across the W14 type
/// renames. Do not change a literal value without a schema version bump and an upcaster, exactly as
/// <c>ElsaRuntimeStorageManifest</c> treats its runtime document kinds.
/// </remarks>
public static class DistributedRuntimeStorageManifest
{
    /// <summary>
    /// Durable per-execution command stream coordination document. The Groundwork transport uses this head with
    /// provider-level compare-and-swap to allocate strictly increasing per-execution command sequences without
    /// scanning the command backlog.
    /// </summary>
    public const string ExecutionCommandStreamHeadDocumentKind = "executionCommandStreamHead";

    /// <summary>
    /// Durable cross-node command inbox. Each queued command for a workflow execution is one document so the inbox
    /// survives node death and a survivor can re-lease and re-drive it on failover.
    /// </summary>
    public const string ExecutionCommandTransportDocumentKind = "executionCommandTransport";

    /// <summary>
    /// Durable per-execution placement lease (W27). One document per workflow execution records which node currently
    /// hosts its workflow-execution actor, so placement routing survives node death and a survivor can take over when
    /// the lease expires. The persisted <c>ExecutionPlacementLease</c> shape is frozen by the committed
    /// <c>Fixtures/v1/executionPlacement.json</c> golden fixture (drift-test-protected), exactly as the transport kind
    /// froze its item shape.
    /// </summary>
    public const string ExecutionPlacementDocumentKind = "executionPlacement";
}
