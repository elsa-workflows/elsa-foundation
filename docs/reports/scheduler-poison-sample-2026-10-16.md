# Scheduler poison sample: 16 October 2026

Investigation for [#1241](https://github.com/elsa-workflows/elsa-foundation/issues/1241).
Program-goal state: `none/free-flow`.
Source baseline: `6b3ad8c3dbee6c4b62f9dcc295c6cf3d5450708b`.

## Finding

The available local sample is empty. Two running Foundation demo backends had **zero scheduler poison
records and zero incidents** when inspected. This run observed no handler failures in those databases
to classify as transient infrastructure failures or deterministic dispatch defects. Historical
running-QA reports describe deterministic expression/input failures, with the limits below. None of
these observations establishes the frequency of either classification in production or shows that
retrying would have succeeded.

Keep the current retry default unchanged in this investigation. The sample supplies no empirical
basis for selecting a bounded retry default, and does not prove that `DoNotRetry` is optimal. A policy
change needs a representative observed sample and evidence about safe, successful re-drive.

## Observed environments

Read-only SQLite transactions at **2026-10-16 21:14:29 UTC** counted the following rows. Both databases
belonged to running local backend processes, discovered through their open database files rather than
connection secrets. These are development/demo environments, not a production deployment. Their
deployed build revisions were not attested; the source baseline above identifies the separate
contract trace, not these binaries.

| Environment | Workflow execution states | Activity execution states | Scheduler poison records | Incidents |
| --- | ---: | ---: | ---: | ---: |
| HTTP usability demo, backend listening on localhost:5097 | 11 | 33 | 0 | 0 |
| Workflow test-drive demo, backend listening on localhost:5384 | 11 | 37 | 0 | 0 |

Both runtime databases contained `elsa_runtime_scheduler_poison` and `elsa_runtime_incident_state`.
Their respective diagnostics databases contained neither table; absence there was not counted as a
zero-row poison store. Counts describe the retained rows at one instant. They do not measure lifetime
failure counts, workload volume, retention, in-memory records, or faults whose poison write failed.

Two running PostgreSQL demo containers were also discovered. A read-only schema query in
`elsa-demo-pg`'s `elsa` database found no poison/incident tables. That database contributes no sample.
The other container, `elsa-demo-postgres`, was not queried successfully: local socket connections using
`postgres` and `elsa` were rejected because those roles do not exist. No credentials were read or
changed. No production endpoint, production database, or sanitized production export was supplied to
this run. Other running hosts without an accessible database sample were not assumed to be empty.

From the databases, only schema names and aggregate counts were collected. No workflow payload,
customer identifier, connection string, raw fault message, or database content export is attached.

### Reproducing the count

Against an authorized runtime SQLite database, open it with URI `mode=ro`, set `PRAGMA query_only=ON`,
and run these queries in one read transaction:

```sql
BEGIN;
SELECT COUNT(*) FROM elsa_runtime_scheduler_poison;
SELECT COUNT(*) FROM elsa_runtime_incident_state;
SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state;
SELECT COUNT(*) FROM elsa_runtime_activity_execution_state;
ROLLBACK;
```

Check `sqlite_master` first; a missing table is an unavailable store, not an empty sample. Keep the
normal WAL view: an immutable-file connection could omit live WAL transactions. No fixture was
created and no failure was injected into another session's running environment.

## Current mechanism and evidence boundary

The earlier [comparison, section 4.7](execution-model-comparison-2026-08.md#47-the-default-retry-policy-is-do-not-retry-but-the-failure-is-visible)
corrects its original silent-poison claim. Its Groundwork-lane store description is historical; the
handler-crash, poison-record and incident-projection flow remains current. Inspect the current host
composition when collecting data.
[PR #786](https://github.com/elsa-workflows/elsa-foundation/pull/786), which the issue described as
awaiting QA, merged on 18 July 2026.

At the source baseline, the [poison record](https://github.com/elsa-workflows/elsa-foundation/blob/6b3ad8c3dbee6c4b62f9dcc295c6cf3d5450708b/Essentials/src/Workflows/Runtime/Core/Models/RuntimeSchedulerPoisonRecord.cs)
contains execution/work-item identity, command kind, handler, outer and first-inner fault, failure
count, disposition, first/last failure times, optional next retry time and metadata. It does not store
the full work-item payload. [Crash recording](https://github.com/elsa-workflows/elsa-foundation/blob/6b3ad8c3dbee6c4b62f9dcc295c6cf3d5450708b/Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/WorkflowSchedulerDrainer.cs#L384)
replaces the latest fault for a repeated work item while retaining the first timestamp and incrementing
the failure count. It is not a per-attempt fault history.

The [current fault-behavior guide](../runtime-fault-behavior.md#how-it-becomes-visible) and
[default retry policy](https://github.com/elsa-workflows/elsa-foundation/blob/6b3ad8c3dbee6c4b62f9dcc295c6cf3d5450708b/Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Incidents/NoopRuntimeDomainRetryPolicy.cs)
describe explicit `DoNotRetry` and a process-local default poison store; durable retention depends on
host composition. The [EF poison store](https://github.com/elsa-workflows/elsa-foundation/blob/6b3ad8c3dbee6c4b62f9dcc295c6cf3d5450708b/Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfWorkflowSchedulerPoisonStore.cs)
uses `ContentJson` as the authoritative record, with scoped identity, time projections and row revision.
An empty database table alone does not attest that a host used that store.

The [incident observer](https://github.com/elsa-workflows/elsa-foundation/blob/6b3ad8c3dbee6c4b62f9dcc295c6cf3d5450708b/Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Incidents/PoisonedSchedulerWorkIncidentObserver.cs#L151)
projects `Poisoned` records after a faulted drain; `RetryScheduled` records are not projected as poison
incidents. Projection is best-effort, so a crash or incident-persistence failure can leave a poison
record without an incident. Default capture retains type/message and optionally a stack trace, not
the complete inner-exception chain. A custom `IRuntimeDomainRetryPolicy` can return `RetryAfter`,
which records `RetryScheduled` with `NextRetryAt`, but the item is parked: nothing re-drives it
automatically and no incident is raised
([#3275](https://github.com/elsa-workflows/elsa-foundation/issues/3275)).

### Historical running-QA observations

Existing committed reports do contain incident observations from real running hosts. They are
separate from this run's empty database sample and do not provide raw poison-record exports:

| Evidence | Observed cause | Classification limit |
| --- | --- | --- |
| [Incident troubleshooting QA, 2 October](incident-troubleshooting/analysis-and-plan.md#reproduction-and-scope) | An intentionally undeclared JavaScript variable produced a Critical, Blocking `SchedulerWorkPoisoned` incident with a Jint exception. A pre-existing undefined-variable incident was also inspected. | The induced case has a deterministic expression/input cause; it is not evidence of a transient service failure or a dispatch-code defect. The pre-existing case's creation provenance is unspecified. |
| [Runtime DB accounting, direct REST control](runtime-db-access/baseline-accounting.md) | The controlled direct REST attempt recorded `ExpressionInputFailureException`, with an inner Jint error reading a property from null. The report retains unsuccessful controls in both cadence captures. | Deterministic fixture/input mismatch as reported; no successful same-input re-drive is demonstrated. The captures do not form an independent production-frequency sample. |

These observations show that the issue's proposed two categories need an additional distinction:
authored expression/input failures can reach the poison path too. Repeating an unchanged invalid
expression is not evidence-backed recovery. Neither historical report establishes a spontaneous
production fault distribution or validates a global retry default.

Regression tests can prove recording, projection and retry mechanics with injected failures. They
cannot provide a real-deployment fault distribution, and no synthetic exception is included in the
observed sample above.

## What would settle the retry question

An environment owner can provide a sanitized export from a known runtime poison store for a stated
observation window. Record the deployed revision, provider, configured retry/capture policy, workload
and retention boundaries, and whether poison persistence was actually enabled. Include all retained
poison records in the window, rather than only the incidents visible in Studio; projection can lag or
fail independently.

For each record, retain the handler and command kind, outer and first-inner exception type, sanitized
cause or provider error category, failure count, disposition, first/last failure times, next retry
time, and policy reason. Use a stable anonymized correlation key for scope/execution/work item so
repeated failures are not mistaken for independent samples. Do not export activity inputs, secret
values, raw connection strings or unrestricted exception details.

Classify observations with evidence:

- **Deterministic dispatch defect:** the same failure repeats for the same input/revision and has a
  reproducible code or invariant cause; retry without changing that cause is not a remedy.
- **Deterministic authored input/expression failure:** the recorded cause identifies an invalid
  expression or input; distinguish it from a defect in dispatch code. Re-drive needs a corrected input
  or definition, not an assumption that infrastructure recovered.
- **Transient infrastructure candidate:** the fault has a documented temporary provider/service cause.
  Count it as a demonstrated retry success only when a safe re-drive after recovery actually succeeds.
- **Unknown:** capture is redacted, the cause is missing, provenance is ambiguous, or re-drive safety and
  outcome are unverified. Preserve this category instead of treating every I/O exception as transient.

Collect corresponding incident/projection and poison-write error observations separately. A zero-row
store can coexist with a failed poison write. Before recommending retries, verify idempotency and
fencing for the affected handler and distinguish a custom-policy successful retry from a hypothetical
success under a different default. Collection or a retry-policy change is follow-up work, not part of
this finding.
