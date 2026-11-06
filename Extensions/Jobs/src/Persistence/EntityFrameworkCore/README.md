# Jobs EF persistence

This module owns Jobs admission, immutable invocation content, attempts, pause/capacity arbitration,
physical receipts, authoritative results, schedules and owed model delivery. It uses the shared EF
connection/schema/migration policy and bounded write retry support. It is not a generic store framework.
The product package contains EF Core/Relational and shared Elsa EF infrastructure; the consuming host
supplies the SQLite or PostgreSQL engine package. Other engines are refused.

`services.AddJobsEntityFrameworkCore(options => { ... })` composes a plain .NET host. Register it before
the Jobs worker: its existing `AddEfModuleHostMigrations` hook validates/applies schema before later
hosted services start. `JobsEntityFrameworkCoreFeature` composes the same authority in a CShell using
the normal shell migration hook. Both preserve the shared migrate-versus-validate configuration,
provider validation, schema finalization and write admission. Production code never uses EnsureCreated.
Provider contexts remain internal, with friendship only for same-domain tooling and provider tests.

`IJobCommands` and `IJobQueries` are scoped replacement contracts. The default query registration follows
the selected command provider if it also implements queries; a replacement must otherwise provide its
own query implementation. Each authoritative write attempt uses a fresh factory context and one whole
transaction. Retry never continues a partially failed transaction. Commands propagate caller cancellation
and preserve provider failures behind `JobPersistenceException`; retained malformed content has a safe
`JobException` code and preserved inner cause.
Database-retained JSON uses a separate decoder that normalizes constructor validation failures;
caller cursors and preflight snapshots keep their caller-error classification.

Indexed keys are SHA-256 addresses over exact scope/identity framing, with residual identity checks.
Strings use the shared provider ordinal collation. An admission recomputes the Core request digest;
physical receipts recompute the Core payload digest. Ordinary producer recognition expires only after
the advertised window and settled responsibilities; admission can then atomically replace that ordinary
key. Durable-origin receipts and cancellation tombstones are separate and are never removed by ordinary
key cleanup. Inputs, attempts, results, origins and deliveries are not purged by this implementation.
Input, result and continuation envelopes share Core's 1 MiB limit with the runtime; complete requests
are bounded to 3 MiB and physical receipts to 2 MiB. Payloads share Core's maximum JSON depth of 32,
leaving room for durable record envelopes. Progress shares the entire UTF-8 envelope's 16 KiB bound.

Claim discovery filters compatible support, implementation pins, active holds and exhausted live permit
counts before its bounded selection. Queue/type gates have no model namespace. Workflow definition and
instance gates require the exact model kind, so colliding model-local IDs remain independent. Every gate,
including an allowed gate, participates in an atomic start CAS. Membership rows make held work skippable
on the shared queue. A persisted per-worker scan cursor advances across examined candidates; a malformed
current-schema request retains its bytes/JobId and receives an unavailable projection without spending an
attempt. `Find` reports that malformed record as unavailable rather than manufacturing valid content.

The first start pins a trusted host registration's implementation identity. Retries/recovery require that
same pin. Missing old support stays accepted without an attempt. Start, renewal and capacity changes
arbitrate through the same scope revisions; permits renew with their exact attempt. Provider time comes
from SQLite integer seconds/milliseconds or PostgreSQL `clock_timestamp()`. Lease-authorized writes
recheck live database time after all writes and roll back if authority expired while waiting. Ownership
revision is separate from physical-start/progress observations. Completion replay cannot release a
successor's permits. Late receipts retain evidence without selecting a result or reviving ownership.

Deployment ceilings count valid authorized leases. A stale process may continue physical effects after
lease loss; these limits are not a strict ceiling on every physical handler. External effects still need
handler/integrator idempotency. Pause never cancels an already permitted attempt.

Schedules use the host's `IJobRecurrenceCalculator` for arithmetic, with no workflow dependency or second
scheduler authority. The provider accepts zero or one calculator and refuses multiple replacements when
the authority resolves. Without a calculator, schedule arithmetic reports `jobs.recurrence.unavailable`;
the due cursor remains retryable. `AddElsaJobs` supplies default UTC interval/cron arithmetic and its
bounded worker schedule loop; the EF provider alone installs neither. Materialization checks the same holds, commits origin-protected acceptance with
cursor/occurrence receipt, and consumes no execution permit. Default coalescing counts long outages in
persisted finite arithmetic slices; it accepts the oldest owed occurrence once counting reaches the
persisted first-slice cutoff. Time elapsed during counting remains due for the next cycle. Explicit replay
materializes bounded pages. Accepted JobIds are never coalesced.
Updating a definition with already due responsibility is refused rather than silently discarding it.
Each schedule's bounded arithmetic slice is calculated before occurrence admission or cursor changes.
Malformed supported definitions receive a durable unavailable marker while their definition bytes,
owed cursor, partial catch-up state and accepted occurrences remain protected. Unsupported schemas and
marked schedules are filtered before bounded selection. A missing calculator advances fair discovery
without permanent quarantine. Inspection returns a nullable definition, stable validated schedule ID
and safe diagnostic; legacy identities supply that ID without a schema backfill or v1 serialization change.

Awaited results create retained delivery responsibility in the result transaction. Workflow holds gate
new continuation permission; queue/type holds do not. Replaying a prior grant resolves the same result
and model input while paused. Only a trusted owning model adapter may assert consumption/terminal-parent
proof; transport acknowledgements are insufficient. Native/BPMN integration remains separately qualified.

SQLite and PostgreSQL source bindings and migrations are not provider qualification. The real-engine
suite under `tests/Persistence/ProviderTests` applies and validates retained migrations, exercises durable
identity/receipt/pause/concurrency/recurrence controls, and keeps unavailable PostgreSQL distinct from a
passing engine. Packaged consumers and independent-process recovery remain separate acceptance evidence.


Operation receipts additionally retain a nullable `AuditJson` envelope, atomically with the original
command/result: cancel, cancel-origin, retry-as-new-run, pause, release-hold, capacity, save-schedule
and schedule-enabled. The envelope is limited to 32 KiB UTF-8/depth 16 and contains only original
`JobOperation`, provider transaction time, command fingerprint/kind, bounded typed targets and the
first confirmation. It stores no whole request, input, result, continuation or progress payload.
Replay still uses the original receipt digest/result and never reads, repairs or restamps audit
metadata. The new reads explicitly retain legacy-unavailable and corrupt-unavailable entries while
healthy rows on the same page remain readable; an unprovable receipt address fails safely.

Audit listing uses the operations `(ScopeKey, Key)` index and the existing scope-bound keyset cursor.
It is global within the supplied authorized scope, not a chronological or complete per-job query.
The additive audit migration, real-engine controls and API/Studio integration require M4
qualification; audit source work is not migration/application or acceptance evidence.

Terminal payload retention defaults to indefinite. Hosts can explicitly set
`TerminalInputRetention` and `TerminalResultRetention` on `JobsEntityFrameworkCoreOptions`.
Each admission captures those durations separately from the immutable v1 request; later configuration
or restart cannot shorten an accepted promise. Legacy records without a captured policy remain
indefinite. Deadlines use provider terminal time, and actual expiry occurs only when a trusted host
invokes bounded `IJobCommands.ExpirePayloadsAsync`; reads never run cleanup. A host can schedule that
maintenance command independently of workers, including while processing is paused. Follow its
scope-bound continuation token to finish a pass.

Cleanup requires a terminal job, no active attempt, and exact settled delivery/grant/ACK proof when
an awaited association requires it. Unsettled, missing or corrupt proof cannot authorize cleanup.
Expiry clears full input/result envelopes, progress and copied physical-report payloads. A late
physical report is fully validated before retaining only its metadata after result expiry; row-token
arbitration prevents a concurrent late report from restoring purged bytes. Cleanup does not change
the selected result, terminal revision or original request/payload/receipt digests.

Request/acceptance/result/association metadata, ordinary-key recognition, durable origins, attempts,
grants, ACKs and consumer proofs remain indefinitely. This explicit metadata policy does not promise
indefinite ordinary-key recognition: its existing window and settled-obligation cleanup still apply.
`JobSnapshot` exposes explicit payload states, compact metadata and captured/actual expiry times.
Expired input cannot be manually retried; `PayloadExpired` is an explicit refusal. The original full
physical-report query refuses `jobs.payload.expired`; `ListPhysicalReceiptInspectionsAsync` continues
to expose exact report identity and original digests without inventing a successful null-valued report.
The retention migration's rollback validates payload availability before changing the schema. Expired
markers or missing protected bytes refuse rollback without replacing JSON or discarding expiry proof.
Unexpired rows can return to the older schema; upgrading them again treats them as legacy indefinite
records because that older schema has no captured retention policy. Database application/rollback
controls and the separate packaged retention witness remain required qualification; generated source
does not update prior milestone receipts.
