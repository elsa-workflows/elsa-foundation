# Packaged standalone Jobs witness

The separate opt-in payload-retention slice uses an already published current-source consumer:

```sh
python3 tools/jobs/PackageProbe/retention_witness.py \
  --entry-assembly /absolute/current-deployment/Jobs.PackageProbe.dll \
  --evidence-dir /tmp/owned-jobs-retention --provider sqlite
```

Select `postgresql` for its owned temporary container profile. This controller runs actual report
handlers, captures finite versus indefinite admission policy, restarts producer/inspector processes,
executes explicit bounded cleanup, reconciles client keys and durable origins, refuses retry after
input expiry, and appends a late report without restoring payload bytes. It retains its own case
states/receipt and does not alter or promote the M1/M2 or model plans. The supported probe commands
are `expire-payloads` and `receipt-inspections`; admission accepts optional `terminalInputRetention`
and `terminalResultRetention` durations. Fresh commands cannot replace an already accepted policy.
This new controller is source-only until actual execution and package provenance are retained.

`../verify-packaged-jobs.py` freezes a full source SHA and publish-state SHA, computes the Jobs
package closure, packs that exact tree, and verifies archive provenance. `--stage publish` creates a
clean console application outside the checkout with exact `PackageReference` versions and a fresh
NuGet cache, restores in locked mode, and publishes it. The receipt verifies the selected archives and
deployed Jobs assemblies and retains the complete external package versions, restore diagnostics and
provider asset hashes. No package feed is published or changed.
The execution receipt also records the actual engine's `sqlite_version()` or PostgreSQL `version()`
result, rather than inferring a database version from its package or container tag.

`--stage execute` then runs `run_witness.py` against that deployed application, from its deployment
directory. It launches separate producer, inspector and worker processes using the public Jobs API.
The input is a useful report with two line items, five units and a calculated subtotal of 52.00.
The report also records attempt entry files, which provide barriers for the crash and pause controls.

```sh
python3 tools/jobs/verify-packaged-jobs.py \
  --source-ref FULL_SOURCE_SHA \
  --publish-state-ref FULL_PUBLISH_STATE_SHA \
  --evidence-dir /tmp/elsa-jobs-package-evidence \
  --provider sqlite --stage execute
```

Use `--provider postgresql` for the same executable and checks against an owned local PostgreSQL 16
container. The controller creates its own database, random credentials and container identity, then
removes the owned container. It accepts no external database connection string. SQLite uses an owned
file in the evidence directory. Both profiles initialize retained migrations once and start every
subsequent process in schema validation mode.

The focused witness checks ordinary-key replay/conflict, a deliberately discarded producer acceptance
response, unsupported-handler preservation, a useful retained typed result across fresh processes,
acceptance while paused, the durable start-permission cutoff, force-killed worker recovery, rejection
of a stale completion, and a stable invocation idempotency key across attempts. Deliberately discarding
a producer response is recorded separately from actual provider commit-ack fault injection. A stable
invocation key is not evidence that a downstream physical effect deduplicated it.

The controller signals only process sessions it created. Each command, result, log, case and process ID
is retained in the owned evidence directory. A failed or unavailable profile fails its receipt; it is
never silently counted as passed. This is one standalone slice. Full scheduling, retry, security,
native/BPMN integration and composed Studio acceptance retain their own gates.
Both scripts refuse optimized Python execution because their witness assertions must remain enabled.

The C# probe is a trusted qualification host, not a product CLI or operational API. Its JSON command
files are created by the controller and must not be exposed as a service to untrusted callers.

## Bounded control groups

The driver keeps `--witness-group m1` as its default. Additional groups may be selected explicitly
after the corresponding runtime/provider source has been committed:

| Group | Controls |
|---|---|
| `schedules` | Provider-time delayed grants; actual default interval/cron pump; held catch-up, restart and bounded explicit replay. |
| `coordination` | Four-scope overlapping holds and version coverage; two live workers; each authorized-lease capacity scope; healthy work past 1,001 held jobs. |
| `attempts` | Paused automatic retry; cancellation and absolute provider deadline; graceful drain and grace-expired recovery; linked idempotent Retry as new run. |
| `chaos` | Real post-COMMIT store-response failures; controlled pause/start order; stale physical overlap and durable downstream effect deduplication. |
| `all` | The original M1 slice followed by all bounded groups. |

New controls use a distinct registered v2 input/result schema and implementation pin. They do not
add chaos fields to the accepted v1 report input or change the v1 handler's semantics. Synthetic
`qualified-model` addresses test Jobs association gates only. Native and BPMN model integration and
operational authorization still require their own qualification.

The fault observer uses public EF `DiagnosticListener` events and public change-tracker metadata.
It records a classified write at `SaveChangesStarting`, correlates the exact context/transaction at
`TransactionCommitted`, and throws one controlled nontransient `DbException`. Each reply-loss case
requires the identical exception instance at the matching `TransactionError`, a fresh durable read,
one observed propagation and no barrier timeout. This is a post-COMMIT store-response fault; it does
not claim that an actual network acknowledgement packet was dropped.

PostgreSQL's pause-before-grant control blocks after selection and before saving. SQLite starts its
write transaction with `BEGIN IMMEDIATE`, so the comparable control blocks an explicit direct claim
at public `TransactionStarting`, armed after host validation. Its receipt states that selection has
not occurred. Both require an external Pause to commit before the barrier is released. A timeout or
rolled-back claim cannot satisfy the evidence gate.

The controller also supports deliberately failing evidence controls, for example:

```sh
python3 tools/jobs/PackageProbe/run_witness.py \
  --entry /absolute/deployment/Jobs.PackageProbe.dll \
  --evidence-dir /tmp/jobs-marker-only-claim \
  --provider sqlite --group chaos --negative-control marker-only-claim
```

This removes the throw while retaining the committed marker and normal progress. The controller must
exit nonzero and record `FAILED` because no exact exception propagation occurred. Matching controls
exist for `admit`, `renew` and `complete`. Negative receipts remain failed evidence controls; they are
never relabeled as a passing product run.

The downstream destination is a separate owned loopback HTTP process with a durable SQLite ledger.
Its unique invocation-key row and request rows commit together. The overlap case restarts that
destination, lets two physical attempts submit the same invocation key, and requires two requests,
one effect and an unchanged authoritative job result after the stale attempt exits. This proves the
explicit destination's idempotency behavior, not universal exactly-once external effects.

For an admitted remote qualification unit, set `ELSA_JOBS_QUALIFICATION_OWNER` to its bounded,
nonsecret owner identity. Every owned PostgreSQL container records namespace/program/owner/run labels
and its exact ID in `owned-container.json`. An abrupt-unit cleanup can use those recorded identities
and verify their labels; the controller never performs broad container cleanup. The local default is
`local-elsa-3586`.
