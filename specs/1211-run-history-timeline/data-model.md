# Data model: Runtime-owned run history

Product requirements are in [spec.md](spec.md); atomic invariants in [contracts/atomic-history.md](contracts/atomic-history.md). Names below describe the implementation boundary, not a new domain.

## Logical candidate and persisted event

The existing RuntimeHistoryEvent envelope supplies identity, run, category/name, observed instant and optional execution/node/checkpoint/incident context. Preserve legacy constructors and contracts. Add typed history context only where required; candidates have no storage-assigned sequence/physical boundary yet. The persisted representation adds workflow-local sequence, physical atomic commit ID/committed instant and stable logical ordinal/source checkpoint.

Required semantic context includes kind/schema version, provenance authority, exact optional execution/node/bookmark/delivery/incident/related-run and causal identity, safe outcomes/reason and value-evidence reference/disposition. Do not serialize full source metadata, checkpoint inputs, exception messages, bookmark payloads or signed capabilities. No inline value payload is required for the baseline.

An optional versioned history batch is a RuntimeCheckpointCommit write sidecar, separate from authoritative RuntimeCheckpointStateChangeSet and RuntimeStateCategory. The coalescing session preserves its ordered logical groups independently of continuation-state folding. Candidate IDs derive from logical checkpoint identity plus semantic kind/subject/ordinal. They are not wall-clock/random IDs. Conflicting content under an existing candidate ID refuses the checkpoint. Repeating an equivalent durable checkpoint allocates no new sequence.

Capture profile is pinned to admission/run identity and carried through retry. Legacy profile absence means uncaptured rather than automatic retroactive enrichment. A later explicit policy change has its own new effective boundary; old queued logical commits retain their original profile/fingerprint.

## Scoped head

One head per exact persistence scope/run tracks last allocated sequence, retained lower boundary, last covered physical checkpoint, pinned Required capture policy, capture-from-start versus late start, capture/gap/retention boundaries and concurrency revision. Scope and run use existing encoded identity/hash agreement. A current terminal status is read from authorized run state, not inferred from the last event name.

Allocate a contiguous range once per physical checkpoint and update the head in that transaction. No rolled-back transaction leaves a head advance. A new checkpoint missing a pinned Required batch is refused, not silently admitted with a gap. Any supported disable/late-enable is an explicit policy boundary and retains its uncaptured interval; never replace it with a complete-looking new head.

## Immutable event row

Key: scoped run + stable event identity; unique scoped run + sequence. Index bounded run sequence, exact event, owning execution, bookmark and relevant delivery/kind identities. Physical and logical checkpoint identities remain separate. Safe common content is validated against row projections/schema version before return, following existing Runtime persistence rules.

Each stored event is immutable. Semantic corrections are additional explicitly related events; no mutation of historical success to hide a later failure. Purge is an explicit retention operation that advances retained coverage, not an interpretation of current state.

## Bookmark lifecycle projection

Key: scoped run + bookmark ID. Includes owning execution/node, safe stimulus/target, creation event/sequence/time, expiry, last recorded lifecycle event/sequence, disposition/reason and causal delivery/continuation references. It contains no raw bookmark payload/capability.

Created handles become Active. Selected initial trigger claim becomes Consumed. Siblings become Retired with the winning handle/claim cause. Stale terminal-handle deletion is cleanup, not resumption. Cancellation/removal/supersession record their actual reason. Rearming creates a different handle. Expiry does not change recorded lifecycle: the API computes current matchability at an identified observation instant.

This projection is staged atomically with events/head and permits bounded activity inventory without grouping an entire event history on every read. Creation-sequence keyset paging and active/lifetime counts use explicit scoped projections; counts are not fabricated from one page.

A first observed consumption/removal after late capture or an explicit gap may have unknown prior state/creation. Keep that unknown state and first-observed sequence for paging; do not fabricate a creation fact/time or a complete lifetime count.

## Query range and continuation

A page contains records, available capture/retention/granularity/settlement coverage, bounded traversal position, captured upper watermark, next continuation and optional new-events information. Native checkpoint history has no asynchronous materializer: committed native/EF history is settled through its acknowledged physical boundary. Optional independent source responsibilities retain their own pending state until acknowledged. A settled prefix does not close the history to future events or late ingress observations. Optional source limitations remain explicit.

Exact IDs use indexed lookup. Free text searches a bounded ordered window of safe summary/context fields and reports searched-through coverage; an empty window may still have more range. Do not return a full filtered total based on an unbounded scan. Continuations authenticate purpose/run/query/limit/scope/tenant/subject/authorization context and are reauthorized on use.

## Retention and values

Run deletion atomically removes head/events/bookmark projections through existing owned deletion. Explicit earlier history purge preserves retained/coverage boundaries. A shorter captured-value lifetime yields an explicit value disposition; history references existing phase-specific evidence and never causes automatic sensitive payload resolution.

Implementation indexes are scoped projections: bookmark counts by run/exact activity and known activity ancestry support bounded inventories/descendants without whole-history grouping or reliance on later native state. Explicit purge retains compact event identity/fingerprint/original-position receipts until run deletion; a receipt is not readable event content and cannot imply an available historical detail.

Optional timers retain bounded typed history responsibility on their existing owner document. The schedule and terminal responsibilities have stable identities, pinned policy and safe metadata, with retirement/acknowledgement state. An independently appended timer observation does not advance native checkpoint coverage. Missing native run/head retains pending responsibility; it does not create a synthetic captured run. Timer retirement is non-fireable and cannot discard an unacknowledged responsibility. This optional owner recovery is distinct from the direct atomic native history path.

## Final owner provenance and no-record source qualification

The final admitted profile3 carries a null-omitted typed checkpoint value delta on the existing inspection contribution: exact checkpoint/work binding and ordered new evidence IDs, including known-empty reprojection. Owners stamp actual invocation/resume/evaluation provenance before merge. Capture must not query a later inspection projection to choose an earlier delta. Actual incident owners distinguish initial creation, strategy evaluation, reconciliation and terminal resolution; a repeated upsert is not another initial fault.

The timer-owner no-record path discharges only a bounded retired prefix after exhaustive indexed active refusal, under actual completed/no-record/fence/native-absence proof. Source creation and discharge lock native liveness before their first consistent read. The new pending-run index retains its equality prefix, retired predicate and deterministic timer-hash tie-breaker. Compact completion receipts preserve idempotency; first later native history retains an explicit gap. After real native authority exists, it governs legitimate new attempts/recovery; old source receipts do not become permanent execution authority.
