# Data Model: Preserve Dispatch Test-Run Scope

## Workflow test scope

- `ScopeId`: nonblank root test-run identity.
- `ExpiresAt`: finite timestamp; equality is expired.
- `TenantId`: nullable only for untenantized hosts; immutable.
- `Partition`: immutable execution partition.
- `State`: `Open`, `Closing`, or `Closed`.
- `CreatedAt`, `ClosingAt`, `ClosedAt`: monotonic lifecycle times.
- `CloseReason`: `Expired` or `ExplicitTeardown`, recorded once.

Open may transition only to Closing; Closing may transition only to Closed. Equivalent create/close replays are idempotent; conflicting identity/context fails closed.

## Scoped execution snapshot

An optional immutable snapshot on execution start/checkpoint/state:

- scope ID;
- expiry;
- tenant and partition binding.

New `TestRun` roots and newly committed DispatchWorkflow descendants require it. Production/background roots reject it. Legacy `TestRun` state may have null and is never inferred into a scope; retained replay of an older unscoped descendant remains readable for compatibility.

## Scoped dispatch

`WorkflowDispatchRecord` and child-start payload retain the same optional scope snapshot as the parent. It is part of immutable context and is compared during admission, replay, child visibility repair, persistence, and inspection projection.

## Scope cleanup request/result

Request:

- scope ID;
- observed/requested time;
- close reason selected internally;
- active access context, not caller-provided tenant/partition.

Safe result dispositions:

- `Accepted` — Open moved to Closing;
- `AlreadyClosing` — equivalent cleanup continues;
- `AlreadyClosed` — no work remains;
- `NotFound` — no visible scope in the active context.

Result counts expose only bounded progress: inspected, cancelled-before-admission, cancellation-queued, terminal-unchanged, and remaining-live.

## Dispatch cleanup transition

```text
Open scope + Pending detached dispatch
  -> scope Closing
  -> dispatch Cancelled(scope-before-admission)
  -> start delivery cannot admit child

Open scope + Started detached dispatch
  -> scope Closing
  -> dispatch Started(scope-cancellation-requested)
     + deterministic child-cancel outbox item
  -> actor Cancel
  -> child terminal checkpoint
  -> dispatch Cancelled
  -> detached parent remains unchanged
```

Waited dispatches are not direct scope-cleanup targets and retain production parent-cancellation behavior. Terminal dispatches remain unchanged. Parent cancellation is waited-only and scope cleanup is detached-only, so they do not claim dual cancellation authority over one dispatch mode.

## Provider compatibility

Missing scope fields deserialize to null. Groundwork stores scope lifecycle separately and materializes scope ID on dispatch documents for bounded queries. In-memory state preserves equivalent logical transactions but not process-crash durability.
