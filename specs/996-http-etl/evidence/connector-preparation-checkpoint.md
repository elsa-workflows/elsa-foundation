# Connector admission and preparation checkpoint

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), draft
[PR #3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156), partial T005.
This checkpoint does not enable HTTP execution or complete T005.

## Implemented boundary

The optional `IEtlConnectorRuntimeStore` capability shares the same scoped EF store instance with
`IEtlRuntimeStore`. It implements admission, snapshot reads, preparation and partition ownership.
Common preparation renewal, cancellation, terminal settlement and native delivery remain on the
existing store contract. Existing v1-only providers are not required to implement the new interface.

An additive migration creates companion connector-run and connector-partition tables with restrictive
foreign keys, concurrency revisions and schema stamps. Existing runtime tables, migrations and v1
cursor meanings are unchanged. Bounded canonical snapshots preserve pinned aliases/revisions and
explicit optional lookup state. CSV ranges and HTTP shard/request positions have distinct types.
Preparation commits its input snapshot and all initial partitions atomically with run readiness.

The legacy file preparation query excludes connector runs. File preparation cannot consume a
connector lease, and binding-resolution refusal cannot terminalize an already admitted connector.
Shared success settlement validates prepared state, the expected partition set and typed completion;
empty CSV input can complete without inventing work. HTTP partitions remain incomplete until future
page transitions establish exhaustion. Connector partition failure proposals use the current owner,
fence and trusted-clock expiry. Save-time expiry rolls back ownership/preparation mutations.
Cancellation retains admitted state and prevents further claims or renewal; it does not recall
external work or prove physical worker termination.

## Review and evidence

Root integration includes a bounded delegated schema review and a separate schema implementation.
The first focused run passed 50 store tests. A later review found that retained-input accounting
accepted an in-range overcount. The added regression failed before the correction (no exception was
raised), then passed after requiring exact agreement with prepared input at this stage. The expanded
focused run passed 53 store tests with zero failures or skips. Three further invalid-admission cases
cover connector-kind mismatch, duplicate shard identity and an invalid page limit in the broader run.

The first full run passed 536 tests and failed eight process-worker tests at startup: the worker
instrumentation requires an implementation-type registration, but the initial DI alias change replaced
it with a factory. The correction retains the original `IEtlRuntimeStore` registration and resolves the
optional connector capability through that same instance. No worker test or deadline was weakened;
the corrected full suite passed **544/544**, zero failures/skips. The subsequently strengthened replacement-ownership case also passed **1/1**, including successful fault settlement by the current owner.

Tests exercise actual SQLite migrations and transactions, scoped DI identity, immutable admission
pins, replay/conflict, file/connector dispatch separation, two shard claims, replacement fencing,
empty/nonempty CSV preparation, invalid snapshots, unknown or inconsistent positions, cancellation,
and expiry during preparation, claim, renewal and release. The existing retained-v1 fixture tests
remain part of the full ETL suite. The final full-suite, architecture, map and diff results are recorded
on the PR receipt, scoped to the checkpoint commit; a skipped or pending gate is not a pass.

## Limits and remaining work

All ETL workers must be upgraded before connector admissions are enabled. The additive schema
preserves v1 data, but old binaries cannot recognize the companion marker and are not safe concurrent
consumers of connector runs. Downgrade refuses to drop accepted connector state; restore a verified
backup when a database downgrade is required. See the [state model](../data-model.md).

T005 still needs retained page attachment and request history, typed owed batches, route-body/effect
state, durable attempts/deadlines/retry eligibility/acknowledgements and atomic acceptance/accounting.
Those additions must extend terminal quiescence and retention validation to include all owed work.
The runtime still returns `etl.connector-runtime-unavailable` for connector execution. Transport,
native/API/Studio acceptance and complete old-worker/native resume remain outstanding. Task-runner
and workflow/queue pause support remain post-program follow-up scope.
