# Finite file ETL

Optional `Elsa.Etl` implements the execution domain admitted by [ADR 0087](../../docs/adr/0087-etl-owns-its-execution-domain.md). [Spec 255](../../specs/255-finite-file-etl/spec.md) and [issue #3109](https://github.com/elsa-workflows/elsa-foundation/issues/3109) own the complete delivery scope. This implementation checkpoint is **not a runnable Studio/native ETL journey**.

## Implemented boundaries

- Core: typed finite topology, exact tenant/persistence scope, immutable versioned artifact envelope, typed JSON representation and SHA-256. Complete v1 envelopes are limited to 256 KiB; a definition may request a lower limit. Unknown saved formats remain observable and are refused at publication/read boundaries.
- `EtlDesign`: scoped compiler with structured diagnostics, schema/key/mapping/filter/limit validation and deterministic behavioral hash. Artifact identity also includes definition/version provenance. Caller-owned collections are snapshotted for publication.
- `EtlDesignEntityFrameworkCore`: SQLite drafts and immutable publications, optimistic revisions, atomic version publication, compiler-backed artifact admission, schema/migration metadata and separate `EtlDesign` history. A real host AutoMigrate/publish/stop/Validate/reload test covers its registration. Other relational providers are not qualified.
- Runtime internals: bounded strict UTF-8 CSV parsing and byte offsets, typed exact-key lookup/map/filter, safe row diagnostics, immutable host binding pins, verified retained snapshots and a streamed logical-record index. Partition ranges are computed without allocating a range per requested partition.
- Physical files: attempt-private staging, bounded writes, flush/close/hash verification, atomic create-only publication, matching-content replay and conflict refusal. Unix publication requires hard-link support; there is no copy fallback. Windows uses its no-replace move. A physical file is not an accepted ETL result.

Only the design features are registered so far. The runtime components are internal building blocks; there is no worker execution feature, run store, API or Studio module to enable yet.

## Remaining delivery

The ETL runtime context, durable admission/preparation, partition leases, record/byte reservations, fenced manifest/cursor settlement and terminal outbox are still required. Native integration must prove Sequence sibling continuation and Flowchart completion through the real bookmark path. API/Studio, two-process recovery, rebuilt-server/browser acceptance and resulting-main checks remain owed.

Host bindings are trusted configuration, never authored paths. Admission must retain exact source/lookup and output revisions before acknowledging work. Preparation accepts those pins and cannot re-resolve the current alias. Removed revisions refuse continuation; they do not redirect it. Runtime-only execution must not require the design database.

Snapshots and output use a shared filesystem whose atomicity and durability the host must support. A failed preparation can leave a complete unreferenced source snapshot; process loss can leave attempt staging. Retention must account for these deterministic candidate keys and remove abandoned files only after their writer has stopped. No automatic orphan collector is implemented. No power-loss durability qualification is claimed by the process-level tests.

Physical effects remain at least once. Lease expiry cannot terminate a worker or release its live buffers. Only a fenced manifest/cursor transaction will make output authoritative; that transaction is not implemented in this checkpoint.
