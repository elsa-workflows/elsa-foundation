# Runtime Core material staging extension points

These opt-in Runtime-owned contracts provide no concrete service registration or feature. Each port is a Replacement: a host selects exactly one applicable registration per scope, rejects duplicates before owner I/O/effects, and preserves an explicitly selected override. There are no strategy keys or aggregating fan-in semantics. The existing
[Runtime implementation catalog](../Elsa.Workflows.Runtime/EXTENSION_POINTS.md) owns execution and persistence hooks.
The consuming [promotion contract](../../../../../specs/1225-workflow-release-promotion/contracts/promotion.md)
describes the caller's fixed intent, recovery and visibility obligations.

| Replacement port | Contract / Core layer | Signature / identity | Callers / replacement obligations |
|---|---|---|---|
| Material stage owner | `IRuntimeMaterialStagingStore` | `RegisterAsync(command, cancellationToken)`, `StageAsync(command, fence, cancellationToken)`, `FindReceiptAsync(commandId, cancellationToken)`, `AdvanceFenceAsync(command, expectedFence, cancellationToken)`, `RevokeAsync(command, expectedFence, cancellationToken)`; results are `ValueTask<RuntimeMaterialStageReceipt>` (nullable for Find) | The release adapter/coordinator consumes one selected physical owner. Every default method refuses unsupported legacy surfaces. Implementations must freeze bodies before I/O, authorize current host access, fence one immutable request, compare original incarnation expectations and commit effects/receipt/retention atomically. Replacements must preserve the complete selected store/cache path. |
| Live authorization | `IRuntimeMaterialStagingAuthorization` | `ValueTask<bool> AuthorizeAsync(RuntimeMaterialStageCommand command, CancellationToken cancellationToken = default)` and `AuthorizeReadAsync(RuntimeDeploymentScope targetScope, string commandId, CancellationToken cancellationToken = default)` | Required host policy for commands and receipt reads, with no default grant. Saved actor IDs and source provenance grant no access. |
| Trusted target scope | `IRuntimeDeploymentScopeAccessor` | `RuntimeDeploymentScope RequireScope()` | Required independently supplied host address; no captured-payload or display-label fallback. |
| Material retention | `IRuntimeMaterialStageRetentionReader` | `ValueTask<IReadOnlyList<RuntimeMaterialStageRetentionRoot>> ReadStageRetentionRootsAsync(int maximumRoots, CancellationToken cancellationToken = default)` | The actual material owner supplies inert retention roots to collection; the default refuses unknown evidence. Missing/failed evidence must retain conservatively. |

The command bounds original payloads and copies member lists and raw bytes. Typed legacy graphs stay read-only
caller inputs until the selected owner freezes them synchronously before I/O. Expected null incarnation means
Missing. Ready/Committed/Revoked receipts and owner fences grant no publication or fresh-start authority.
No named event, background task, material write or activation is introduced by these contracts.
