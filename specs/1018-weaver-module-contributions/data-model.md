# Candidate Data Model

Semantic names below guide #3437 implementation; #3438 package consumption may refine signatures before freeze. Fields express transport/lifecycle needs, not a universal domain schema.

| Entity | Essential data | Invariants |
|---|---|---|
| Resource target | Owner, module-defined resource type/ID, module-defined revision, optional navigation label | Route text never grants identity or authority; revision meaning belongs to module. |
| Context attachment | Identity, owner, schema version, target, provenance, sensitivity, trust class, bounded JSON/content, availability | Separate trusted guidance, authoritative facts and untrusted client snapshots/resource data. Admission records exclusions/redaction. |
| Contributor outcome | Available/absent/unavailable, required/optional designation, attachments/descriptors, diagnostics | Required missing input denies dependent actions; optional failure is visible partial context. Unexpected exceptions/cancellation propagate. |
| Tool descriptor | Stable identity/owner, input/target/result schema versions, permissions, allowed modes and execution location | Trusted backend registration defines execution authority. Client metadata cannot create a tool. |
| Prepared invocation | Invocation/session/turn IDs, principal/tenant association, tool, canonical arguments, exact target/revision, context generation, risk/review data, expiry | Immutable reviewed meaning; current authority, module availability and target constraints rechecked before application. |
| Proposal | Prepared invocation identity, approval/denial/execution state and timestamps, stored outcome | Existing generic review concept; approval does not imply effect. |
| Client binding | Private current module/editor selection, generation, revision getter, direct execution callback | Not serialized executable code. Unmount/removal/selection lifetime change invalidates generation. |
| Result receipt | Invocation identity, outcome/effect location, module result type/schema version/JSON, observed target/revision and evidence | Local working state differs from durable resource effect. Text-only/no-op success does not qualify. |

Named `AgentContextCollecting`, `AgentCapabilitiesCollecting` and `AgentToolsCollecting` events expose request and initialized get-only output collections. One aggregator per event composes the existing source interfaces. The admitted per-turn snapshot feeds current registry/invoker lookup; it is not another independently populated registry.

## State transitions

```text
Requested -> Prepared -> AwaitingApproval -> Approved -> Revalidate
                                     Denied           |-> ServerExecuting -> Receipt
                                                      |-> AwaitingClient -> Dispatched -> Receipt
                                                      |                              |-> Uncertain
                                                      |-> Refused/Expired/Cancelled
```

Read-only tools may finish without mutation approval under existing policy. Candidate local mutations always require explicit review, including FullAuto. Apply returns HTTP 202 while pending. Only accepted observed-state receipt marks the proposal Executed; the resumed turn consumes the stored typed result.

- Before dispatch/application, malformed, denied, stale, expired or missing binding/executor produces explicit refusal and zero effects.
- Exact originating generation and revision are separate checks. A successful edit creates a new revision; this expected transition does not invalidate its own receipt.
- After dispatch, cancellation/timeout/context loss cannot assert rollback. Preserve an available receipt or report uncertainty; never silently replay.
- Matching duplicate deliveries coalesce in the in-memory pending record. Conflicting acknowledgements are diagnosed and cannot replace an accepted receipt or invoke again.
- An acknowledgement must match session, actor, invocation and bound envelope; it records an effect and never executes it. Late/expired/cancelled acknowledgement behavior preserves any already-observed effect without reopening execution.
- Singleton stores retain records only. Scoped services resolve current actor, policy and module tools; no HttpContext, scoped tool or callback is retained in singleton state.
- Process restart loses candidate memory. No durable recovery or duplicate protection across restart is claimed; vanished records fail explicitly and cannot authorize replay.

## Thin resource evidence

Workflow: exact definition ID, draft hash/revision, node ID and designer position; native atomic transaction; acknowledgement after editor document state reflects the expected change. Receipt identifies local dirty state. Whole-batch undo verifies the post-apply revision before restoring, refusing intervening edits. Save/publish remain explicit.

Unrelated fixture: a module-owned server resource with revision and a harmless editable value. Its executor applies through its owning service and returns actual read-back revision/value. No workflow fields or provider/host branches participate.
