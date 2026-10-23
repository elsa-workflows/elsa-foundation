# Data Model: Bounded Durable-Value Page Reuse

This feature adds private, transient state to one active coalescing session. It adds no persisted entity, migration, or public model/interface. `RuntimeStorePage<T>` and the existing durable-value models remain the caller-facing contracts.

## Reuse owner

**Runtime coalescing session** is the authority and lifetime boundary. It already binds a workflow execution and has an active/deactivated state. It is eligible only while the current call belongs to that same active owner, its execution matches, the mode is Coalesced, the option is enabled, and the first-party EF durable-store composition was captured as eligible.

The existing session accessor is nested. A child coalescing scope receives an independent empty memo only when it has a legitimate active owner. Entering it permanently suspends the parent memo for the remainder of that parent scope. Popping the child does not reactivate the parent memo. Session disposal, deactivation, interruption, and recovery remove reusable cache state; they do not change the existing staged overlay.

## Memo key

Each entry is keyed by the exact inner-page request and its current authorization domain:

| Field | Meaning |
|---|---|
| Persistence scope | Current selected tenant/partition value. |
| Access policy | Ordinary or privileged policy; reuse is admitted only for the existing eligible ordinary one-scope path. |
| Access purpose | Current purpose value when present; included so future/current context changes cannot alias. |
| Across-scopes flag | Current access-context flag; across-scope requests bypass. |
| Workflow execution ID | Execution identity passed to the inner durable-value query. |
| Requested page limit | Exact limit sent to the provider. |
| Input continuation | Exact opaque cursor string, including null. Runtime Services does not decode or normalize it. |
| Continuation codec identity | Object reference for the built-in stable HMAC codec. A different instance cannot reuse an entry validated under a different key/configuration. |

The owning session is implicit in the cache instance and is checked again for every lookup and fill. No entry is shared between sessions or workflow executions.

## Memo entry

An entry stores only a detached successful inner `RuntimeStorePage<DurableValueState>` snapshot:

- A private array of detached rows.
- The exact output continuation token, if any.
- Deterministic accounted-content, page, and row totals for bounds.
- The generation at which the provider read began and completed.

The cache does not store merged rows, activity inputs, workflow outputs, point reads, errors, or exceptions. A successful empty page is a valid entry. Rejection, cancellation, or any exception produces no entry. On every cache hit, a fresh detached row set is returned to the existing merger, which applies the current staged overlay.

## Cached row value

`DurableValueState` data must be copied as a complete model value, not as a shallow record/reference copy:

| Field | Cached representation |
|---|---|
| `DurableValueId`, `WorkflowExecutionId`, `ValueId` | Immutable string values copied into the detached model. |
| `Type` (`Kind`, `Id`, `Schema`) | New descriptor; clone `Schema` JSON value. |
| `Lifecycle`, `Storage` | Enum values. |
| `InlineValue` | Clone JSON value so the returned `JsonElement` does not depend on source lifetime or caller mutation. |
| `ExternalReference` (`StorageProfile`, `Locator`, `Metadata`) | New external-reference value with a detached metadata snapshot. |
| `SourceActivityExecutionId`, `CapturedAt` | Immutable scalar values. |
| `Metadata` | New dictionary snapshot; do not retain a caller-owned mutable dictionary. |

Clone when admitting the memo-owned snapshot and clone again when returning a hit. The second clone prevents mutation through a returned row from changing later hits.

## Cache lifecycle and bounds

The cache-local state consists of a monotonically advancing generation, a counted number of active inner writes, an admission-enabled flag, and page/row/content counters for the active generation. The synchronization protecting this state is local to the memo; it does not turn the broader runtime session into a concurrent session or serialize database calls.

Initial session-local limits for the active generation are 32 pages, 1,024 rows, and 4 MiB of conservatively accounted retained content. Accounted content includes all key strings (including cursor input), output cursor strings, every durable-value string and metadata key/value, JSON raw UTF-8, and fixed deterministic row/entry overhead. Use checked arithmetic before cloning. A successful boundary clears the prior generation before a fresh segment starts, keeping resident memo state within the same caps. These figures are implementation guardrails; they do not claim exact CLR heap use or an optimum based on benchmark data.

On a capacity boundary, return the full page already obtained from the provider, clear entries, and stop admitting entries until a successful boundary/reset. A successful inner commit ends the current data generation and may begin a fresh empty generation. Failure/cancellation disables admissions through the remainder of the owning session. No oversized result is truncated, partially cached, or returned partially.

## State transitions

| Event | Memo behavior |
|---|---|
| Eligible successful page read | Admit only if its captured generation, execution owner, active context, and codec identity remain current. |
| Logical deferred checkpoint buffered in memory | Keep baseline entries; the existing overlay is still re-applied on each read. |
| Actual inner checkpoint commit or direct durable-value save/delete begins | Advance generation, clear entries, mark write active; bypass lookup and admission while any write is active. |
| Inner write ends | Fence and clear again. On failure/cancellation permanently disable admission for this session; on success permit an empty new generation only if admission has not already been permanently disabled. |
| Read completes after a generation/owner/context change | Return the provider result to its caller as normal but do not admit it. |
| Nested owner entered | Suspend parent memo permanently for that parent scope; child starts empty if independently eligible. |
| Capacity exhausted | Return complete provider result, clear entries, disable admission until successful boundary/reset. |
| Owner disposed, deactivated, interrupted, or recovered | Clear entries. A retried/recovered execution starts in a new empty session. |
