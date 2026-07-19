# Contract: DispatchWorkflow Fault and Cancellation

## Stable activity surface

Inputs and defaults remain:

- `WorkflowDefinitionId`
- `Inputs`
- `WaitForCompletion=false`
- `CancelChildOnParentCancellation=true`
- `CorrelationId`

Outputs remain `ChildWorkflowExecutionId` and `Result`. Outcomes remain `Dispatched`, `Completed`, `Faulted`, `Cancelled`, and `DispatchFailed`.

## Terminal child contract

The child terminal checkpoint records the matching dispatch projection and deterministic parent-resume intent atomically for Completed, Faulted, and Cancelled wait-mode children. Completed retains #679 output behavior. Faulted and Cancelled carry no outputs.

Fault metadata has one canonical shape: `code=child-workflow-faulted`, `category=execution`, `summary=The child workflow faulted.`, invariant `incidentCount`, invariant `incidentIdsTruncated`, and at most 32 ordinal `incidentId.000`–`incidentId.031` keys after dedupe/sort/truncate. Cancellation metadata is exactly `code=child-workflow-cancelled`, `category=execution`, and `summary=The child workflow was cancelled.`. No exception-derived or free-form child data is included.

The existing parent bookmark stimulus and resume-target identifiers remain unchanged. The activity validates exact deterministic linkage, sets both outputs, emits the matching outcome, and completes normally. No connected edge is required and no implicit fault escalation occurs.

## Parent cancellation checkpoint contract

Only a workflow transition to Cancelled triggers propagation. For every nonterminal wait-mode dispatch whose effective policy is true, the same checkpoint contains:

1. one canonical `WorkflowDispatchCancellationRequest`; and
2. one deterministic child-cancel post-commit intent.

The provider resolves the request within the checkpoint transaction. Pending becomes Cancelled-before-admission; Started records cancellation requested; terminal state is preserved. The provider never derives the request identity from a mutable status read.

## Admission contract

Child-start delivery must atomically admit the dispatch before invoking the external start dispatcher. A cancelled-before-admission result acknowledges without invoking start. An admitted/already-admitted result uses the same deterministic child and start identities. Terminal results acknowledge safely.

Providers that advertise DispatchWorkflow runtime support must supply the additive admission and cancellation capabilities. `WorkflowDispatchReadinessInitializer` validates both and fails initialization when either is absent rather than falling back to a racy check.

## Child Cancel delivery contract

Stable intent kind: `Elsa.Activities.DispatchWorkflow.CancelChild`.

The handler validates deterministic payload and dispatch identity, loads the durable dispatch for `Partition` and `Authority.SystemIdentity`, and queries `IWorkflowExecutionStateStore` for authoritative child state, then:

- acknowledges cancelled-before-admission, DispatchFailed, or an already terminal child;
- retries while an admitted child is not visible;
- otherwise enqueues a deterministic at-least-once `WorkflowExecutionCommandKind.Cancel` through the configured actor provider, using the child partition and stored authority identity;
- retries Rejected and Deferred delivery; after Accepted, AcceptedButFaulted, or Duplicate, rechecks state and acknowledges only a terminal child.

The contribution uses positive-backoff `RetryUntilAcknowledged`. It introduces no finite exhaustion, dead-letter, or redrive behavior.

## Opt-out and detached contract

`CancelChildOnParentCancellation=false` disables directives and child-cancel intents for waited dispatches. Fire-and-forget always behaves as disabled regardless of authored input. Ordinary parent completion never propagates cancellation.

## Provider contract

Built-in in-memory and Groundwork providers must prove:

- one durable winner for concurrent admission and parent cancellation;
- atomic parent cancellation state + directive resolution + cancel outbox;
- stale claims or transaction versions cannot reverse the winner;
- restart before/after admission, directive commit, child visibility, command enqueue, terminal notification, and acknowledgement converges;
- terminal child state always outranks a late Cancel command.

## Compatibility contract

- Base `IWorkflowDispatchStore` and public record constructors remain unchanged.
- Admission and cancellation are additive provider capabilities and are catalogued extension points.
- Existing Completed and fire-and-forget behavior remain unchanged.
- Existing stable bookmark/wire identifiers remain unchanged despite their historical “Completed” wording.
- #681 dead-letter/redrive, #682 TestRun, and #683 distributed execution remain out of scope.
