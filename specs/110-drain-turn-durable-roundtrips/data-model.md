# Data Model — spec 110

No new persisted document kinds, no storage-manifest change, no schema-version bump. This unit adds
in-memory instrumentation and (Phase 1) an in-memory reconstructible read cache. Nothing durable
changes.

## Diagnostic entities (benchmark-local, not persisted)

- **DurableRoundTripCounters** — per-run tallies: `checkpointCommits` (== `checkpointCommit` marker
  documents), scheduler-queue transitions (`enqueue`, `dequeue`, `delete`, `consume`, `claim`,
  `complete`, `release`, `list`), root-write-lease writes (`acquire`, `release`, `renew`), and
  executable-store reads (`find`) / writes (`save`) / paging (`listPage`).

## Phase 1 cache (in-memory, reconstructible)

- **Executable read cache** — a per-drain map `ArtifactId → WorkflowExecutable`. Populated lazily on the
  first durable `FindAsync` for an id within the drain; read-through on subsequent resolutions. Holds
  only already-durable, immutable, content-addressed artifacts (ADR 0038); disposed at drain end. Never
  a source of truth — a miss re-reads the durable store with a byte-identical result.

## Invariants preserved (unchanged by this unit)

- W5 terminal-status fencing, RT-2 single-writer/ownership fencing, ADR 0020 checkpoint+post-commit
  atomicity, spec-105 consume-idempotency, and crash-redrive all live on the commit/fence/queue path,
  which this unit does not touch. The read cache carries no state that affects any of them.
