# Finite ETL

Optional `Elsa.Etl` implements the execution domain admitted by [ADR 0087](../../docs/adr/0087-etl-owns-its-execution-domain.md). [Spec 255](../../specs/255-finite-file-etl/spec.md) and [issue #3109](https://github.com/elsa-workflows/elsa-foundation/issues/3109) own the complete delivery scope. Durable fixtures and the [rebuilt Workbench HTTP journey](../../specs/255-finite-file-etl/evidence/workbench-http.md) exercise native invocation and continuation. The [Studio browser journey](../../specs/255-finite-file-etl/evidence/studio-browser.md) records authoring, publication, exact selection, native execution and inspection; each receipt identifies its qualified source state.

## Finite HTTP connectors

[Spec 996](../../specs/996-http-etl/spec.md) and [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151)
extend the finite runtime with HTTP JSON sources and HTTP/JSONL routes. The
[shared-worker checkpoint](../../specs/996-http-etl/evidence/http-worker-checkpoint.md) records HTTP-to-HTTP,
HTTP-to-JSONL and CSV-to-HTTP execution with optional CSV lookup and durable retries. These use the same worker
capacity and terminal delivery as file ETL, with additive connector provider contracts.

## JSON Lines file input

[Spec 1190](../../specs/1190-etl-jsonl-input/spec.md), part of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908),
adds a `jsonl` source: a host-approved file holding one JSON object per line, with fields selected by JSON Pointer.
It reuses the CSV file path end to end (pinned approval, retained snapshot, byte index, file partitions, owed
batches and fenced acceptance). Malformed, non-object and over-long lines are routed record errors rather than run
failures. A file run with zero records settles as success when its preparation commits, for CSV and JSON Lines alike.

## CSV file delivery

[Spec 1191](../../specs/1191-etl-csv-delivery/spec.md) adds a `csv` destination for either route. Each published batch is
one RFC 4180 file (comma, double-quote quoting, CRLF, UTF-8 without a BOM) beginning with its header row: the mapped
field names on the main route, `recordOrdinal,code,field` on the error route. Values keep the canonical text of their
typed values. The CSV bytes, header included, are what the batch output limit bounds; a row that does not fit is
rejected as `etl.output-too-large`. Retention, effect identity, create-only `.csv` publication, verification and fenced
acceptance are the JSON Lines file path. The connector store's effect columns record the delivery mechanism (HTTP or
file), so CSV needed no migration. A single-column mapping writes a null as an empty line; Elsa's CSV reader reads it as one null field, but many external readers skip blank lines. An HTTP route's closing bracket is reserved with its first element: a file route filling the output limit can no longer fail the batch, and an HTTP main route's exact-limit first row is rejected instead of failing it.

[Native continuation](../../specs/996-http-etl/evidence/http-native-checkpoint.md),
[process recovery](../../specs/996-http-etl/evidence/http-process-checkpoint.md),
[cancellation](../../specs/996-http-etl/evidence/http-cancellation-checkpoint.md),
[operator inspection](../../specs/996-http-etl/evidence/connector-inspection-checkpoint.md) and
[real Workbench/Studio journeys](../../specs/996-http-etl/evidence/http-live-acceptance-checkpoint.md)
record executed acceptance at their stated source checkpoints. A
[frozen predecessor/current successor proof](../../specs/996-http-etl/evidence/v1-native-upgrade-checkpoint.md)
also resumes an existing file run and its surrounding native Sequence. Final integrated review, CI and
resulting-main delivery remain tracked by [T020–T022](../../specs/996-http-etl/tasks.md); these receipts do not
claim a released HTTP connector or completion of the wider program.

### HTTP credentials and diagnostics

Each physical HTTP attempt resolves its retained connection under the run's tenant. Credential rotation can
therefore take effect on a retry without changing the non-secret pin. Revocation or non-secret configuration
drift refuses the retry; it does not silently redirect the run or use anonymous credentials. These point-of-use
checks cannot recall a request already sent to the remote system.

ETL-owned errors and worker logs use stable codes, counts and failure types. They omit raw request URLs,
credentials, headers, payload values and provider exception details. The transport constructs its own HTTP
client without an automatic URI logger and currently emits no custom ETL spans. This does not disable platform HTTP tracing: under the
[.NET URI-redaction default](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-builtin-activities),
`System.Net.Http` request spans retain the origin and path while replacing the query with `*`. Hosts own
exporters, additional instrumentation and redaction configuration. Do not place credentials in endpoint paths
or assume that arbitrary host telemetry is covered by ETL's diagnostic boundary. The workflow activity fault
mask is a separate adapter concern, not a global telemetry scrubber.

## Continuous HTTP runs

[Spec 1192](../../specs/1192-etl-continuous-http/spec.md) lets an HTTP pipeline run continuously: set
`continuous: { pollIntervalSeconds }` (1–86,400) on a source with token or next-link pagination. A pipeline without it
is finite and keeps its exact artifact bytes. Each host shard is still one independently leased partition.

- **Feed contract.** Re-requesting a cursor returns the records after it, and every page with records carries a
  continuation. When nothing is new, the feed answers with no records and a continuation that is absent or the same
  cursor. A page with records and no continuation ends that shard, as in a finite run. A collection that re-lists
  everything is not a continuous source. Single-page pagination and file sources are refused.
- **Idling.** A caught-up response retains nothing: the request's fetch journal is cleared, its reservations are
  released and the shard polls the same cursor again after the interval. A cursor that moves without records waits one
  interval too, but each such response is a retained page.
- **Completion.** `POST /etl/runs/{id}/complete` (permission `etl/runs:complete`, Studio "Complete run") records a
  durable request and makes idle shards due at once. Each shard ends at its first caught-up response from an attempt
  started after the request, retained as a final empty page; then the run settles `Succeeded`, or `BusinessError` if
  records were rejected. Cancellation still settles `Cancelled`, and the first terminal outcome wins. A native workflow
  waits on `RunEtl` until the run settles; it cannot request completion itself yet.
- **Bounded state** ([spec 1196](../../specs/1196-etl-continuous-compaction/spec.md)). Every claim in a continuous
  run deletes the rows behind every shard's window, in the claim transaction and before any work is charged: it keeps the
  current request, the predecessor page and request, and that page's batches. The partition keeps the deleted rows'
  counts as a compacted summary, so page, batch, record and effect counts stay lifetime totals. A shard holds at most
  2 pages, 3 requests and 2 pages' batches; size the retention budgets for that peak once per shard, since they are per
  run. The worker deletes a batch's retained route bodies after its acceptance and a left page's cached files after the
  advance; a crash in between leaves those files behind once. `MaxPagesPerPartition` does not limit a continuous
  shard, which may accept up to 2³¹ − 1 pages over its life.
- **Limits.** In a continuous run, a continuation that revisits a request older than the window is not detected as a
  cycle and is fetched again, so a continuous feed must move its cursor forward. A feed outage longer than the fetch
  deadline (`DeadlineSeconds`) fails the run. Workers without these features refuse a continuous artifact; upgrade every
  worker in a scope before admitting one. Claims are ordered by run creation, so a continuous run whose shards are never
  idle can keep every connector slot in its scope and delay newer runs. Deciding whether a caught-up response ends a
  draining shard compares host clocks, which must agree, as leases already require. Each caught-up poll increments the
  shard's poll generation, which is part of its page-cache key, so a stalled worker's late page never reaches a later
  poll; that orphaned page stays on disk.

## Implemented boundaries

- Core: typed finite topology, exact nullable-tenant/required-persistence scope, immutable versioned artifact envelope, typed JSON representation and SHA-256. Complete v1 envelopes are limited to 256 KiB; a definition may request a lower limit. Unknown saved formats remain observable and are refused at publication/read boundaries.
- `EtlDesign`: scoped compiler with structured diagnostics, schema/key/mapping/filter/limit validation and deterministic behavioral hash. Artifact identity also includes definition/version provenance. Caller-owned collections are snapshotted for publication.
- `EtlDesignEntityFrameworkCore`: SQLite drafts and immutable publications, optimistic revisions, atomic version publication, compiler-backed artifact admission, schema/migration metadata and separate `EtlDesign` history. A real host AutoMigrate/publish/stop/Validate/reload test covers its registration. Other relational providers are not qualified.
- Runtime processing: bounded strict UTF-8 CSV parsing and byte offsets, typed exact-key lookup/map/filter, safe row diagnostics, immutable host binding pins, verified retained snapshots and a streamed logical-record index. Partition ranges are computed without allocating a range per requested partition.
- Physical files: attempt-private staging, bounded writes, flush/close/hash verification, atomic create-only publication, matching-content replay and conflict refusal. The internal terminal materializer streams only manifest-accepted chunks into deterministic main/error JSONL routes, verifying exact pinned revisions/content and labelling cancelled/faulted output partial. Unix publication requires hard-link support; there is no copy fallback. Windows uses its no-replace move. A physical file is not an accepted ETL result.

- `EtlRuntime`: bounded background attempts, retained executable validation, independently leased preparation/partitions and process-local record/input/lookup/output allowances. The Tasks lifecycle starts/stops shell workers; plain hosts use the same instance as a hosted service. Compute uses explicitly configured scopes, retained binding revisions and lookup row/cell ceilings. Terminal delivery discovers pending work in the composition's runtime database independently of the current binding catalog, validates each retained invocation's exact scope and run identity, and creates a scoped native operation. Removing file bindings therefore does not orphan an already-owed result. Invalid candidates remain pending while valid later candidates can progress; this is not a tenant-default fallback or permission to execute files outside configured bindings.
- `EtlRuntimeEntityFrameworkCore`: separate SQLite runtime context, schema and migration history; short idempotent admission, pinned bindings, immutable preparation metadata, durable batch reservations, fenced manifest/cursor/quota settlement, first-wins terminal result and recoverable delivery outbox. Runtime DI/database composition does not require design services or design tables. The shared persistence package still references the design assembly.
- [Native workflow adapter](src/Workflows/Runtime/README.md): typed `RunEtl` wait, actual-bookmark admission, private-state/consumed-receipt cancellation, scoped terminal delivery and durable acknowledgement proofs. The [publication contributor](src/Workflows/Design/README.md) binds the full artifact hash through a literal selection and embeds the artifact without workflow dependency edges. [Persisted parent publication](../../specs/255-finite-file-etl/evidence/persisted-parent-publication.md) and [native cancellation evidence](../../specs/255-finite-file-etl/evidence/native-subtree-cancellation.md) cover the ordinary catalog/compiler and native composite boundaries.
- `EtlDesignApi` and `EtlRuntimeApi`: independently composed, permission-checked operations for authoring/publication and runtime inspection, cancellation and continuous-run completion. Bounded signed cursors preserve scope and query identity. Responses expose safe binding aliases and retained facts, not host paths. New publication rechecks current binding approval; replay of an existing immutable publication does not. Runtime inspection does not require design services or active workers. Connector runs use the optional inspector and a separately advertised typed partition endpoint; page/item positions, retry state and owed receipts remain distinct from accepted route-body counts. [Inspection evidence and limits](../../specs/996-http-etl/evidence/connector-inspection-checkpoint.md).
- [EtlStudio](studio/Elsa.Studio.Etl/EXTENSION_POINTS.md): structured finite-stage authoring, saved-revision validation/publication, exact published selection through the public `RunEtl` property editor, and run/partition inspection. [Component evidence](../../specs/255-finite-file-etl/evidence/studio-components.md) records executed checks without substituting them for browser acceptance.

A compiler-produced valid/filtered/rejected fixture runs through two real hosts sharing SQLite/filesystem storage, then a separate host reopens accepted output and the owed terminal delivery. These hosts run inside one test process. Separate [two-process recovery](../../specs/255-finite-file-etl/evidence/two-process-recovery.md) and [concurrent partition proof](../../specs/255-finite-file-etl/evidence/round-1-review.md) exercise distinct worker processes, interruption and survivor takeover. Native parent delivery has separate durable fixture evidence linked above.

## Host composition and operation

Enable only the responsibilities a host needs:

| Responsibility | Features / prerequisites |
|---|---|
| Author and publish ETL | `EtlDesign`, `EtlDesignEntityFrameworkCore`, `EtlDesignApi`; Foundation Identity, API capabilities, host persistence scope and a scoped `IEtlBindingCatalog` |
| Execute accepted runs | `EtlRuntime`, `EtlRuntimeEntityFrameworkCore`; Tasks lifecycle, shared input/snapshot/output storage and explicit binding revisions |
| Author or execute v2 connectors | Add `IEtlConnectorBindingCatalog` and `IEtlConnectorBindingResolver`; configure `EtlRuntimeFeature.HttpBindings` and independent `HttpLimits`. Authenticated bindings require public Connections providers and the retained tenant; anonymous bindings do not |
| Inspect, cancel or complete runs | `EtlRuntimeApi`, `EtlRuntimeEntityFrameworkCore`; Foundation Identity, API capabilities and host persistence scope; no compute worker required |
| Invoke from native workflows | `EtlWorkflowsDesign` on the native publication host, `EtlWorkflowsRuntime` on the native execution host, plus the applicable ETL/runtime stores |
| Author and inspect in Studio | `EtlStudio` with the ordinary Studio authenticated backend configuration; backend authoring/runtime capabilities must be available |

The Workbench references the modules for discovery but does not enable ETL in its default shell. The reproducible
[HTTP/browser fixtures](../../e2e-tests/etl/README.md) show opt-in configuration with isolated databases and file
bindings. They use SQLite, the qualified provider for this slice; they are not production deployment templates.
Set API cursor signing keys consistently across replicas. Set tenant and persistence partition explicitly;
`null` tenant is a distinct global scope, not an alias for `default`.

Before accepting file work, configure immutable source/lookup revisions and SHA-256 pins. HTTP revisions pin
endpoint/path policy, shard seeds and non-secret connection identity/settings; credentials are resolved at each
attempt. Configure output roots supporting the required atomic publication, snapshot capacity and bounded
worker allowances. The [binding contracts](src/Core/EXTENSION_POINTS.md#connector-binding-approval) define
these distinct identities. Replicas participating in the
same run must see its retained revisions, database and storage. Changing a current alias affects later admission;
retain older revisions until their admitted runs no longer need them. Inspect terminal diagnostics and partition
cursors before taking recovery action. A successful HTTP cancellation reports the durable terminal outcome; it
does not establish that every physical operation has stopped.

For rollback, stop new ETL authoring/publication and invocation admission first, while retaining compatible
readers, workers, bindings and result delivery for accepted work. Do not delete ETL tables, snapshots, chunks or
native waiting state as a rollback shortcut. Upgrade all participating ETL workers and readers before admitting
v2 runs: older binaries cannot safely consume connector state. SQLite connector migrations are additive and
forward-only; the frozen predecessor proof qualifies the stated v1 upgrade path, not a downgrade. Keep the
original storage paths and retained configuration for accepted work. Unknown saved formats must be refused
rather than reinterpreted.
There is no general pause/resume, automatic orphan collection or retry/reset administration surface in this slice.

## Remaining delivery

Native invocation/result delivery and native cancellation now have real durable Sequence, Flowchart and CompensationScope evidence, including placed boundaries and delayed admission. The terminal outbox is acknowledged only after demonstrated consumption or terminal parent/activity state, never merely queueing. [Live-attempt replacement](../../specs/255-finite-file-etl/evidence/live-attempt-replacement.md) proves real SQLite takeover, cancellation, retained record/byte allowances through cleanup and successor replay under its own permit. The [owned two-process fixture](../../specs/255-finite-file-etl/evidence/two-process-recovery.md) proves survivor recovery after a predecessor exits with a published, unaccepted chunk. The [interruption matrix](../../specs/255-finite-file-etl/evidence/interruption-matrix.md) also proves real read/reservation, source/JSONL staging, committed-cursor and result-claim interruption, plus conflict refusal after takeover. Store/native tests separately establish stale cursor/disposition and consumed-receipt semantics. The [identity/format matrix](../../specs/255-finite-file-etl/evidence/recovery-identity-formats.md), [SQLite provider qualification](../../specs/255-finite-file-etl/evidence/provider-reopen.md) and Studio/HTTP receipts record executed acceptance and its limits. Those file receipts retain their historical scope. Current HTTP integration, review, CI and resulting-main delivery checks are tracked in [tasks T020–T022](../../specs/996-http-etl/tasks.md); older receipts do not substitute for those gates.

Host bindings are trusted configuration, never authored paths. Admission retains exact source/lookup and output revisions before acknowledging work. Preparation accepts those pins and cannot re-resolve the current alias. Removed revisions refuse continuation; they do not redirect it. Runtime-only execution must not require the design database.

Snapshots and output use a shared filesystem whose atomicity and durability the host must support. A failed preparation can leave a complete unreferenced source snapshot; process loss can leave attempt staging. Retention must account for these deterministic candidate keys and remove abandoned files only after their writer has stopped. No automatic orphan collector is implemented. No power-loss durability qualification is claimed by the process-level tests.

Physical effects remain at least once. Lease expiry cannot terminate a worker or release its live buffers. Only the fenced manifest/cursor transaction makes output authoritative. Logical payload allowances are not a heap/RSS limit, and retained chunks are not automatically removed or compensated.
