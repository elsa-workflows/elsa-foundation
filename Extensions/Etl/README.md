# Finite file ETL

Optional `Elsa.Etl` implements the execution domain admitted by [ADR 0087](../../docs/adr/0087-etl-owns-its-execution-domain.md). [Spec 255](../../specs/255-finite-file-etl/spec.md) and [issue #3109](https://github.com/elsa-workflows/elsa-foundation/issues/3109) own the complete delivery scope. Durable fixtures and the [rebuilt Workbench HTTP journey](../../specs/255-finite-file-etl/evidence/workbench-http.md) exercise native invocation and continuation. The [Studio browser journey](../../specs/255-finite-file-etl/evidence/studio-browser.md) records authoring, publication, exact selection, native execution and inspection; each receipt identifies its qualified source state.

## Finite HTTP connector work in progress

[Spec 996](../../specs/996-http-etl/spec.md) and [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151)
extend the finite runtime with HTTP JSON sources and HTTP/JSONL routes. The
[shared-worker checkpoint](../../specs/996-http-etl/evidence/http-worker-checkpoint.md) records actual HTTP-to-HTTP,
HTTP-to-JSONL and CSV-to-HTTP execution with optional CSV lookup and durable retries. It uses the same worker
capacity, terminal delivery and optional provider boundary as file ETL. Full HTTP native continuation, Studio,
operator and process-recovery acceptance is still in progress; the file-only evidence below does not establish it.

## Implemented boundaries

- Core: typed finite topology, exact nullable-tenant/required-persistence scope, immutable versioned artifact envelope, typed JSON representation and SHA-256. Complete v1 envelopes are limited to 256 KiB; a definition may request a lower limit. Unknown saved formats remain observable and are refused at publication/read boundaries.
- `EtlDesign`: scoped compiler with structured diagnostics, schema/key/mapping/filter/limit validation and deterministic behavioral hash. Artifact identity also includes definition/version provenance. Caller-owned collections are snapshotted for publication.
- `EtlDesignEntityFrameworkCore`: SQLite drafts and immutable publications, optimistic revisions, atomic version publication, compiler-backed artifact admission, schema/migration metadata and separate `EtlDesign` history. A real host AutoMigrate/publish/stop/Validate/reload test covers its registration. Other relational providers are not qualified.
- Runtime processing: bounded strict UTF-8 CSV parsing and byte offsets, typed exact-key lookup/map/filter, safe row diagnostics, immutable host binding pins, verified retained snapshots and a streamed logical-record index. Partition ranges are computed without allocating a range per requested partition.
- Physical files: attempt-private staging, bounded writes, flush/close/hash verification, atomic create-only publication, matching-content replay and conflict refusal. The internal terminal materializer streams only manifest-accepted chunks into deterministic main/error JSONL routes, verifying exact pinned revisions/content and labelling cancelled/faulted output partial. Unix publication requires hard-link support; there is no copy fallback. Windows uses its no-replace move. A physical file is not an accepted ETL result.

- `EtlRuntime`: bounded background attempts, retained executable validation, independently leased preparation/partitions and process-local record/input/lookup/output allowances. The Tasks lifecycle starts/stops shell workers; plain hosts use the same instance as a hosted service. Compute uses explicitly configured scopes, retained binding revisions and lookup row/cell ceilings. Terminal delivery discovers pending work in the composition's runtime database independently of the current binding catalog, validates each retained invocation's exact scope and run identity, and creates a scoped native operation. Removing file bindings therefore does not orphan an already-owed result. Invalid candidates remain pending while valid later candidates can progress; this is not a tenant-default fallback or permission to execute files outside configured bindings.
- `EtlRuntimeEntityFrameworkCore`: separate SQLite runtime context, schema and migration history; short idempotent admission, pinned bindings, immutable preparation metadata, durable batch reservations, fenced manifest/cursor/quota settlement, first-wins terminal result and recoverable delivery outbox. Runtime DI/database composition does not require design services or design tables. The shared persistence package still references the design assembly.
- [Native workflow adapter](src/Workflows/Runtime/README.md): typed `RunEtl` wait, actual-bookmark admission, private-state/consumed-receipt cancellation, scoped terminal delivery and durable acknowledgement proofs. The [publication contributor](src/Workflows/Design/README.md) binds the full artifact hash through a literal selection and embeds the artifact without workflow dependency edges. [Persisted parent publication](../../specs/255-finite-file-etl/evidence/persisted-parent-publication.md) and [native cancellation evidence](../../specs/255-finite-file-etl/evidence/native-subtree-cancellation.md) cover the ordinary catalog/compiler and native composite boundaries.
- `EtlDesignApi` and `EtlRuntimeApi`: independently composed, permission-checked operations for authoring/publication and runtime inspection/cancellation. Bounded signed cursors preserve scope and query identity. Responses expose safe binding aliases and retained facts, not host paths. New publication rechecks current binding approval; replay of an existing immutable publication does not. Runtime inspection does not require design services or active workers.
- [EtlStudio](studio/Elsa.Studio.Etl/EXTENSION_POINTS.md): structured finite-stage authoring, saved-revision validation/publication, exact published selection through the public `RunEtl` property editor, and run/partition inspection. [Component evidence](../../specs/255-finite-file-etl/evidence/studio-components.md) records executed checks without substituting them for browser acceptance.

A compiler-produced valid/filtered/rejected fixture runs through two real hosts sharing SQLite/filesystem storage, then a separate host reopens accepted output and the owed terminal delivery. These hosts run inside one test process. Separate [two-process recovery](../../specs/255-finite-file-etl/evidence/two-process-recovery.md) and [concurrent partition proof](../../specs/255-finite-file-etl/evidence/round-1-review.md) exercise distinct worker processes, interruption and survivor takeover. Native parent delivery has separate durable fixture evidence linked above.

## Host composition and operation

Enable only the responsibilities a host needs:

| Responsibility | Features / prerequisites |
|---|---|
| Author and publish ETL | `EtlDesign`, `EtlDesignEntityFrameworkCore`, `EtlDesignApi`; Foundation Identity, API capabilities, host persistence scope and a scoped `IEtlBindingCatalog` |
| Execute accepted runs | `EtlRuntime`, `EtlRuntimeEntityFrameworkCore`; Tasks lifecycle, shared input/snapshot/output storage and explicit file-binding revisions |
| Inspect or cancel runs | `EtlRuntimeApi`, `EtlRuntimeEntityFrameworkCore`; Foundation Identity, API capabilities and host persistence scope; no compute worker required |
| Invoke from native workflows | `EtlWorkflowsDesign` on the native publication host, `EtlWorkflowsRuntime` on the native execution host, plus the applicable ETL/runtime stores |
| Author and inspect in Studio | `EtlStudio` with the ordinary Studio authenticated backend configuration; backend authoring/runtime capabilities must be available |

The Workbench references the modules for discovery but does not enable ETL in its default shell. The reproducible
[HTTP/browser fixtures](../../e2e-tests/etl/README.md) show opt-in configuration with isolated databases and file
bindings. They use SQLite, the qualified provider for this slice; they are not production deployment templates.
Set API cursor signing keys consistently across replicas. Set tenant and persistence partition explicitly;
`null` tenant is a distinct global scope, not an alias for `default`.

Before accepting work, configure immutable source/lookup revisions and SHA-256 pins, output roots supporting
the required atomic publication, snapshot capacity and bounded worker allowances. Replicas participating in the
same run must see its retained revisions, database and storage. Changing a current alias affects later admission;
retain older revisions until their admitted runs no longer need them. Inspect terminal diagnostics and partition
cursors before taking recovery action. A successful HTTP cancellation reports the durable terminal outcome; it
does not establish that every physical operation has stopped.

For rollback, stop new ETL authoring/publication and invocation admission first, while retaining compatible
readers, workers, bindings and result delivery for accepted work. Do not delete ETL tables, snapshots, chunks or
native waiting state as a rollback shortcut. The initial ETL schemas have no previous ETL release to demonstrate
a downgrade or historical upgrade against; unknown saved formats must be refused rather than reinterpreted.
There is no general pause/resume, automatic orphan collection or retry/reset administration surface in this slice.

## Remaining delivery

Native invocation/result delivery and native cancellation now have real durable Sequence, Flowchart and CompensationScope evidence, including placed boundaries and delayed admission. The terminal outbox is acknowledged only after demonstrated consumption or terminal parent/activity state, never merely queueing. [Live-attempt replacement](../../specs/255-finite-file-etl/evidence/live-attempt-replacement.md) proves real SQLite takeover, cancellation, retained record/byte allowances through cleanup and successor replay under its own permit. The [owned two-process fixture](../../specs/255-finite-file-etl/evidence/two-process-recovery.md) proves survivor recovery after a predecessor exits with a published, unaccepted chunk. The [interruption matrix](../../specs/255-finite-file-etl/evidence/interruption-matrix.md) also proves real read/reservation, source/JSONL staging, committed-cursor and result-claim interruption, plus conflict refusal after takeover. Store/native tests separately establish stale cursor/disposition and consumed-receipt semantics. The [identity/format matrix](../../specs/255-finite-file-etl/evidence/recovery-identity-formats.md), [SQLite provider qualification](../../specs/255-finite-file-etl/evidence/provider-reopen.md) and Studio/HTTP receipts record executed acceptance and its limits. Current integrated validation, review, CI and resulting-main delivery checks are tracked in [tasks T034–T036](../../specs/255-finite-file-etl/tasks.md); older receipts do not substitute for those gates.

Host bindings are trusted configuration, never authored paths. Admission retains exact source/lookup and output revisions before acknowledging work. Preparation accepts those pins and cannot re-resolve the current alias. Removed revisions refuse continuation; they do not redirect it. Runtime-only execution must not require the design database.

Snapshots and output use a shared filesystem whose atomicity and durability the host must support. A failed preparation can leave a complete unreferenced source snapshot; process loss can leave attempt staging. Retention must account for these deterministic candidate keys and remove abandoned files only after their writer has stopped. No automatic orphan collector is implemented. No power-loss durability qualification is claimed by the process-level tests.

Physical effects remain at least once. Lease expiry cannot terminate a worker or release its live buffers. Only the fenced manifest/cursor transaction makes output authoritative. Logical payload allowances are not a heap/RSS limit, and retained chunks are not automatically removed or compensated.
