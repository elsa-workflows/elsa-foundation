# Execution lifecycle compatibility

**Status**: In progress

Delivery: [#2960](https://github.com/elsa-workflows/elsa-foundation/issues/2960), parent [#2905](https://github.com/elsa-workflows/elsa-foundation/issues/2905), under [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Purpose

Prove the existing durable background-work path with a second composition consumer before extracting a neutral execution API. [Spec 241](../241-native-durable-offload/spec.md) remains the completed native proof; this unit does not expand its acceptance retroactively.

## This change

Run a published BPMN process with two background children inside both Sequence and Flowchart parents. Sequence has a final native successor. The outer Flowchart has an independent sibling branch and a join before its final step, so the test exercises two composition layers rather than a linear wrapper. Use the existing EF checkpoint/work store and worker. BPMN keeps its gateway and process-completion semantics; each parent keeps its own ordering or join rules. No new persistence authority, public runtime abstraction, package upgrade or interpreter replacement is introduced.

Acceptance:
- Both BPMN child completion orders wait for both children before joining and advance the parent exactly once.
- Sequence advances its successor once. The outer Flowchart waits for both BPMN and its independent sibling, whether BPMN or that sibling completes first.
- Replayed accepted results do not produce another process or parent completion.
- Cancellation before claims and between results makes pending work unclaimable or rejects late receipts; the final step does not start.
- A typed fault receipt produces the blocking incident and terminal workflow/BPMN outcome, retires remaining work, and never advances the final successor. Existing parent fault-routing differences remain explicit.
- Durable state is reloaded through a fresh host where the fixture supports it, preserving identities and outcome. In-process host reload is not a process-kill or multi-host proof.
- Existing native tests retain their assertions and pass alongside the new BPMN cases.

## Lifecycle contract for subsequent integration

These obligations guide the next production host. This test slice does not claim that a direct BPMN host or a neutral lifecycle API exists.

| Boundary | Required meaning |
|---|---|
| Request acceptance | Authorization and full execution scope checked before replay lookup or mutation; a receipt proves acceptance, not physical completion. |
| Work admission | Immutable artifact/handler/schema pins, invocation identity, inputs and capability requirements; no dispatch before durable admission. |
| Attempt ownership | Worker claim fence is distinct from coordinator ownership. Expired or replaced attempts cannot commit new results. |
| Result acceptance | Exact invocation/attempt correlation, duplicate equivalence and conflict rejection; logical acceptance and semantic consumption remain separate. |
| Model transition | State, owed commands and receipt identity commit atomically under current ownership/revision. Model owns completion, fault and cancellation. |
| Composition | A BPMN child must complete through the common parent-child protocol in any conforming composite. No parent-type whitelist or Sequence/Flowchart-specific BPMN adapter. Parent-owned Flowchart joins, Sequence ordering and future StateMachine transitions remain separate from BPMN token semantics. An empty queue is not completion. |
| Child completion | Stable child outcome and parent-consumption identities allow redelivery without repeated semantic advancement. Cross-execution delivery may span transactions. |
| Cancellation | A durable cancellation request, a work tombstone, a worker-returned Cancel and a model outcome are distinct. No claim that a cancelled/expired worker physically stopped. |
| Compatibility | Existing definitions, state schemas and wire identifiers remain readable. Additive versioned adoption; no implicit conversion of active executions. |
| Retention/rollback | Preserve replay identities and capable readers/workers for accepted executions. No cleanup or rollback that resurrects or strands work. |

### Cancellation race dispositions

| Durable order | Required disposition |
|---|---|
| Cancellation before work claim | Pending work becomes unclaimable; model records its cancellation outcome. |
| Result stored, not consumed, then cancellation | Model cancellation policy determines consumption/discard; receipt alone cannot force parent advancement. |
| First result consumed, then cancellation | Already consumed progress is not undone; remaining work is cancelled according to model semantics. |
| Child completed, parent still active, then cancellation | Parent decides its remaining work; child outcome consumption remains idempotent. |
| Execution terminal, then cancellation/result replay | Preserve terminal outcome; return deterministic replay/rejection disposition. |
| Lease expires while external effect continues | Reject stale result writes, but retain at-least-once external-effect accounting. |

These are required semantic boundaries, not a new universal DTO hierarchy. BPMN business errors and incidents must not collapse into a success Boolean. Signals emitted with a fault must follow the released interpreter's obligations; the current parent-notification host restriction is a separate #2735 adoption dependency.

## Evidence and limits

Record commands and results in [plan.md](plan.md). Existing native acceptance lives in [spec 241 quickstart](../241-native-durable-offload/quickstart.md). New BPMN cases exercise the current embedded interpreter; they do not prove released `Bpmn.Semantics` parity, direct hosting, simultaneous cluster ownership, Orleans or the full connector/agent program.

The current child-fault protocol is opt-in: Flowchart handles child faults and becomes Faulted, while Sequence halts with a blocking incident and retains its Running activity status even when the workflow is Faulted. The compatibility tests preserve this existing distinction; they do not prescribe it for a future direct host or normalize all composite statuses.

## Composite conformance

Sequence and Flowchart are the concrete parent witnesses in this unit, not an exhaustive claim about every possible composite. Additional built-in or third-party composites that support structural children must satisfy the same outcome identity, idempotent consumption, cancellation scope and fault-routing contract while applying their own scheduling rules. A repeatable child invocation must distinguish each invocation; the same node ID is not an invocation identity.

No production StateMachine implementation was found in the current source tree; [Elsa 3 parity](../../docs/program-goals/elsa3-parity.md) explicitly defers it. Its future acceptance must include BPMN as a state child, repeated state entry with distinct invocation identities, stale completion after a transition, and cancellation/fault handling. StateMachine coverage is pending, not passed, and adding the activity itself is outside this test slice.
