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

## Keyed SQL delivery

[Spec 1197](../../specs/1197-etl-sql-destination/spec.md) adds a `sql` destination for the main route: a host-approved
PostgreSQL or SQL Server table that each batch upserts by key. Install `Elsa.Etl.Sql.PostgreSql` or
`Elsa.Etl.Sql.SqlServer` and add an `EtlRuntimeFeature.SqlBindings` entry: alias, `Provider` (`postgresql` or `sqlserver`),
a `ConnectionString` from host configuration, `AllowedTables`, and optionally `LedgerTable` (default
`elsa_etl_effect_ledger`) and `CreateLedger` (default true). The pinned binding handle hashes the provider and the parsed
host, port, database and user, never the password.

- **Exactly once per effect.** Each batch is one transaction: a ledger row holding the batch's effect identity and content
  hash, then one upsert statement per record in order. A redelivery finds its ledger row and writes nothing, so a worker
  killed after its commit is replayed without a second write. Different content under the same effect fails with
  `etl.effect-conflict`.
- **Keys and columns.** The key is one to eight output fields mapped from required source fields, and the table needs a
  unique constraint or index on exactly those columns (`etl.sql-key-constraint-missing` otherwise). Columns are the output
  field names, which must be identifiers distinct ignoring case. Identifiers are quoted exactly as authored
  (case-sensitive on PostgreSQL), and `AllowedTables` compares them exactly. The target must be a base table: a missing
  table, a view or another non-table target is a rejection on both engines. A key repeated
  in a batch resolves under the database's collation, the later record winning. Across concurrently processed partitions
  or shards, the winner for a shared key is unspecified.
- **Failures.** A deterministic refusal (a type the column rejects, a NOT NULL or permission error, a missing table)
  fails the run with `etl.sql-write-rejected`; a connection loss, timeout, deadlock or serialization failure retries
  after two seconds, so an outage pauses the run. Without the binding's engine package, admission refuses the run with
  `etl.sql-unavailable`. The error route cannot be SQL.
- **Ledger retention.** The runtime never deletes ledger rows. Delete only the rows of terminal runs: deleting a running
  run's row can allow a second write.

## RabbitMQ delivery

[Spec 1200](../../specs/1200-etl-rabbitmq-delivery/spec.md) adds a `rabbitmq` destination for the main route: a
host-approved exchange and routing key to which each accepted record is published as one persistent JSON message. Install
`Elsa.Etl.RabbitMq` and add an `EtlRuntimeFeature.RabbitMqBindings` entry: alias, an AMQP URI as `ConnectionString` from
host configuration, and `AllowedExchanges` (an empty name allows the default exchange). The pinned binding handle hashes
the parsed host, port, virtual host and user, and the allowed exchanges, never the password. The runtime never declares
exchanges, queues or bindings.

- **Publisher confirms.** A batch's messages are published in order on one channel with confirmation tracking, mandatory
  and persistent, with content type `application/json`. The route is recorded, and the batch may be accepted, only once the
  broker has confirmed every message.
- **At least once, with stable ids.** RabbitMQ has no ledger the runtime could consult, so a batch whose publication was
  interrupted, or whose worker died after the confirms and before the record, is published again in full. Every message
  carries the message id `{effectId}:{index}` and the headers `elsa-etl-run-id`, `elsa-etl-batch-id`,
  `elsa-etl-effect-id` and `elsa-etl-record-index`, identical on every replay, so consumers deduplicate by message id. A
  replayed batch can arrive after messages of later batches.
- **Failures.** A message no queue matches is returned and fails the run with `etl.rabbitmq-unroutable`. A negative
  acknowledgement (for example a queue at its length limit with `reject-publish`), an unreachable broker, a closed
  connection, a connection or channel limit, a broker blocking publishers under a resource alarm, or a confirm that takes over 30 seconds
  retries after two seconds (a blocked target keeps one connection open until the broker unblocks it), so an outage pauses the run. Access refused, a missing exchange or failed
  authentication fails the run with `etl.rabbitmq-publish-rejected`; a store that refuses to record a confirmed route
  fails it with `etl.rabbitmq-delivery-refused`. Without `Elsa.Etl.RabbitMq`, admission refuses the run
  with `etl.rabbitmq-unavailable`. The error route cannot be RabbitMQ.
- **Ordering.** Messages of one batch keep their order; batches of different partitions or shards interleave.

## RabbitMQ ingestion

[Spec 1201](../../specs/1201-etl-rabbitmq-source/spec.md) adds a finite `rabbitmq` source: a host-approved queue drained up to
`maxMessages` (1 to 100,000) when a run prepares, its JSON message bodies read with declared JSON fields as for a JSON Lines
source. Add an `EtlRuntimeFeature.RabbitMqBindings` entry with role `Source` and `AllowedQueues`; `Elsa.Etl.RabbitMq`
provides the consumer. The runtime never declares queues.

- **Manual acknowledgements.** Preparation receives the messages without acknowledging them, writes their bodies as a JSON
  Lines snapshot, and acknowledges them only once the store has recorded that snapshot as the run's prepared input. A
  preparation that fails, is refused or whose worker dies before the record leaves them unacknowledged; the broker
  requeues them, and the next attempt reads them again into a snapshot of its own.
- **At least once.** A worker that dies, or a connection that is lost, after the messages were received and before the
  acknowledgement reaches the broker can leave the run with messages the broker also requeues, so a later run reads them
  again. Records carry no message id, so destinations should be idempotent (a keyed SQL destination is) or tolerate
  duplicates.
- **Bounds and bodies.** A run takes at most `maxMessages` and stops before a message that would take the snapshot past
  `MaxSnapshotBytes`, or the run's retained input (the snapshot, its index of one 24-byte entry per record plus a
  sentinel entry, and the lookup) past `http.maxRetainedInputBytes`; the rest stay queued. A single message larger than that fails preparation with
  `etl.snapshot-too-large` and stays queued. Each message is one record: a body that is not JSON, or not a JSON object, is
  routed to errors as `etl.jsonl.record-invalid` or `etl.jsonl.record-not-object` and still acknowledged.
- **Failures.** An unreachable broker or a closed connection leaves preparation to run again after its lease lapses; access
  refused, a missing queue, a stream queue (which `basic.get` cannot read) or failed authentication fails the run with
  `etl.rabbitmq-read-rejected`. Two runs reading one queue at once take different messages.
- **Requeue limits.** Every preparation that does not record its snapshot returns the messages to the queue. A quorum queue
  counts those returns: by default RabbitMQ 4 drops a message after 20 of them, so a pipeline that keeps failing (a broken
  lookup, a refused record) loses the messages. Give such queues a dead-letter exchange, or a higher `x-delivery-limit`.
- **Acknowledgement timeout.** RabbitMQ closes a channel whose deliveries stay unacknowledged past its `consumer_timeout`
  (30 minutes by default). A run stops taking messages after five minutes (always taking at least one) and leaves the rest
  for the next run, so a slow or distant broker drains a backlog over several runs; a channel closed for that timeout is
  retried. Keep `consumer_timeout` well above the time a preparation takes to record its snapshot.
- **Snapshots of failed attempts.** Each preparation attempt writes its own snapshot, under a random key, so an attempt that
  does not record it leaves a snapshot behind that no later attempt reuses; retention must account for these.

## SQL snapshot reads

[Spec 1198](../../specs/1198-etl-sql-source/spec.md) adds a `sql` source: a host-approved PostgreSQL or SQL Server table,
its declared columns and a key of one to eight required columns. Add an `EtlRuntimeFeature.SqlBindings` entry with role
`Source`, as for destinations; `AllowedTables` must name the table.

- **One point in time.** Preparation reads the declared columns once, ordered by the key, in a single transaction:
  `REPEATABLE READ READ ONLY` on PostgreSQL, which first takes an `ACCESS SHARE` table lock so a read behind a
  truncate-and-reload sees the reloaded rows rather than an empty table (where PostgreSQL refuses the lock, as for a role
  with column-level grants only, a materialized view or a foreign table, the read runs without it; a lock that times out
  or is cancelled never falls back to an unlocked read: the read fails, or retries when the engine classes the error as
  transient), and `SNAPSHOT` on
  SQL Server, which needs `ALLOW_SNAPSHOT_ISOLATION ON` for the
  database (`etl.sql-read-rejected` otherwise). The rows are retained as a JSON Lines input snapshot, and from there the
  run is a JSON Lines run: its partitions, positions and replay are unchanged, every partition sees the same rows, and no
  connection is held while records are processed. A unique key keeps record numbers reproducible; rows with equal keys
  keep the order the engine returns.
- **Recovery.** A worker that dies after publishing the snapshot is replaced by one that reuses it, and a replacement that
  started reading before a lapsed predecessor published adopts that snapshot, so a run never reads a second, different
  point in time. A transient read failure (a connection loss or an unreachable database) records nothing and logs a
  warning, and preparation runs again once its lease lapses, so an outage pauses the run. The read has no command
  timeout and nothing else bounds it: a read blocked on a lock (for example behind `ALTER TABLE` or `TRUNCATE`) waits
  until the lock is released or the worker stops. To detect a database host that vanishes mid-read, give a PostgreSQL
  binding's connection string a `Keepalive` interval (Npgsql has none by default; SqlClient keeps connections alive).
- **Types.** `String` accepts text (retained as raw UTF-8), GUIDs, and timestamps or times of day (ISO 8601 invariant
  text); a `date` column is written as `yyyy-MM-dd`, and an interval or a zoned time of day (PostgreSQL `timetz`, with or without a precision) is refused.
  On PostgreSQL, Npgsql maps `infinity` and `-infinity` to the extreme dates, so a `date`, `timestamp` or `timestamptz` at
  those extremes (including a finite `9999-12-31`) is refused; SQL Server's extremes, such as a temporal table's open
  period end, are real values and are kept; `Int64` accepts 8- to
  64-bit integers; `Decimal` accepts exact decimals within .NET `decimal` range and integers; `Boolean` accepts booleans
  (`bit` on SQL Server). Any other value, including a floating-point column, fails preparation with
  `etl.sql-column-type-unsupported`, naming the column. A NULL in a required column is a record error on the error route.
- **Limits.** The snapshot is bounded by `MaxSnapshotBytes` (`etl.snapshot-too-large`); a row longer than
  `MaxRecordBytes` is a record error, `etl.record-too-large`. A SQL source is finite only. SQL Server's driver replaces
  an unpaired surrogate in `nvarchar` text with U+FFFD before the run sees it. A driver materialises each value before
  the size check, so one very large value is held in memory. An error the engine classifies as transient pauses the
  run even when it is a misconfiguration, such as a wrong SQL Server database name (error 4060); the worker's warning
  names the exception type, not the error number.
  Authored queries and log CDC are not supported.
- **Watermark reads.** [Spec 1199](../../specs/1199-etl-sql-watermark/spec.md) adds an optional `watermark`: a declared,
  required `Int64` column. A row whose watermark is NULL is read on every run and rejected as a record error, so it
  is never silently skipped. A run then reads only rows whose watermark is above the high watermark of the last completed
  run of the same definition, source alias, source binding revision (the pinned handle, which covers the database, user
  and allowed tables), table and column. The first run reads every row, and so does the first run after any of these
  changes, including a new binding revision, so a non-idempotent route then receives the table again. The checkpoint
  does not include the destination: a new definition version that routes to a new, empty target reads only rows above
  the checkpoint, and loading that target from the start needs a new source identity, as there is no checkpoint reset
  yet. The run's highest watermark
  is raised into that checkpoint in the transaction that completes it, so a run settled by a failure (a fault, a
  cancellation or a rejected delivery) leaves it unchanged and the next run reads those rows again. Runs of one
  watermarked definition must not overlap: the checkpoint never moves backwards, but a run that read a row before a
  newer run delivered it can finish last and write the older row over the newer one in a keyed destination, and the
  checkpoint, already past the row's newer value, does not read it again until it changes. Nothing enforces this yet
  ([#3721](https://github.com/elsa-workflows/elsa-foundation/issues/3721)); start the next run only once the previous
  one has settled. **A row whose watermark is at or below a checkpoint
  already passed is never read.** Values drawn when a row is inserted, such as a sequence or identity, can commit out of
  order under concurrent writers: a run can read value 101 while the transaction holding 100 is still open, and row 100,
  committed later, is then skipped. A per-row version counter is not a watermark either. Use a column whose values
  become visible in increasing order, for example one written by a single writer or by a job that stamps rows after
  their transactions commit; otherwise load the table with snapshot reads. Such a job should leave unstamped rows at a
  value at or below every checkpoint, such as 0, not NULL: NULL rows are rejected as record errors on every run until
  they are stamped, so the error route fills with rows a later run delivers. Running the definition repeatedly, for
  example from a scheduled workflow, loads the table incrementally; rows deleted from the source are not propagated.
- **Upgrades.** A worker without this slice cannot read a `sql`-source artifact: its preparation claim fails and is
  retried, without failing the run, and can hold up other preparations in the scope. Upgrade every worker in a scope
  before such a pipeline is admitted.

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
  poll; that orphaned page stays on disk. Because every claim compacts every shard, a shard
  whose rows fail the compaction's safety check (which needs corruption) fails every claim whose candidate is in its run,
  even when the candidate is a peer. Claims pick the first eligible shard in a fixed order, so this stalls claims across
  the scope, as a corrupt candidate's own audit already did. The first claim after upgrading a long-running run compacts
  every shard's whole history in one write transaction; peers' lease renewals wait on it, and a slow one can cost a
  lease. This happens once and loses nothing.

## Implemented boundaries

- Core: typed finite topology, exact nullable-tenant/required-persistence scope, immutable versioned artifact envelope, typed JSON representation and SHA-256. Complete v1 envelopes are limited to 256 KiB; a definition may request a lower limit. Unknown saved formats remain observable and are refused at publication/read boundaries.
- `EtlDesign`: scoped compiler with structured diagnostics, schema/key/mapping/filter/limit validation and deterministic behavioral hash. Artifact identity also includes definition/version provenance. Caller-owned collections are snapshotted for publication.
- `EtlDesignEntityFrameworkCore`: drafts and immutable publications on SQLite, SQL Server, PostgreSQL or MySQL ([spec 1202](../../specs/1202-etl-design-store-providers/spec.md)); optimistic revisions, atomic version publication, compiler-backed artifact admission, schema/migration metadata and separate `EtlDesign` history. Key columns use each engine's binary collation, and a publication that meets a transient write conflict (a serialization failure, a deadlock, a lock timeout or, on SQLite, a busy or locked database) is retried as a whole, and a draft update's one conditional statement and every other store and catalog read retry the same way. A real host AutoMigrate/publish/stop/Validate/reload test covers its registration on SQLite, and the `etl-ef` container suite runs the design-store contracts on the three server engines. The SQLite chain under `Migrations/Sqlite` is hand-maintained; the server engines' migrations are generated with `tools/etl/generate-etl-design-migrations.sh`.
- Runtime processing: bounded strict UTF-8 CSV parsing and byte offsets, typed exact-key lookup/map/filter, safe row diagnostics, immutable host binding pins, verified retained snapshots and a streamed logical-record index. Partition ranges are computed without allocating a range per requested partition.
- Physical files: attempt-private staging, bounded writes, flush/close/hash verification, atomic create-only publication, matching-content replay and conflict refusal. The internal terminal materializer streams only manifest-accepted chunks into deterministic main/error JSONL routes, verifying exact pinned revisions/content and labelling cancelled/faulted output partial. Unix publication requires hard-link support; there is no copy fallback. Windows uses its no-replace move. A physical file is not an accepted ETL result.

- `EtlRuntime`: bounded background attempts, retained executable validation, independently leased preparation/partitions and process-local record/input/lookup/output allowances. The Tasks lifecycle starts/stops shell workers; plain hosts use the same instance as a hosted service. Compute uses explicitly configured scopes, retained binding revisions and lookup row/cell ceilings. Terminal delivery discovers pending work in the composition's runtime database independently of the current binding catalog, validates each retained invocation's exact scope and run identity, and creates a scoped native operation. Removing file bindings therefore does not orphan an already-owed result. Invalid candidates remain pending while valid later candidates can progress; this is not a tenant-default fallback or permission to execute files outside configured bindings.
- `EtlRuntimeEntityFrameworkCore`: separate runtime context. Its schema is qualified on SQL Server, PostgreSQL and MySQL as well as SQLite ([spec 1203](../../specs/1203-etl-runtime-store-schema/spec.md)): run keys are a 64-character hash of the run id there (the id itself is kept in full), check constraints are rendered per engine, and the runtime-store contracts pass on all four. Its registration now selects all four engines after [spec 1206](../../specs/1206-etl-runtime-concurrency/spec.md)'s store concurrency controls; the native Flowchart → JSONL ETL → Done journey and completed-state host restart pass with both ETL stores on each server ([source-bound receipts](../../specs/1206-etl-runtime-concurrency/evidence/workbench-journey.json)). Native workflow persistence in this proof remains SQLite; mid-work/old-writer recovery and Studio are separate acceptance. It retains the existing schema and migration history; short idempotent admission, pinned bindings, immutable preparation metadata, durable batch reservations, fenced manifest/cursor/quota settlement, first-wins terminal result and recoverable delivery outbox. Runtime DI/database composition does not require design services or design tables. The shared persistence package still references the design assembly.
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
bindings. They use SQLite, the only engine qualified for both ETL stores; they are not production deployment templates.
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

Snapshots and output use a shared filesystem whose atomicity and durability the host must support. A failed preparation can leave a complete unreferenced source snapshot (for a RabbitMQ source, under a random per-attempt key rather than a deterministic one); process loss can leave attempt staging. Retention must account for these candidate keys and remove abandoned files only after their writer has stopped. No automatic orphan collector is implemented. No power-loss durability qualification is claimed by the process-level tests.

Physical effects remain at least once. Lease expiry cannot terminate a worker or release its live buffers. Only the fenced manifest/cursor transaction makes output authoritative. Logical payload allowances are not a heap/RSS limit, and retained chunks are not automatically removed or compensated.
