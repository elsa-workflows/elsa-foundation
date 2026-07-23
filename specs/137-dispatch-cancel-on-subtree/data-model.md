# Data Model: Cancel Waited Dispatches on Subtree Teardown

No new persisted entity or schema is introduced.

## Local cancellation selection

The checkpoint already carries activity-execution state changes. The enricher derives an in-memory ordinal set containing the `ActivityExecutionId` of each change that:

- is an upsert; and
- has resulting status `Cancelled`.

Deletion and non-cancelled upserts do not grant child-cancellation authority.

## Dispatch ownership join

Each `WorkflowDispatchRecord` already carries:

- `ParentWorkflowExecutionId`;
- `ParentActivityExecutionId`;
- `ChildWorkflowExecutionId`;
- mode, effective propagation policy, lifecycle status, and cancellation markers.

A local cancellation selects a record only when its `ParentActivityExecutionId` exactly matches a member of the cancelled activity set. Whole-parent cancellation continues to select all records under the parent before existing eligibility filters apply.

## Existing cancellation responsibility

Selected, eligible records reuse the shipped:

- `WorkflowDispatchCancellationRequest`;
- deterministic child-cancel post-commit intent;
- provider-atomic cancellation directive resolution;
- child Cancel command delivery and terminal acknowledgement.

The checkpoint occurrence time remains the request timestamp. Request, intent, command, envelope, and idempotency identities remain unchanged.

## State-transition matrix

| Parent workflow | Owning activity | Dispatch eligibility | Result |
|---|---|---|---|
| Running | Cancelled | waited + propagation enabled + active/marked | existing cancellation responsibility |
| Running | Cancelled | detached, opted out, or terminal without committed work | unchanged |
| Running | Not cancelled | any | unchanged |
| Cancelled | any | waited + propagation enabled + active/marked | existing whole-parent responsibility |
| Cancelled | Cancelled | waited + propagation enabled + active/marked | one deduplicated responsibility |

Provider lifecycle transitions and late terminal races remain exactly as specified by work unit 100.
